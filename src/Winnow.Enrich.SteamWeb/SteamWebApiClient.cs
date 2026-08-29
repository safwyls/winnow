using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Winnow.Core.Ingest;
using Winnow.Enrich.SteamWeb.Credentials;
using Winnow.Enrich.SteamWeb.Http;
using Winnow.Enrich.SteamWeb.Model;
using Winnow.Enrich.SteamWeb.Storage;
using Microsoft.Extensions.Logging;

namespace Winnow.Enrich.SteamWeb;

/// <summary>
/// Client for <c>IPlayerService/GetOwnedGames</c> (§4.2). The API key never
/// appears in a log line; failures soft-fail rather than throwing.
/// </summary>
public sealed class SteamWebApiClient : ISteamWebApiClient
{
    /// <summary>Named/typed <see cref="HttpClient"/> for the authenticated Web API.</summary>
    public const string HttpClientName = "steam-web";

    /// <summary><c>metadata_cache.provider</c> value for everything this client stores.</summary>
    public const string CacheProvider = "steam-web";

    /// <summary><c>CandidateOwnership.Source</c> value for candidates this module emits (§5.1 provenance).</summary>
    public const string SourceName = "steam_web_api";

    /// <summary>
    /// Verified live 2026-08-24: 200, one request for the whole library
    /// (841 games in a single 263 KB response), and <c>rtime_last_played</c>
    /// populated because the key belongs to the queried account (§4.2).
    /// </summary>
    private const string GetOwnedGamesPath = "IPlayerService/GetOwnedGames/v1/";

    private readonly HttpClient _http;
    private readonly ISteamWebMetadataCache _cache;
    private readonly ISteamApiKeyProvider _keys;
    private readonly SteamWebOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<SteamWebApiClient> _log;

    public SteamWebApiClient(
        HttpClient http,
        ISteamWebMetadataCache cache,
        ISteamApiKeyProvider keys,
        SteamWebOptions options,
        TimeProvider clock,
        ILogger<SteamWebApiClient> log)
    {
        _http = http;
        _cache = cache;
        _keys = keys;
        _options = options;
        _clock = clock;
        _log = log;

        _http.BaseAddress ??= _options.BaseAddress;
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", _options.UserAgent);
        }
    }

    /// <summary>Cache key for one account's owned library.</summary>
    public static string OwnedGamesCacheKey(SteamId steamId) => "owned:" + steamId.Value.ToString(CultureInfo.InvariantCulture);

    public async ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default)
        => await _keys.GetAsync(ct) is not null;

    public async Task<SteamOwnedLibrary> GetOwnedGamesAsync(
        SteamId steamId, TimeSpan? cacheTtl = null, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var cacheKey = OwnedGamesCacheKey(steamId);
        var cutoff = Cutoff(cacheTtl ?? _options.CacheTtl, now);

        var entry = await _cache.GetAsync(CacheProvider, cacheKey, ct);
        if (entry is { } cached && cached.FetchedAt >= cutoff
            && SteamWebJson.TryReadOwnedGames(cached.PayloadJson) is { } fresh)
        {
            _log.LogDebug(
                "Steam Web API owned library for {SteamId} served from cache ({Count} games).",
                steamId, fresh.Count);
            return new SteamOwnedLibrary(steamId, Succeeded: true, fresh, cached.FetchedAt, FromCache: true);
        }

        if (await _keys.GetAsync(ct) is not { } key)
        {
            // Not an error, and not a warning: this is simply a user who has not
            // pasted a key into settings. §5.1 — the module declines and the app
            // works exactly as it does without it.
            _log.LogDebug("Steam Web API not configured; owned-games lookup skipped.");
            return ServeStale(steamId, entry, now);
        }

        var body = await GetOwnedGamesBodyAsync(key, steamId, ct);
        var games = SteamWebJson.TryReadOwnedGames(body);
        if (games is null)
        {
            if (body is not null)
            {
                // A 200 whose body is not a library. The bare {"response":{}}
                // envelope lands here, which is what Steam sends for a profile it
                // will not disclose (verified live 2026-08-24 against a second
                // account on the same machine). It is NOT "owns nothing" and
                // must not be cached as one.
                _log.LogWarning(
                    "Steam {Endpoint} returned no library for {SteamId}. Treating the response as "
                    + "unanswered rather than as an empty library: the bare envelope Steam sends for a "
                    + "profile it will not disclose is indistinguishable from one, and caching it would "
                    + "record the account as owning nothing for a whole TTL.",
                    GetOwnedGamesPath, steamId);
            }

            return ServeStale(steamId, entry, now);
        }

        // Only a real answer reaches the cache, and it is stored verbatim so the
        // fields this client does not project — the per-platform playtime splits,
        // content descriptors, anything Valve adds later — are recoverable
        // without a refetch.
        await _cache.SetAsync(CacheProvider, cacheKey, body, now, ct);

        _log.LogInformation(
            "Steam Web API owned library for {SteamId}: {Count} games, {WithPlaytime} with playtime, "
            + "{WithLastPlayed} with a last-played timestamp.",
            steamId,
            games.Count,
            games.Count(static g => g.PlaytimeForeverMinutes > 0),
            games.Count(static g => g.LastPlayedUtc is not null));

        return new SteamOwnedLibrary(steamId, Succeeded: true, games, now, FromCache: false);
    }

    public async Task<IReadOnlyList<CandidateOwnership>> GetOwnershipCandidatesAsync(
        SteamId steamId, TimeSpan? cacheTtl = null, CancellationToken ct = default)
    {
        var library = await GetOwnedGamesAsync(steamId, cacheTtl, ct);
        return library.Succeeded ? library.ToCandidates(SourceName) : [];
    }

    /// <summary>
    /// A stale cache entry beats nothing: ownership does not un-happen, so
    /// yesterday's library is a strictly better answer than an empty one when
    /// today's request failed. Falls back to unanswered when there is no entry at
    /// all.
    /// </summary>
    private static SteamOwnedLibrary ServeStale(SteamId steamId, SteamWebCacheEntry? entry, DateTime now)
        => entry?.PayloadJson is { } payload && SteamWebJson.TryReadOwnedGames(payload) is { } stale
            ? new SteamOwnedLibrary(steamId, Succeeded: true, stale, entry.Value.FetchedAt, FromCache: true)
            : SteamOwnedLibrary.Unanswered(steamId, now);

    /// <summary>
    /// The one request this module makes. Returns the body, or null when the
    /// request did not produce one — the single place where "Steam said no"
    /// becomes "no data" instead of an exception.
    /// </summary>
    private async Task<string?> GetOwnedGamesBodyAsync(SteamApiKey key, SteamId steamId, CancellationToken ct)
    {
        // §4.2 is emphatic about all three flags, and one of them is a trap:
        // without skip_unvetted_apps=false, apps flagged "Profile Features
        // Limited" are silently omitted. Measured live on 2026-08-24 against the
        // user's own account: 841 titles with the flag, 834 without it — seven
        // owned games vanish, with no error and no indication in the response.
        //
        // The key is appended last and the string is used exactly once. It is
        // never logged, never stored, and never put into an exception message;
        // see SteamWebRedaction for why even the framework's own request logging
        // is removed for this client rather than trusted.
        var uri = GetOwnedGamesPath
            + "?steamid=" + steamId.Value.ToString(CultureInfo.InvariantCulture)
            + "&include_appinfo=1"
            + "&include_played_free_games=1"
            + "&skip_unvetted_apps=false"
            + "&format=json"
            + "&key=" + Uri.EscapeDataString(key.Value);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                // Endpoint constant, not request.RequestUri — that one carries
                // the key.
                _log.LogWarning(
                    response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
                        ? "Steam {Endpoint} returned {StatusCode}; the configured API key was rejected or is "
                        + "not entitled to this profile. Steam Web API enrichment is skipped this pass."
                        : "Steam {Endpoint} returned {StatusCode}; skipping this request.",
                    GetOwnedGamesPath,
                    (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller asked to stop. That is not an enrichment failure and
            // must not be swallowed into a silent empty result.
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            // Offline, DNS failure, TLS failure, or a timeout the retry policy
            // already exhausted. Enrichment failing is a degraded run, never a
            // crashed one (§5.1).
            //
            // Type and message only, never the exception object: a full stack
            // dump can carry an inner exception that quoted the request URI, and
            // the request URI carries the key.
            _log.LogWarning(
                "Steam {Endpoint} request failed ({ExceptionType}); skipping.",
                GetOwnedGamesPath, ex.GetType().Name);
            return null;
        }
    }

    private static DateTime Cutoff(TimeSpan ttl, DateTime now)
        => ttl <= TimeSpan.Zero ? DateTime.MaxValue : now - ttl;
}
