using System.Globalization;
using System.Net.Http.Headers;
using Hoard.Enrich.Steam.Model;
using Hoard.Enrich.Steam.Storage;
using Microsoft.Extensions.Logging;

namespace Hoard.Enrich.Steam;

/// <summary>
/// Client for Steam's two keyless store-frontend endpoints, per
/// <c>docs/spikes/steam-store-tags.md</c>.
///
/// <para>Everything that could go wrong slowly — retry, 429 backoff, the request
/// rate — lives in the <see cref="HttpClient"/> handler pipeline, so this class
/// only has to worry about three things: what to ask, how to batch it, and what
/// not to ask twice.</para>
///
/// <para><b>Soft-fail is the contract, not a courtesy.</b> These endpoints are
/// undocumented; the spike's instruction is to treat a shape change as expected.
/// So a non-200, an unparseable body, or a dead network produces "no data for
/// this batch" and is logged — it is never thrown at a caller, and it is never
/// written to the cache. Caching a failure as a miss would record "Steam has
/// never heard of these 600 games" for a whole TTL on the strength of one
/// 503.</para>
///
/// <para><b>Tags are fetched and cached, and nothing is built on them.</b> The
/// full response body is stored per app in <c>metadata_cache</c>, so weights,
/// descriptions, release dates and Deck compatibility are all recoverable later
/// without a refetch. The exposed surface stays deliberately at names plus tag
/// ranks.</para>
/// </summary>
public sealed class SteamStoreClient : ISteamStoreClient
{
    /// <summary>Named/typed <see cref="HttpClient"/> for the store endpoints.</summary>
    public const string HttpClientName = "steam-store";

    /// <summary><c>metadata_cache.provider</c> value for everything this client stores.</summary>
    public const string CacheProvider = "steam-store";

    /// <summary>Verified live 2026-08-23: keyless, batches ~100 appids per request.</summary>
    private const string GetItemsPath = "IStoreBrowseService/GetItems/v1/";

    /// <summary>Verified live 2026-08-23: keyless, whole vocabulary in one request.</summary>
    private const string GetTagListPath = "IStoreService/GetTagList/v1/";

    private readonly HttpClient _http;
    private readonly IStoreMetadataCache _cache;
    private readonly SteamStoreOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<SteamStoreClient> _log;

    public SteamStoreClient(
        HttpClient http,
        IStoreMetadataCache cache,
        SteamStoreOptions options,
        TimeProvider clock,
        ILogger<SteamStoreClient> log)
    {
        _http = http;
        _cache = cache;
        _options = options;
        _clock = clock;
        _log = log;

        _http.BaseAddress ??= _options.BaseAddress;
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", _options.UserAgent);
        }
    }

    /// <summary>Cache key for one store item.</summary>
    public static string AppCacheKey(string appId) => "app:" + appId;

    /// <summary>Cache key for the tag vocabulary in one language.</summary>
    public static string TagListCacheKey(string language) => "taglist:" + language;

    public async Task<IReadOnlyDictionary<string, SteamStoreItem>> GetItemsAsync(
        IEnumerable<string> appIds, TimeSpan? cacheTtl = null, CancellationToken ct = default)
    {
        var wanted = appIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            // The request encodes appid as a JSON number, so anything that is
            // not one is dropped here rather than corrupting a whole batch.
            .Where(IsAppId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var results = new Dictionary<string, SteamStoreItem>(StringComparer.Ordinal);
        if (wanted.Length == 0)
        {
            return results;
        }

        var cached = await _cache.GetManyAsync(CacheProvider, wanted.Select(AppCacheKey), ct);
        var cutoff = Cutoff(cacheTtl ?? _options.CacheTtl);
        var pending = new List<string>(wanted.Length);

        foreach (var appId in wanted)
        {
            if (!cached.TryGetValue(AppCacheKey(appId), out var entry) || entry.FetchedAt < cutoff)
            {
                pending.Add(appId);
                continue;
            }

            if (entry.PayloadJson is null)
            {
                // A cached miss: the store answered and had nothing for this
                // appid. Re-asking every run would spend the request budget
                // learning the same nothing.
                continue;
            }

            if (SteamStoreJson.TryParseItem(appId, entry.PayloadJson) is { } item)
            {
                results[appId] = item;
            }
            else
            {
                // Stored payload no longer projects — a shape change that landed
                // in the cache before it was noticed. Refetch rather than serve
                // nothing forever.
                pending.Add(appId);
            }
        }

        if (pending.Count == 0)
        {
            _log.LogDebug("Resolved {Count} Steam appids entirely from cache.", wanted.Length);
            return results;
        }

        var batchSize = Math.Max(1, _options.BatchSize);
        var fetchedAt = _clock.GetUtcNow().UtcDateTime;

        foreach (var batch in pending.Chunk(batchSize))
        {
            var body = await GetAsync(
                GetItemsPath, SteamStoreJson.BuildGetItemsQuery(batch, _options), ct);
            if (body is null)
            {
                continue;
            }

            var raw = SteamStoreJson.TryReadStoreItems(body);
            if (raw is null)
            {
                _log.LogWarning(
                    "Steam GetItems returned an unrecognised envelope for {Count} appids; "
                    + "treating the batch as unanswered. The endpoint is undocumented — check the contract test.",
                    batch.Length);
                continue;
            }

            if (raw.Count == 0)
            {
                // A 200 with no items at all for a non-empty request is not a
                // credible "none of these exist"; it is a shape change wearing a
                // success code. Do not turn it into a batch of cached misses.
                _log.LogWarning(
                    "Steam GetItems returned no store items for {Count} appids; treating the batch as unanswered.",
                    batch.Length);
                continue;
            }

            foreach (var appId in batch)
            {
                // Correlate by id, never by position: the response may omit,
                // reorder or add items relative to the request.
                var item = raw.TryGetValue(appId, out var rawItem)
                    ? SteamStoreJson.TryParseItem(appId, rawItem)
                    : null;

                if (item is not null)
                {
                    results[appId] = item;
                }

                // Every appid in an answered batch gets a row, matched or not.
                // A null payload records a genuine miss (success != 1, or the
                // store simply did not return the item); the raw item body is
                // stored verbatim so nothing has to be refetched to look at a
                // field this client does not project today.
                await _cache.SetAsync(
                    CacheProvider,
                    AppCacheKey(appId),
                    item is null ? null : rawItem,
                    fetchedAt,
                    ct);
            }
        }

        return results;
    }

    public async Task<SteamTagVocabulary> GetTagListAsync(
        TimeSpan? cacheTtl = null, CancellationToken ct = default)
    {
        var key = TagListCacheKey(_options.Language);
        var entry = await _cache.GetAsync(CacheProvider, key, ct);
        var stale = entry?.PayloadJson is { } payload ? SteamStoreJson.TryReadTagList(payload) : null;

        if (entry is { } e && e.FetchedAt >= Cutoff(cacheTtl ?? _options.TagListCacheTtl) && stale is not null)
        {
            return stale;
        }

        var body = await GetAsync(GetTagListPath, SteamStoreJson.BuildTagListQuery(_options), ct);
        var fresh = body is null ? null : SteamStoreJson.TryReadTagList(body);
        if (fresh is null)
        {
            if (body is not null)
            {
                _log.LogWarning(
                    "Steam GetTagList returned an unrecognised envelope; the endpoint is undocumented — "
                    + "check the contract test.");
            }

            // A stale vocabulary is strictly better than none: tag names do not
            // change meaning, and the alternative is unresolvable tag ids.
            return stale ?? SteamTagVocabulary.Empty;
        }

        await _cache.SetAsync(CacheProvider, key, body, _clock.GetUtcNow().UtcDateTime, ct);
        return fresh;
    }

    /// <summary>
    /// One <c>input_json</c> GET. Returns the body, or null when the request did
    /// not produce one — the single place where "Steam said no" becomes "no
    /// data" instead of an exception.
    /// </summary>
    private async Task<string?> GetAsync(string path, string inputJson, CancellationToken ct)
    {
        var uri = path + "?input_json=" + Uri.EscapeDataString(inputJson);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning(
                    "Steam store {Path} returned {StatusCode}; skipping this request.",
                    path, (int)response.StatusCode);
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
            _log.LogWarning(ex, "Steam store {Path} request failed; skipping.", path);
            return null;
        }
    }

    private DateTime Cutoff(TimeSpan ttl)
        => ttl <= TimeSpan.Zero ? DateTime.MaxValue : _clock.GetUtcNow().UtcDateTime - ttl;

    private static bool IsAppId(string value)
        => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0;
}
