using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Winnow.Enrich.Updates.Model;
using Winnow.Enrich.Updates.Storage;
using Microsoft.Extensions.Logging;

namespace Winnow.Enrich.Updates;

/// <summary>
/// Client for <c>ISteamNews/GetNewsForApp/v2</c>. Retry and rate limiting live
/// in the HttpClient pipeline; this class decides what to ask and what a given
/// answer means (including the 403-is-no-feed rule).
/// </summary>
public sealed class SteamNewsClient : ISteamNewsClient
{
    /// <summary>Named/typed <see cref="HttpClient"/> for the news endpoint.</summary>
    public const string HttpClientName = "steam-news";

    /// <summary><c>metadata_cache.provider</c> for the no-feed negatives this client stores.</summary>
    public const string CacheProvider = "steam-news";

    /// <summary>v2 endpoint path (v1 lacks the <c>feeds</c> parameter).</summary>
    private const string NewsPath = "ISteamNews/GetNewsForApp/v2/";

    private readonly HttpClient _http;
    private readonly IUpdateSignalCache _cache;
    private readonly UpdateSignalOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<SteamNewsClient> _log;

    public SteamNewsClient(
        HttpClient http,
        IUpdateSignalCache cache,
        UpdateSignalOptions options,
        TimeProvider clock,
        ILogger<SteamNewsClient> log)
    {
        _http = http;
        _cache = cache;
        _options = options;
        _clock = clock;
        _log = log;

        _http.BaseAddress ??= _options.NewsBaseAddress;
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", _options.UserAgent);
        }
    }

    /// <summary>Cache key for the "this appid has no news feed" negative.</summary>
    public static string NoFeedCacheKey(string appId) => "nofeed:" + appId;

    public async Task<NewsFetch> GetLatestPatchNoteAsync(string appId, CancellationToken ct = default)
    {
        if (!IsAppId(appId))
        {
            return NewsFetch.Unavailable;
        }

        // A live no-feed negative short-circuits before the pipeline, so a
        // delisted game costs one request per NoNewsFeedRetryAfter rather than
        // one per sweep.
        var cached = await _cache.GetAsync(CacheProvider, NoFeedCacheKey(appId), ct);
        if (cached is { } entry && entry.FetchedAt >= NoFeedCutoff())
        {
            return NewsFetch.NoFeedCached;
        }

        var query =
            $"?appid={appId}"
            // count=1 + maxlength=1 is what holds the response to ~440 bytes:
            // the newest item's metadata with its body truncated to one
            // character. Nothing here reads `contents`, and the full text would
            // multiply a 370-app sweep's bandwidth for no gain.
            + "&count=1&maxlength=1"
            + "&tags=" + Uri.EscapeDataString(_options.NewsTags);

        HttpStatusCode status;
        string body;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, NewsPath + query);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _http.SendAsync(request, ct);
            status = response.StatusCode;
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller asked to stop. Not an enrichment failure, and it must
            // not be swallowed into a silent "no news".
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            _log.LogWarning(ex, "Steam news request for appid {AppId} failed; no signal this pass.", appId);
            return NewsFetch.Unavailable;
        }

        if (status == HttpStatusCode.Forbidden)
        {
            // NOT throttling. Verified live: 460, 480, 520 and 750 answer 403
            // with body {} while known-good appids answer 200 in the same burst.
            // The retry policy already declined to back off on it (403 is not in
            // its transient allow-list); here it becomes a durable per-appid
            // fact, scoped to this appid and invisible to every other app in the
            // batch. Logged at Debug, not Warning: in a 616-game Steam library
            // this fires for dozens of perfectly ordinary delisted titles, and a
            // warning per sweep per dead game would train users to ignore the log.
            _log.LogDebug(
                "Appid {AppId} has no Steam news feed (403). Caching as a per-appid negative for {Retry}; "
                + "this is not rate limiting and must never trigger backoff.",
                appId, _options.NoNewsFeedRetryAfter);

            await _cache.SetAsync(
                CacheProvider, NoFeedCacheKey(appId), body, _clock.GetUtcNow().UtcDateTime, ct);
            return NewsFetch.NoFeed;
        }

        if (status != HttpStatusCode.OK)
        {
            _log.LogWarning(
                "Steam news for appid {AppId} returned {StatusCode}; no signal this pass.",
                appId, (int)status);
            return NewsFetch.Unavailable;
        }

        var item = UpdateSignalJson.TryReadNewestNewsItem(body, out var recognised);
        if (!recognised)
        {
            _log.LogWarning(
                "Steam news for appid {AppId} returned an unrecognised envelope; treating as unanswered. "
                + "Check the contract test.",
                appId);
            return NewsFetch.Unavailable;
        }

        // A recognised envelope with no items is an answer: this app has a feed,
        // and nothing in it is tagged `patchnotes` (verified live for 790). Not
        // cached — the next sweep asks the same cheap question and this is
        // exactly the state that changes when a game finally patches.
        return item is null ? NewsFetch.NoItems : NewsFetch.Ok(item);
    }

    private DateTime NoFeedCutoff()
        => _options.NoNewsFeedRetryAfter <= TimeSpan.Zero
            ? DateTime.MaxValue
            : _clock.GetUtcNow().UtcDateTime - _options.NoNewsFeedRetryAfter;

    private static bool IsAppId(string value)
        => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0;
}
