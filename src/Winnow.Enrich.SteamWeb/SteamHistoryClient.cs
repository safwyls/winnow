using System.Globalization;
using System.Net;
using Winnow.Enrich.SteamWeb.Credentials;
using Winnow.Enrich.SteamWeb.Http;
using Winnow.Enrich.SteamWeb.Model;
using Winnow.Enrich.SteamWeb.Storage;
using Microsoft.Extensions.Logging;

namespace Winnow.Enrich.SteamWeb;

/// <summary>
/// Client for M5's two history endpoints. Shares
/// <see cref="SteamWebOptions"/>, the singleton rate limiter and the retry
/// policy with <see cref="SteamWebApiClient"/>: they talk to the same host under
/// the same key and the same 429 budget, so pacing them separately would let two
/// clients each stay under a limit they are jointly over.
///
/// <para>The API key never appears in a log line. Failures soft-fail to an
/// explicit "unanswered" rather than throwing, and the distinction between "the
/// year is empty" and "the request did not complete" is preserved all the way
/// out, because the backfill records completion markers off it.</para>
/// </summary>
public sealed class SteamHistoryClient : ISteamHistoryClient
{
    /// <summary>Named/typed <see cref="HttpClient"/> for the history endpoints.</summary>
    public const string HttpClientName = "steam-web-history";

    /// <summary>
    /// Verified live 2026-08-28 with the user's stored key: 200, populated for
    /// the key-holder's own account, coverage 2022 onward.
    /// </summary>
    private const string YearInReviewPath = "ISaleFeatureService/GetUserYearInReview/v1/";

    /// <summary>
    /// Verified live 2026-08-28: 200 with <b>no</b> <c>steamid</c> parameter;
    /// the key identifies the account.
    /// </summary>
    private const string LastPlayedTimesPath = "IPlayerService/ClientGetLastPlayedTimes/v1/";

    /// <summary>
    /// Cache key for <see cref="LastPlayedTimesPath"/>. Deliberately carries no
    /// account: the endpoint is scoped to the key, so keying the entry by a
    /// steamid would fetch the same bytes once per enumerated local account.
    /// </summary>
    private const string LastPlayedCacheKey = "lastplayed";

    private readonly HttpClient _http;
    private readonly ISteamWebMetadataCache _cache;
    private readonly ISteamCredentialProvider _credentials;
    private readonly SteamWebOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<SteamHistoryClient> _log;

    public SteamHistoryClient(
        HttpClient http,
        ISteamWebMetadataCache cache,
        ISteamCredentialProvider credentials,
        SteamWebOptions options,
        TimeProvider clock,
        ILogger<SteamHistoryClient> log)
    {
        _http = http;
        _cache = cache;
        _credentials = credentials;
        _options = options;
        _clock = clock;
        _log = log;

        _http.BaseAddress ??= _options.BaseAddress;
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", _options.UserAgent);
        }
    }

    /// <summary>Cache key for one account's Year in Review of one year.</summary>
    public static string YearInReviewCacheKey(SteamId steamId, int year)
        => string.Create(CultureInfo.InvariantCulture, $"yir:{steamId.Value}:{year}");

    public async ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default)
        => (await _credentials.GetInventoryAsync(ct)).HasUsableCredential;

    public async Task<SteamLastPlayedTimes> GetLastPlayedTimesAsync(
        SteamCredentialPurpose purpose = SteamCredentialPurpose.Unattended,
        TimeSpan? cacheTtl = null,
        CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var cutoff = Cutoff(cacheTtl ?? _options.CacheTtl, now);

        var entry = await _cache.GetAsync(SteamWebApiClient.CacheProvider, LastPlayedCacheKey, ct);
        if (entry is { } cached && cached.FetchedAt >= cutoff
            && SteamHistoryJson.TryReadLastPlayedTimes(cached.PayloadJson) is { } fresh)
        {
            return new SteamLastPlayedTimes(Answered: true, fresh, cached.FetchedAt, FromCache: true);
        }

        if (await _credentials.GetAsync(purpose, ct) is not { } credential)
        {
            _log.LogDebug("Steam Web API not configured; last-played lookup skipped.");
            return SteamLastPlayedTimes.Unanswered(now);
        }

        var body = await GetBodyAsync(
            credential,
            sending => sending.AppendTo(LastPlayedTimesPath + "?format=json"),
            LastPlayedTimesPath,
            ct);
        var games = SteamHistoryJson.TryReadLastPlayedTimes(body);
        if (games is null)
        {
            if (body is not null)
            {
                _log.LogWarning(
                    "Steam {Endpoint} returned a body with no games array; treating it as unanswered "
                    + "rather than as an account that has played nothing.",
                    LastPlayedTimesPath);
            }

            return ServeStaleLastPlayed(entry, now);
        }

        // Stored verbatim, so the per-platform splits and the fields this client
        // does not project stay recoverable without a refetch.
        await _cache.SetAsync(SteamWebApiClient.CacheProvider, LastPlayedCacheKey, body, now, ct);

        var result = new SteamLastPlayedTimes(Answered: true, games, now, FromCache: false);
        _log.LogInformation(
            "Steam last-played times: {Count} apps, {WithFirstPlayed} carrying a first-played date.",
            result.Games.Count, result.WithFirstPlayed);

        return result;
    }

    public async Task<SteamYearInReview> GetYearInReviewAsync(
        SteamId steamId,
        int year,
        SteamCredentialPurpose purpose = SteamCredentialPurpose.Unattended,
        TimeSpan? cacheTtl = null,
        CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var cacheKey = YearInReviewCacheKey(steamId, year);
        var cutoff = Cutoff(cacheTtl ?? _options.CacheTtl, now);

        var entry = await _cache.GetAsync(SteamWebApiClient.CacheProvider, cacheKey, ct);
        if (entry is { } cached && cached.FetchedAt >= cutoff
            && SteamHistoryJson.TryReadYearInReview(cached.PayloadJson) is { } fresh)
        {
            return Build(steamId, year, fresh, cached.FetchedAt, fromCache: true);
        }

        if (await _credentials.GetAsync(purpose, ct) is not { } credential)
        {
            _log.LogDebug("Steam Web API not configured; Year in Review lookup skipped.");
            return SteamYearInReview.Unanswered(steamId, year, now);
        }

        var body = await GetBodyAsync(
            credential,
            sending => sending.AppendTo(
                YearInReviewPath
                + "?steamid=" + steamId.Value.ToString(CultureInfo.InvariantCulture)
                + "&year=" + year.ToString(CultureInfo.InvariantCulture)
                + "&format=json"),
            YearInReviewPath,
            ct);
        var payload = SteamHistoryJson.TryReadYearInReview(body);
        if (payload is not { } parsed)
        {
            if (body is not null)
            {
                // The bare {"response":{}} envelope lands here. For an ordinary
                // GetOwnedGames call that is "a profile Steam will not
                // disclose"; here it is also what a year with no Steam Replay
                // looks like, and the two are indistinguishable on the wire.
                // Either way the year holds nothing to import. The backfill
                // records it as answered-and-empty, which is why this is not a
                // failure.
                _log.LogDebug(
                    "Steam {Endpoint} returned no stats block for {Year}; the year holds nothing to import.",
                    YearInReviewPath, year);

                await _cache.SetAsync(SteamWebApiClient.CacheProvider, cacheKey, body, now, ct);
                return new SteamYearInReview(
                    steamId, year, Answered: true, AccountId: null, Games: [], now, FromCache: false);
            }

            return ServeStaleYearInReview(steamId, year, entry, now);
        }

        await _cache.SetAsync(SteamWebApiClient.CacheProvider, cacheKey, body, now, ct);

        var result = Build(steamId, year, parsed, now, fromCache: false);
        if (result.AccountMismatch)
        {
            // The key belongs to a different account than the one being
            // back-filled. Logged at warning because it is the one condition
            // that would write somebody else's history onto this library's
            // ownerships, and the backfill refuses the whole account on it.
            _log.LogWarning(
                "Steam {Endpoint} answered for account {Answered} when {Requested} was asked about; "
                + "the configured API key belongs to a different account and this response is discarded.",
                YearInReviewPath, result.AccountId, steamId.AccountId);
        }
        else
        {
            _log.LogInformation(
                "Steam Year in Review {Year}: {Games} games, {Months} monthly data points.",
                year, result.Games.Count, result.Games.Sum(static g => g.Months.Count));
        }

        return result;
    }

    private static SteamYearInReview Build(
        SteamId steamId, int year, SteamYearInReviewPayload payload, DateTime observedAt, bool fromCache)
        => new(steamId, year, Answered: true, payload.AccountId, payload.Games, observedAt, fromCache);

    /// <summary>
    /// A stale cache entry beats nothing: history does not un-happen, so
    /// yesterday's answer is strictly better than none when today's request
    /// failed.
    /// </summary>
    private static SteamLastPlayedTimes ServeStaleLastPlayed(SteamWebCacheEntry? entry, DateTime now)
        => entry?.PayloadJson is { } payload && SteamHistoryJson.TryReadLastPlayedTimes(payload) is { } stale
            ? new SteamLastPlayedTimes(Answered: true, stale, entry.Value.FetchedAt, FromCache: true)
            : SteamLastPlayedTimes.Unanswered(now);

    private static SteamYearInReview ServeStaleYearInReview(
        SteamId steamId, int year, SteamWebCacheEntry? entry, DateTime now)
        => entry?.PayloadJson is { } payload && SteamHistoryJson.TryReadYearInReview(payload) is { } stale
            ? Build(steamId, year, stale, entry.Value.FetchedAt, fromCache: true)
            : SteamYearInReview.Unanswered(steamId, year, now);

    /// <summary>
    /// Sends one request and returns the body, or null when the request did not
    /// produce one. The single place where "Steam said no" becomes "no data"
    /// instead of an exception.
    /// </summary>
    private async Task<string?> GetBodyAsync(
        SteamCredential credential,
        Func<SteamCredential, string> buildUri,
        string endpoint,
        CancellationToken ct)
    {
        // The URI is built from whichever credential is being sent rather than
        // once up front, because a 401 can hand back a renewed session token and
        // the credential travels in the query string. SteamAuthorizedRequest
        // owns the one retry that follows.
        var outcome = await SteamAuthorizedRequest.SendAsync(_http, _credentials, credential, buildUri, ct);

        if (outcome.Renewed)
        {
            _log.LogInformation(
                "Steam {Endpoint} returned 401; the Steam session was renewed and the request was sent once "
                + "more. A second refusal is not retried.",
                endpoint);
        }

        if (outcome.FailureType is { } failure)
        {
            _log.LogWarning(
                "Steam {Endpoint} request failed ({ExceptionType}); skipping.", endpoint, failure);
            return null;
        }

        if (outcome.Body is null)
        {
            // The endpoint constant, never the request URI, which carries the
            // credential.
            _log.LogWarning(
                outcome.Status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
                    ? "Steam {Endpoint} returned {StatusCode}; the credential in force was rejected or is "
                    + "not entitled to this data. The backfill retries on a later run."
                    : "Steam {Endpoint} returned {StatusCode}; skipping this request.",
                endpoint,
                (int?)outcome.Status ?? 0);
        }

        return outcome.Body;
    }

    private static DateTime Cutoff(TimeSpan ttl, DateTime now)
        => ttl <= TimeSpan.Zero ? DateTime.MaxValue : now - ttl;
}
