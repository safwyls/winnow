using System.Globalization;
using System.Net.Http.Headers;
using Hoard.Enrich.Updates.Model;
using Hoard.Enrich.Updates.Storage;
using Microsoft.Extensions.Logging;

namespace Hoard.Enrich.Updates;

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

        var body = await GetAsync("v1/info/" + appId, ct);
        if (body is null)
        {
            // Nothing is cached on failure. Caching one would record "this app
            // has no build data" for the whole TTL on the strength of a single
            // 503 from a service §4.5 already saw go dark.
            return BuildInfoFetch.Unavailable;
        }

        var branch = UpdateSignalJson.TryReadPublicBranch(appId, body, out var present);

        if (!present)
        {
            _log.LogWarning(
                "steamcmd.net returned no entry for appid {AppId}; treating as unanswered. "
                + "A missing app is a 200 carrying an EMPTY object for the appid, not an absent key — "
                + "check the contract test.",
                appId);
            return BuildInfoFetch.Unavailable;
        }

        var fetchedAt = _clock.GetUtcNow().UtcDateTime;

        // `present` with no projectable branch is the verified missing-app shape
        // — {"data":{"999999999":{}},"status":"success"} at HTTP 200 — or a real
        // app with no public branch. Either way the service answered, so the
        // answer is cached as a miss.
        await _cache.SetAsync(CacheProvider, key, branch is null ? null : body, fetchedAt, ct);

        return branch is null ? BuildInfoFetch.NoData : BuildInfoFetch.Ok(branch);
    }

    /// <summary>
    /// One GET. Returns the body, or null when the request did not produce one —
    /// the single place where "steamcmd.net said no" becomes "no build signal"
    /// instead of an exception.
    /// </summary>
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
