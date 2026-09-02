using System.Globalization;
using System.Net.Http.Headers;
using Winnow.Enrich.Steam.Model;
using Winnow.Enrich.Steam.Storage;
using Microsoft.Extensions.Logging;

namespace Winnow.Enrich.Steam;

/// <summary>
/// Client for Steam's keyless store-frontend endpoints. Soft-fail on all paths;
/// failures are logged, never thrown or cached. Retry and rate limiting live in
/// the <see cref="HttpClient"/> handler pipeline.
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

    /// <summary>
    /// Verified live 2026-08-25: keyless, 200, 16246 bytes, 72 categories in one
    /// request. Probed alongside two plausible sibling names —
    /// <c>GetStoreCategoryList</c> and <c>GetCategories</c> — which both 404, so
    /// this one is not a guess that happened to work.
    /// </summary>
    private const string GetStoreCategoriesPath = "IStoreBrowseService/GetStoreCategories/v1/";

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

    /// <summary>Cache key for the store-category vocabulary in one language.</summary>
    public static string StoreCategoriesCacheKey(string language) => "categories:" + language;

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

            // A SHORT response is already a shape anomaly, not an answer.
            // docs/spikes/steam-store-tags.md:57-60 verified that appids with no
            // store page — 760, 1391110 — come back INSIDE the array as
            // {"success":15,"visible":false,"name":""}; the request still 200s
            // and still returns one item per appid asked for. So "the store
            // answered and had nothing for this appid" arrives as a present item
            // that fails to project, which the loop below handles. An appid
            // simply absent from the array is the endpoint behaving differently
            // from the way it was verified to behave.
            //
            // The distinction matters because the two readings differ by two
            // orders of magnitude: a 200 carrying 1 of 100 requested items is
            // either 99 games Steam has never heard of, or one truncated
            // response. Recording the first costs 99 cached misses held for the
            // full 7-day TTL, and nothing re-asks until it expires.
            var answered = raw.Count >= batch.Length;
            if (!answered)
            {
                _log.LogWarning(
                    "Steam GetItems returned {Returned} store items for {Requested} appids. "
                    + "Non-store appids come back inside the array, so a short response is a shape "
                    + "change rather than a batch of misses — the {Missing} unanswered appids are "
                    + "left uncached and retried next pass. The endpoint is undocumented; check the "
                    + "contract test.",
                    raw.Count, batch.Length, batch.Length - raw.Count);
            }

            foreach (var appId in batch)
            {
                // Correlate by id, never by position: the response may omit,
                // reorder or add items relative to the request.
                var present = raw.TryGetValue(appId, out var rawItem) && rawItem is not null;
                var item = present ? SteamStoreJson.TryParseItem(appId, rawItem!) : null;

                if (item is not null)
                {
                    results[appId] = item;
                }

                // An appid the response omitted during a short batch is left
                // out of the cache entirely, so the next pass asks again. It is
                // NOT written as a miss: a miss is a claim about the store's
                // contents, and a truncated response is no evidence for one.
                if (!present && !answered)
                {
                    continue;
                }

                // Every appid the batch actually answered for gets a row,
                // matched or not. A null payload records a genuine miss
                // (success != 1, or an item present but unprojectable); the raw
                // item body is stored verbatim so nothing has to be refetched to
                // look at a field this client does not project today.
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

    public async Task<IReadOnlyDictionary<string, SteamStoreItem>> GetCachedItemsAsync(
        IEnumerable<string> appIds, CancellationToken ct = default)
    {
        var wanted = appIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Where(IsAppId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var results = new Dictionary<string, SteamStoreItem>(StringComparer.Ordinal);
        if (wanted.Length == 0)
        {
            return results;
        }

        var cached = await _cache.GetManyAsync(CacheProvider, wanted.Select(AppCacheKey), ct);

        foreach (var appId in wanted)
        {
            // No cutoff. A stale body is exactly as good an answer about which
            // appid is this app's parent as a fresh one, and re-asking is the
            // one thing this method promises not to do.
            if (cached.TryGetValue(AppCacheKey(appId), out var entry)
                && entry.PayloadJson is { } payload
                && SteamStoreJson.TryParseItem(appId, payload) is { } item)
            {
                results[appId] = item;
            }
        }

        _log.LogDebug(
            "Projected {Hits} of {Requested} Steam appids from the store cache with no request.",
            results.Count, wanted.Length);

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

    public async Task<SteamStoreCategoryVocabulary> GetStoreCategoriesAsync(
        TimeSpan? cacheTtl = null, CancellationToken ct = default)
    {
        // Deliberately the same shape as GetTagListAsync, statement for
        // statement: same cache-then-fetch order, same "a stale vocabulary beats
        // no vocabulary" fallback, same refusal to cache an unrecognised body.
        // Two vocabularies that behave differently under failure would be two
        // sets of edge cases to remember.
        var key = StoreCategoriesCacheKey(_options.Language);
        var entry = await _cache.GetAsync(CacheProvider, key, ct);
        var stale = entry?.PayloadJson is { } payload
            ? SteamStoreJson.TryReadStoreCategories(payload)
            : null;

        if (entry is { } e
            && e.FetchedAt >= Cutoff(cacheTtl ?? _options.StoreCategoryCacheTtl)
            && stale is not null)
        {
            return stale;
        }

        var body = await GetAsync(
            GetStoreCategoriesPath, SteamStoreJson.BuildStoreCategoriesQuery(_options), ct);
        var fresh = body is null ? null : SteamStoreJson.TryReadStoreCategories(body);
        if (fresh is null)
        {
            if (body is not null)
            {
                _log.LogWarning(
                    "Steam GetStoreCategories returned an unrecognised envelope; the endpoint is "
                    + "undocumented — check the contract test.");
            }

            // Category names do not change meaning, so an old snapshot is
            // strictly better than unresolvable ids.
            return stale ?? SteamStoreCategoryVocabulary.Empty;
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
