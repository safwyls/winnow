using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Winnow.Enrich.Igdb.Credentials;
using Winnow.Enrich.Igdb.Model;
using Winnow.Enrich.Igdb.Storage;
using Microsoft.Extensions.Logging;

namespace Winnow.Enrich.Igdb;

/// <summary>
/// Apicalypse client for IGDB v4. Auth, retry and rate limiting live in the
/// <see cref="HttpClient"/> handler pipeline.
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

    /// <summary>
    /// Cache key for a Steam appid lookup. Kept in its original un-namespaced
    /// shape to preserve existing cached rows.
    /// </summary>
    public static string SteamAppCacheKey(string appId) => "steam-app:" + appId;

    /// <summary>
    /// Cache key for a lookup under any other <c>external_game_source</c>.
    ///
    /// <para>The source id is in the key because uids are only unique
    /// <i>within</i> a source: <c>"1"</c> is Fallout on GOG (source 5) and a
    /// perfectly plausible Steam appid, and a cached miss under one source read
    /// back as an answer for another is a silent wrong title.</para>
    /// </summary>
    public static string ExternalCacheKey(int sourceId, string uid)
        => "external:" + sourceId.ToString(CultureInfo.InvariantCulture) + ":" + uid;

    /// <summary>Cache key for a full game record.</summary>
    public static string GameCacheKey(long igdbId) => "game:" + igdbId.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Shape version of a cached <c>game:</c> payload. Bumping it makes every
    /// stored entry a miss on the next read, which is how a cache full of
    /// payloads written before a field existed refetches instead of answering
    /// with the field silently empty for the rest of the TTL. Version 2 is
    /// the first to carry <c>game_type</c>, <c>parent_game</c>,
    /// <c>version_parent</c> and <c>version_title</c>.
    /// </summary>
    public const int GamePayloadVersion = 2;

    /// <summary>
    /// Versioned envelope a game is cached in. An unversioned payload
    /// (everything written before version 2) fails to match
    /// <see cref="GamePayloadVersion"/> on read and is refetched.
    /// </summary>
    private sealed record GamePayload(int Version, IgdbGame? Game);

    public async ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default)
        => await _credentials.GetAsync(ct) is not null;

    public Task<IReadOnlyDictionary<string, IgdbExternalMatch>> ResolveBySteamAppIdsAsync(
        IEnumerable<string> appIds, TimeSpan? cacheTtl = null, CancellationToken ct = default)
        => ResolveExternalAsync(
            _options.SteamExternalGameSourceId, appIds, SteamAppCacheKey, "Steam appid", cacheTtl, ct);

    public Task<IReadOnlyDictionary<string, IgdbExternalMatch>> ResolveByExternalIdsAsync(
        int externalGameSourceId,
        IEnumerable<string> uids,
        TimeSpan? cacheTtl = null,
        CancellationToken ct = default)
        => externalGameSourceId == _options.SteamExternalGameSourceId
            // Same source, same answers, and the same 865 cache rows already on
            // disk. Routing it through the Steam key rather than minting a
            // parallel `external:1:` namespace is what keeps a caller that says
            // "source 1" and a caller that says "Steam" from paying twice for
            // one appid.
            ? ResolveBySteamAppIdsAsync(uids, cacheTtl, ct)
            : ResolveExternalAsync(
                externalGameSourceId,
                uids,
                uid => ExternalCacheKey(externalGameSourceId, uid),
                "external id (source " + externalGameSourceId.ToString(CultureInfo.InvariantCulture) + ")",
                cacheTtl,
                ct);

    /// <summary>
    /// One <c>external_games</c> sweep: cache first, batch the remainder, cache
    /// every answer including the misses.
    ///
    /// <para><paramref name="cacheKey"/> is a parameter rather than derived from
    /// <paramref name="sourceId"/> so Steam can keep the un-namespaced key its
    /// existing rows were written under — see
    /// <see cref="SteamAppCacheKey"/>.</para>
    /// </summary>
    private async Task<IReadOnlyDictionary<string, IgdbExternalMatch>> ResolveExternalAsync(
        int sourceId,
        IEnumerable<string> uids,
        Func<string, string> cacheKey,
        string what,
        TimeSpan? cacheTtl,
        CancellationToken ct)
    {
        var wanted = uids
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Where(Apicalypse.IsSafeStringValue)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var results = new Dictionary<string, IgdbExternalMatch>(StringComparer.Ordinal);
        if (wanted.Length == 0)
        {
            return results;
        }

        var pending = new List<string>(wanted.Length);
        var cached = await _cache.GetManyAsync(CacheProvider, wanted.Select(cacheKey), ct);
        var cutoff = Cutoff(cacheTtl);

        foreach (var uid in wanted)
        {
            if (cached.TryGetValue(cacheKey(uid), out var entry) && entry.FetchedAt >= cutoff)
            {
                // A null payload is a cached miss: IGDB has no record for this
                // id under this source. Re-asking every run would spend the rate
                // limit learning the same nothing.
                if (Deserialize<ExternalMatchCacheEntry>(entry.PayloadJson) is { } hit)
                {
                    results[uid] = hit.ToDomain(uid);
                }

                continue;
            }

            pending.Add(uid);
        }

        if (pending.Count == 0)
        {
            _log.LogDebug("Resolved {Count} {What}s entirely from cache.", wanted.Length, what);
            return results;
        }

        if (!await IsConfiguredAsync(ct))
        {
            // Not an error: serve the cache and move on (§5.1 — enrichment
            // never blocks or breaks a path).
            _log.LogDebug(
                "IGDB not configured; {Cached} of {Total} {What}s served from cache, {Pending} left unresolved.",
                results.Count, wanted.Length, what, pending.Count);
            return results;
        }

        var fetchedAt = _clock.GetUtcNow().UtcDateTime;
        foreach (var batch in pending.Chunk(BatchSize))
        {
            var page = await FetchAllAsync<IgdbExternalGameDto>(
                "external_games",
                (limit, offset) => Apicalypse.ExternalGames(batch, sourceId, limit, offset),
                ct);

            if (!page.Succeeded)
            {
                // The batch failed rather than came back empty. Caching a miss
                // here would record "IGDB has never heard of these games" for a
                // whole TTL on the strength of one 503.
                continue;
            }

            var found = new Dictionary<string, IgdbExternalMatch>(StringComparer.Ordinal);
            foreach (var row in page.Items)
            {
                if (row.Uid is not { Length: > 0 } uid || row.Game is not { Id: > 0 } game)
                {
                    continue;
                }

                found[uid] = new IgdbExternalMatch(
                    uid,
                    game.Id,
                    game.Name,
                    IgdbJson.CoverUrl(game.Cover),
                    IgdbJson.ReleaseYear(game.FirstReleaseDate),
                    game.Summary);
            }

            foreach (var uid in batch)
            {
                // Every requested id gets a cache row, matched or not. The null
                // payload is the record of a miss.
                var match = found.GetValueOrDefault(uid);
                if (match is not null)
                {
                    results[uid] = match;
                }

                await _cache.SetAsync(
                    CacheProvider,
                    cacheKey(uid),
                    match is null ? null : Serialize(ExternalMatchCacheEntry.From(match)),
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

        // Payloads written under an older version, kept aside. A version
        // mismatch asks for a refetch but does NOT throw the old answer away:
        // the machine may have no Twitch credentials and no network, and 1,923
        // entries that stop deserializing on an offline install would be a worse
        // bug than a missing field. Anything the refetch does not replace is
        // served from here.
        var superseded = new Dictionary<long, IgdbGame>();

        foreach (var id in wanted)
        {
            if (cached.TryGetValue(GameCacheKey(id), out var entry) && entry.FetchedAt >= cutoff)
            {
                if (entry.PayloadJson is null)
                {
                    // A cached miss carries no fields, so it cannot be missing
                    // any: the payload version does not apply to it, and
                    // re-asking would spend the budget learning the same
                    // nothing.
                    continue;
                }

                if (Deserialize<GamePayload>(entry.PayloadJson) is
                    { Version: GamePayloadVersion, Game: { } game })
                {
                    results.Add(game);
                    continue;
                }

                // Either a payload written before this version — every entry in
                // a cache built without game_type, parent_game and
                // version_parent is one — or one that no longer projects.
                // Refetch rather than serve a row whose new fields are silently
                // empty for the rest of the TTL, and keep the old answer as the
                // fallback for a refetch that cannot happen.
                if (Deserialize<IgdbGame>(entry.PayloadJson) is { IgdbId: > 0 } legacy)
                {
                    superseded[id] = legacy;
                }
            }

            pending.Add(id);
        }

        if (pending.Count == 0 || !await IsConfiguredAsync(ct))
        {
            results.AddRange(superseded.Values);
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
                    superseded.Remove(id);
                }

                await _cache.SetAsync(
                    CacheProvider,
                    GameCacheKey(id),
                    game is null ? null : Serialize(new GamePayload(GamePayloadVersion, game)),
                    fetchedAt,
                    ct);
            }
        }

        // Whatever the refetch could not replace. An old shape is still a real
        // answer about a real game; the version only decides whether it is
        // allowed to be the FIRST answer.
        results.AddRange(superseded.Values);
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

        try
        {
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller asked to stop. Not an enrichment failure, and it must
            // not be swallowed into a silent empty page.
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or JsonException)
        {
            // Offline, DNS failure, TLS failure, a timeout the retry policy
            // already exhausted, or a body that is not the JSON this endpoint
            // has always returned. A NON-200 was already a degraded batch rather
            // than an exception (above); a dead socket was not, and the
            // difference was doing real damage.
            //
            // GetGamesAsync serves cached rows first and fetches only the
            // remainder, so an exception escaping from here discarded every
            // CACHED row in the same call. On a library with 865 games on disk
            // and one id nobody has ever looked up, a dropped connection turned
            // "864 hits and one miss" into "nothing at all" — enrichment
            // breaking a caller instead of degrading, which §5.1 forbids
            // outright. The Steam store client has always read it this way
            // (SteamStoreClient.GetAsync); this brings IGDB in line.
            _log.LogWarning(ex, "IGDB /{Endpoint} request failed; skipping this batch.", endpoint);
            return new PageResult<T>(false, []);
        }
    }

    /// <summary>Rows read so far, and whether every request behind them succeeded.</summary>
    private sealed record PageResult<T>(bool Succeeded, List<T> Items);

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, IgdbJson.Options);

    private static T? Deserialize<T>(string? json)
        where T : class
        => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<T>(json, IgdbJson.Options);

    /// <summary>
    /// Cached shape of an <c>external_games</c> match. The store id is the cache
    /// key, so it is not duplicated inside the payload — which is also why the
    /// stored JSON is unchanged by the rename from the Steam-only shape and
    /// every existing cache row still deserializes.
    /// </summary>
    private sealed record ExternalMatchCacheEntry(
        long IgdbId, string? Name, string? CoverUrl, int? FirstReleaseYear, string? Summary)
    {
        internal static ExternalMatchCacheEntry From(IgdbExternalMatch match)
            => new(match.IgdbId, match.Name, match.CoverUrl, match.FirstReleaseYear, match.Summary);

        internal IgdbExternalMatch ToDomain(string uid)
            => new(uid, IgdbId, Name, CoverUrl, FirstReleaseYear, Summary);
    }
}
