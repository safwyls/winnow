using System.Globalization;
using System.Net.Http.Headers;
using Winnow.Enrich.Updates.Model;
using Winnow.Enrich.Updates.Storage;
using Microsoft.Extensions.Logging;

namespace Winnow.Enrich.Updates;

/// <summary>
/// <see cref="IBuildInfoClient"/> over api.steamcmd.net, per
/// <c>docs/spikes/update-signals.md</c> §1.
/// </summary>
public sealed class SteamCmdBuildInfoClient : IBuildInfoClient
{
    /// <summary>Named/typed <see cref="HttpClient"/> for the build-info endpoint.</summary>
    public const string HttpClientName = "steamcmd-info";

    /// <summary><c>metadata_cache.provider</c> for the bodies this client stores.</summary>
    public const string CacheProvider = "steamcmd";

    private readonly HttpClient _http;
    private readonly IUpdateSignalCache _cache;
    private readonly UpdateSignalOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<SteamCmdBuildInfoClient> _log;

    public SteamCmdBuildInfoClient(
        HttpClient http,
        IUpdateSignalCache cache,
        UpdateSignalOptions options,
        TimeProvider clock,
        ILogger<SteamCmdBuildInfoClient> log)
    {
        _http = http;
        _cache = cache;
        _options = options;
        _clock = clock;
        _log = log;

        _http.BaseAddress ??= _options.BuildInfoBaseAddress;
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", _options.UserAgent);
        }
    }

    /// <summary>Cache key for one app's build info.</summary>
    public static string AppCacheKey(string appId) => "app:" + appId;

    public async Task<BuildInfoFetch> GetPublicBranchAsync(
        string appId, TimeSpan? cacheTtl = null, CancellationToken ct = default)
    {
        if (!IsAppId(appId))
        {
            return BuildInfoFetch.Unavailable;
        }

        var key = AppCacheKey(appId);
        var cutoff = Cutoff(cacheTtl ?? _options.BuildInfoCacheTtl);

        if (await _cache.GetAsync(CacheProvider, key, ct) is { } cached && cached.FetchedAt >= cutoff)
        {
            if (cached.PayloadJson is null)
            {
                // A cached miss: the service answered and had nothing for this
                // appid. Re-asking spends the volunteer service's bandwidth to
                // relearn the same nothing.
                return BuildInfoFetch.NoDataCached;
            }

            var cachedBranch = UpdateSignalJson.TryReadPublicBranch(appId, cached.PayloadJson, out var cachedPresent);
            if (cachedBranch is not null)
            {
                return BuildInfoFetch.OkCached(cachedBranch);
            }

            if (cachedPresent)
            {
                return BuildInfoFetch.NoDataCached;
            }

            // A stored body that no longer projects — a shape change that landed
            // in the cache before it was noticed. Fall through and refetch
            // rather than serve nothing until the TTL expires.
        }

        var fetched = await FetchAsync(appId, key, ct);
        return fetched.Outcome switch
        {
            FetchOutcome.Unavailable => BuildInfoFetch.Unavailable,
            _ when fetched.Branch is not null => BuildInfoFetch.Ok(fetched.Branch),
            _ => BuildInfoFetch.NoData,
        };
    }

    /// <inheritdoc />
    public async Task<AppInfoFetch> GetAppInfoAsync(
        string appId,
        TimeSpan? cacheTtl = null,
        bool cachedOnly = false,
        CancellationToken ct = default)
    {
        if (!IsAppId(appId))
        {
            return AppInfoFetch.Unavailable;
        }

        var key = AppCacheKey(appId);
        var cutoff = Cutoff(cacheTtl ?? _options.AppInfoCacheTtl);

        if (await _cache.GetAsync(CacheProvider, key, ct) is { } cached && cached.FetchedAt >= cutoff)
        {
            if (cached.PayloadJson is null)
            {
                // A cached miss: the service answered and had nothing for this
                // appid — the missing-app body, or the restricted one.
                return AppInfoFetch.NoDataCached;
            }

            var cachedInfo = UpdateSignalJson.TryReadCommon(appId, cached.PayloadJson, out var cachedPresent);
            if (cachedInfo is not null)
            {
                return AppInfoFetch.OkCached(cachedInfo);
            }

            if (cachedPresent)
            {
                // A stored body that genuinely carries no `common` — cached
                // because its `depots` projected, which is the whole reason the
                // two projections share one row.
                return AppInfoFetch.NoDataCached;
            }

            // A stored body that no longer parses at all — a shape change that
            // landed in the cache before it was noticed. Fall through and
            // refetch rather than serve nothing until the TTL expires.
        }

        if (cachedOnly)
        {
            // The caller asked to spend nothing at the volunteer service.
            // Reported as Unavailable, not NoData, so "we did not look" can
            // never be mistaken for "the service said no".
            return AppInfoFetch.Unavailable;
        }

        var fetched = await FetchAsync(appId, key, ct);
        return fetched.Outcome switch
        {
            FetchOutcome.Unavailable => AppInfoFetch.Unavailable,
            _ when fetched.Info is not null => AppInfoFetch.Ok(fetched.Info),
            _ => AppInfoFetch.NoData,
        };
    }

    /// <summary>
    /// One request, both projections, one cache write. Cached when either projection
    /// succeeded; genuine misses are cached as null payload; request failures are never cached.
    /// </summary>
    private async Task<FetchedApp> FetchAsync(string appId, string key, CancellationToken ct)
    {
        var body = await GetAsync("v1/info/" + appId, ct);
        if (body is null)
        {
            return FetchedApp.Unavailable;
        }

        var branch = UpdateSignalJson.TryReadPublicBranch(appId, body, out var present);
        var info = UpdateSignalJson.TryReadCommon(appId, body, out _);

        if (!present)
        {
            _log.LogWarning(
                "steamcmd.net returned no entry for appid {AppId}; treating as unanswered. "
                + "A missing app is a 200 carrying an EMPTY object for the appid, not an absent key — "
                + "check the contract test.",
                appId);
            return FetchedApp.Unavailable;
        }

        var fetchedAt = _clock.GetUtcNow().UtcDateTime;
        var payload = UpdateSignalJson.HasAppPayload(appId, body) ? body : null;
        await _cache.SetAsync(CacheProvider, key, payload, fetchedAt, ct);

        return new FetchedApp(FetchOutcome.Answered, branch, info);
    }

    private enum FetchOutcome
    {
        Answered,
        Unavailable,
    }

    private readonly record struct FetchedApp(FetchOutcome Outcome, BuildBranch? Branch, SteamAppInfo? Info)
    {
        public static FetchedApp Unavailable { get; } = new(FetchOutcome.Unavailable, null, null);
    }

    /// <summary>One GET. Returns the body, or null on failure.</summary>
    private async Task<string?> GetAsync(string path, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning(
                    "steamcmd.net {Path} returned {StatusCode}; no build signal this pass.",
                    path, (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            _log.LogWarning(ex, "steamcmd.net {Path} request failed; no build signal this pass.", path);
            return null;
        }
    }

    private DateTime Cutoff(TimeSpan ttl)
        => ttl <= TimeSpan.Zero ? DateTime.MaxValue : _clock.GetUtcNow().UtcDateTime - ttl;

    private static bool IsAppId(string value)
        => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0;
}
