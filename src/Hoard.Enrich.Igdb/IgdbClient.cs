using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Hoard.Enrich.Igdb.Credentials;
using Hoard.Enrich.Igdb.Model;
using Hoard.Enrich.Igdb.Storage;
using Microsoft.Extensions.Logging;

namespace Hoard.Enrich.Igdb;

/// <summary>
/// Apicalypse client for IGDB v4.
///
/// <para>Everything that could go wrong slowly — auth, retry, 4 req/s — lives in
/// the <see cref="HttpClient"/> handler pipeline, so this class only has to
/// worry about three things: what to ask, how to batch it, and what not to ask
/// twice.</para>
///
/// <para><b>Editions are out of scope here.</b> §4.4 identifies
/// <c>game_versions</c> as the endpoint that models Skyrim vs. Special Edition
/// vs. Anniversary, and it is the right abstraction for the Release layer — but
/// that is a later milestone. Nothing in this class should start inferring
/// editions from names in the meantime.</para>
/// </summary>
public sealed class IgdbClient : IIgdbClient
{
    /// <summary>Named/typed <see cref="HttpClient"/> for api.igdb.com.</summary>
    public const string HttpClientName = "igdb";

    /// <summary><c>metadata_cache.provider</c> value for everything this client stores.</summary>
    public const string CacheProvider = "igdb";

    private readonly HttpClient _http;
    private readonly IMetadataCache _cache;
    private readonly IIgdbCredentialProvider _credentials;
    private readonly IgdbOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<IgdbClient> _log;

    public IgdbClient(
        HttpClient http,
        IMetadataCache cache,
        IIgdbCredentialProvider credentials,
        IgdbOptions options,
        TimeProvider clock,
        ILogger<IgdbClient> log)
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

    /// <summary>Cache key for a Steam appid lookup.</summary>
    public static string SteamAppCacheKey(string appId) => "steam-app:" + appId;

    /// <summary>Cache key for a full game record.</summary>
    public static string GameCacheKey(long igdbId) => "game:" + igdbId.ToString(CultureInfo.InvariantCulture);

    public async ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default)
        => await _credentials.GetAsync(ct) is not null;

    public async Task<IReadOnlyDictionary<string, IgdbSteamMatch>> ResolveBySteamAppIdsAsync(
        IEnumerable<string> appIds, TimeSpan? cacheTtl = null, CancellationToken ct = default)
    {
        var wanted = appIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Where(Apicalypse.IsSafeStringValue)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var results = new Dictionary<string, IgdbSteamMatch>(StringComparer.Ordinal);
        if (wanted.Length == 0)
        {
            return results;
        }

        var pending = new List<string>(wanted.Length);
        var cached = await _cache.GetManyAsync(
            CacheProvider, wanted.Select(SteamAppCacheKey), ct);
        var cutoff = Cutoff(cacheTtl);

        foreach (var appId in wanted)
        {
            if (cached.TryGetValue(SteamAppCacheKey(appId), out var entry) && entry.FetchedAt >= cutoff)
            {
                // A null payload is a cached miss: IGDB has no record for this
                // appid. Re-asking every run would spend the rate limit
                // learning the same nothing.
                if (Deserialize<SteamMatchCacheEntry>(entry.PayloadJson) is { } hit)
                {
                    results[appId] = hit.ToDomain(appId);
                }

                continue;
            }

            pending.Add(appId);
        }

        if (pending.Count == 0)
        {
            _log.LogDebug("Resolved {Count} Steam appids entirely from cache.", wanted.Length);
            return results;
        }

        if (!await IsConfiguredAsync(ct))
        {
            // Not an error: serve the cache and move on (§5.1 — enrichment
            // never blocks or breaks a path).
            _log.LogDebug(
                "IGDB not configured; {Cached} of {Total} appids served from cache, {Pending} left unresolved.",
                results.Count, wanted.Length, pending.Count);
            return results;
        }

        var fetchedAt = _clock.GetUtcNow().UtcDateTime;
        foreach (var batch in pending.Chunk(BatchSize))
        {
            var page = await FetchAllAsync<IgdbExternalGameDto>(
                "external_games",
                (limit, offset) => Apicalypse.SteamExternalGames(
                    batch, _options.SteamExternalGameSourceId, limit, offset),
                ct);

            if (!page.Succeeded)
            {
                // The batch failed rather than came back empty. Caching a miss
                // here would record "IGDB has never heard of these games" for a
                // whole TTL on the strength of one 503.
                continue;
            }

            var found = new Dictionary<string, IgdbSteamMatch>(StringComparer.Ordinal);
            foreach (var row in page.Items)
            {
                if (row.Uid is not { Length: > 0 } uid || row.Game is not { Id: > 0 } game)
                {
                    continue;
                }

                found[uid] = new IgdbSteamMatch(
                    uid,
                    game.Id,
                    game.Name,
                    IgdbJson.CoverUrl(game.Cover),
                    IgdbJson.ReleaseYear(game.FirstReleaseDate),
                    game.Summary);
            }

            foreach (var appId in batch)
            {
                // Every requested appid gets a cache row, matched or not. The
                // null payload is the record of a miss.
                var match = found.GetValueOrDefault(appId);
                if (match is not null)
                {
                    results[appId] = match;
                }

                await _cache.SetAsync(
                    CacheProvider,
                    SteamAppCacheKey(appId),
                    match is null ? null : Serialize(SteamMatchCacheEntry.From(match)),
                    fetchedAt,
                    ct);
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<IgdbGame>> GetGamesAsync(
        IEnumerable<long> igdbIds, TimeSpan? cacheTtl = null, CancellationToken ct = default)
    {
        var wanted = igdbIds.Where(id => id > 0).Distinct().ToArray();
        var results = new List<IgdbGame>(wanted.Length);
        if (wanted.Length == 0)
        {
            return results;
        }

        var cached = await _cache.GetManyAsync(CacheProvider, wanted.Select(GameCacheKey), ct);
        var cutoff = Cutoff(cacheTtl);
        var pending = new List<long>(wanted.Length);

        foreach (var id in wanted)
        {
            if (cached.TryGetValue(GameCacheKey(id), out var entry) && entry.FetchedAt >= cutoff)
            {
                if (Deserialize<IgdbGame>(entry.PayloadJson) is { } game)
                {
                    results.Add(game);
                }

                continue;
            }

            pending.Add(id);
        }

        if (pending.Count == 0 || !await IsConfiguredAsync(ct))
        {
            return results;
        }

        var fetchedAt = _clock.GetUtcNow().UtcDateTime;
        foreach (var batch in pending.Chunk(BatchSize))
        {
            var page = await FetchAllAsync<IgdbGameDto>(
                "games", (limit, offset) => Apicalypse.Games(batch, limit, offset), ct);

            if (!page.Succeeded)
            {
                continue;
            }

            var found = new Dictionary<long, IgdbGame>();
            foreach (var dto in page.Items)
            {
                if (dto.Id > 0)
                {
                    found[dto.Id] = dto.ToDomain();
                }
            }

            foreach (var id in batch)
            {
                found.TryGetValue(id, out var game);
                if (game is not null)
                {
                    results.Add(game);
                }

                await _cache.SetAsync(
                    CacheProvider, GameCacheKey(id), game is null ? null : Serialize(game), fetchedAt, ct);
            }
        }

        return results;
    }

    private int BatchSize => Math.Clamp(_options.BatchSize, 1, Apicalypse.MaxLimit);

    private DateTime Cutoff(TimeSpan? cacheTtl)
    {
        var ttl = cacheTtl ?? _options.CacheTtl;
        return ttl <= TimeSpan.Zero ? DateTime.MaxValue : _clock.GetUtcNow().UtcDateTime - ttl;
    }

    /// <summary>
    /// Runs one Apicalypse query, following <c>offset</c> pages until a page
    /// comes back short.
    ///
    /// <para>A batch of 400 ids normally fits in one 500-row page. Paging exists
    /// because <c>external_games</c> can hold more than one row per appid, and a
    /// silently truncated page would look exactly like "IGDB doesn't know these
    /// games" — the worst possible failure for a resolver that caches its
    /// misses.</para>
    ///
    /// <para><see cref="PageResult{T}.Succeeded"/> separates "IGDB answered, and
    /// the answer was nothing" from "the request failed". Only the first may be
    /// cached.</para>
    /// </summary>
    private async Task<PageResult<T>> FetchAllAsync<T>(
        string endpoint, Func<int, int, string> queryFactory, CancellationToken ct)
    {
        // Always ask for the full 500-row page even though a batch carries 400
        // ids: the limit bounds rows returned, not ids requested, and the slack
        // is what keeps the common batch to one request.
        const int limit = Apicalypse.MaxLimit;
        var offset = 0;
        var items = new List<T>();

        while (true)
        {
            var page = await PostAsync<T>(endpoint, queryFactory(limit, offset), ct);
            if (!page.Succeeded)
            {
                return new PageResult<T>(false, items);
            }

            items.AddRange(page.Items);
            if (page.Items.Count < limit)
            {
                return new PageResult<T>(true, items);
            }

            offset += limit;
        }
    }

    private async Task<PageResult<T>> PostAsync<T>(string endpoint, string query, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            // Apicalypse is posted as text/plain (§4.4) — not form-encoded,
            // not JSON, and never as query parameters.
            Content = new StringContent(query, Encoding.UTF8),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(Apicalypse.ContentType)
        {
            CharSet = "utf-8",
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            // Enrichment failing is a degraded run, never a crashed one.
            _log.LogWarning(
                "IGDB /{Endpoint} returned {StatusCode}; skipping this batch.",
                endpoint, (int)response.StatusCode);
            return new PageResult<T>(false, []);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var items = await JsonSerializer.DeserializeAsync<List<T>>(stream, IgdbJson.Options, ct);
        return new PageResult<T>(true, items ?? []);
    }

    /// <summary>Rows read so far, and whether every request behind them succeeded.</summary>
    private sealed record PageResult<T>(bool Succeeded, List<T> Items);

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, IgdbJson.Options);

    private static T? Deserialize<T>(string? json)
        where T : class
        => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<T>(json, IgdbJson.Options);

    /// <summary>
    /// Cached shape of a Steam match. The appid is the cache key, so it is not
    /// duplicated inside the payload.
    /// </summary>
    private sealed record SteamMatchCacheEntry(
        long IgdbId, string? Name, string? CoverUrl, int? FirstReleaseYear, string? Summary)
    {
        internal static SteamMatchCacheEntry From(IgdbSteamMatch match)
            => new(match.IgdbId, match.Name, match.CoverUrl, match.FirstReleaseYear, match.Summary);

        internal IgdbSteamMatch ToDomain(string appId)
            => new(appId, IgdbId, Name, CoverUrl, FirstReleaseYear, Summary);
    }
}
