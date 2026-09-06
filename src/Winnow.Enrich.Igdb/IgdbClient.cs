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
    /// <c>version_parent</c> and <c>version_title</c>; version 3 is the first
    /// to carry <c>platforms</c>; version 4 is the first to carry
    /// <c>screenshots</c>, <c>artworks</c> and the four rating figures
    /// (<c>rating</c>, <c>rating_count</c>, <c>aggregated_rating</c>,
    /// <c>aggregated_rating_count</c>). Measured cost of the 3 → 4 bump:
    /// 967 games refetch in 3 requests (400 ids per batch), and the cached
    /// payload grows from 628 to 658 bytes per game — about 4.8%.
    /// </summary>
    public const int GamePayloadVersion = 4;

    /// <summary>
    /// Versioned envelope a game is cached in. An unversioned payload
    /// (everything written before version 2) fails to match
    /// <see cref="GamePayloadVersion"/> on read and is refetched.
    /// </summary>
    private sealed record GamePayload(int Version, IgdbGame? Game);

    public const int ExternalMatchPayloadVersion = 1;
    private sealed record ExternalMatchPayload(int Version, ExternalMatchCacheEntry? Match);

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
                var payload = Deserialize<ExternalMatchPayload>(entry.PayloadJson);
                if (payload is { Version: ExternalMatchPayloadVersion })
                {
                    if (payload.Match is { IgdbId: > 0 } hit)
                        results[uid] = hit.ToDomain(uid);
                    continue;
                }
                // Keep an older positive answer available when refetch fails or
                // credentials are absent, but never treat it as current.
                var legacy = payload?.Match ?? Deserialize<ExternalMatchCacheEntry>(entry.PayloadJson);
                if (legacy is { IgdbId: > 0 }) results[uid] = legacy.ToDomain(uid);
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
                // Misses carry the version too, so a schema bump rechecks them.
                var match = found.GetValueOrDefault(uid);
                if (match is not null)
                {
                    results[uid] = match;
                }
                else results.Remove(uid);

                await _cache.SetAsync(
                    CacheProvider,
                    cacheKey(uid),
                    Serialize(new ExternalMatchPayload(ExternalMatchPayloadVersion,
                        match is null ? null : ExternalMatchCacheEntry.From(match))),
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
                var payload = Deserialize<GamePayload>(entry.PayloadJson);
                if (payload is { Version: GamePayloadVersion })
                {
                    if (payload.Game is { } game) results.Add(game);
                    continue;
                }

                // Either a payload written before this version — a cache built
                // before the current field list is full of them — or one that no
                // longer projects. Refetch rather than serve a row whose new
                // fields are silently empty for the rest of the TTL, and keep the
                // old answer as the fallback for a refetch that cannot happen.
                //
                // Two stored shapes have to survive that, and the order matters.
                // An older ENVELOPE ({"version":2,"game":{…}}) is what every
                // payload on a current install looks like; the bare shape below
                // it is what installs predating version 2 wrote. Read as a bare
                // IgdbGame, an envelope yields IgdbId 0, fails the `> 0` guard
                // and is dropped — so without the envelope branch a version bump
                // turns "a stale row missing one field" into "nothing at all" on
                // a machine with no credentials and no network, repealing the
                // guarantee above. Whoever bumps the version next inherits this:
                // the fallback reads envelopes, and must go on reading them.
                if (payload is { Game: { IgdbId: > 0 } outdated })
                {
                    superseded[id] = outdated;
                }
                else if (Deserialize<IgdbGame>(entry.PayloadJson) is { IgdbId: > 0 } legacy)
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
                }
                superseded.Remove(id);

                await _cache.SetAsync(
                    CacheProvider,
                    GameCacheKey(id),
                    Serialize(new GamePayload(GamePayloadVersion, game)),
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

    /// <summary>
    /// Cache key for an age-rating lookup. Own namespace, separate from
    /// <see cref="GameCacheKey"/>, so a deprecated-field 400 cannot invalidate
    /// the metadata cache.
    /// </summary>
    public static string AgeRatingsCacheKey(long igdbId)
        => "maturity:" + igdbId.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Shape version of a cached <c>maturity:</c> payload. Version 1 is the
    /// first and only shape so far. Bumping it makes every stored entry a miss
    /// on the next read, the same mechanism <see cref="GamePayloadVersion"/>
    /// uses.
    /// </summary>
    public const int AgeRatingsPayloadVersion = 1;

    private sealed record AgeRatingsPayload(int Version, IReadOnlyList<string>? Ratings);

    public async Task<IReadOnlyDictionary<long, IgdbAgeRatings>> GetAgeRatingsAsync(
        IEnumerable<long> igdbIds, TimeSpan? cacheTtl = null, CancellationToken ct = default)
    {
        var wanted = igdbIds.Where(id => id > 0).Distinct().ToArray();
        var results = new Dictionary<long, IgdbAgeRatings>();
        if (wanted.Length == 0)
        {
            return results;
        }

        var cached = await _cache.GetManyAsync(CacheProvider, wanted.Select(AgeRatingsCacheKey), ct);
        var cutoff = Cutoff(cacheTtl);
        var pending = new List<long>(wanted.Length);

        foreach (var id in wanted)
        {
            if (cached.TryGetValue(AgeRatingsCacheKey(id), out var entry) && entry.FetchedAt >= cutoff)
            {
                if (Deserialize<AgeRatingsPayload>(entry.PayloadJson) is
                    { Version: AgeRatingsPayloadVersion } payload)
                {
                    if (payload.Ratings is { Count: > 0 } ratings)
                        results[id] = new IgdbAgeRatings(id, ratings);
                    continue;
                }
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
            var page = await FetchAllAsync<IgdbAgeRatingsGameDto>(
                "games", (limit, offset) => Apicalypse.AgeRatings(batch, limit, offset), ct);

            if (!page.Succeeded)
            {
                // The query names deprecated fields. When IGDB rejects it
                // — the 400 a removed field would cause — re-ask with only
                // the current reference fields rather than losing the batch.
                page = await FetchAllAsync<IgdbAgeRatingsGameDto>(
                    "games",
                    (limit, offset) => Apicalypse.AgeRatingsWithoutDeprecatedFields(batch, limit, offset),
                    ct);
            }

            if (!page.Succeeded)
            {
                continue;
            }

            var found = new Dictionary<long, IReadOnlyList<string>>();
            foreach (var dto in page.Items)
            {
                if (dto.Id > 0 && RatingTokens(dto) is { Count: > 0 } tokens)
                {
                    found[dto.Id] = tokens;
                }
            }

            foreach (var id in batch)
            {
                var tokens = found.GetValueOrDefault(id);
                if (tokens is { Count: > 0 })
                {
                    results[id] = new IgdbAgeRatings(id, tokens);
                }

                await _cache.SetAsync(
                    CacheProvider,
                    AgeRatingsCacheKey(id),
                    Serialize(new AgeRatingsPayload(AgeRatingsPayloadVersion, tokens)),
                    fetchedAt,
                    ct);
            }
        }

        return results;
    }

    /// <summary>
    /// Maps one game's age-rating rows into distinct <c>board:tier</c> tokens.
    /// Three readings are tried per row in descending order of how firmly the
    /// value is established: the published rating enum, then the organization
    /// name and rating-category label, then the published category enum paired
    /// with that label. A row none of the three can name yields no token, so
    /// it cannot manufacture a row.
    /// </summary>
    private static IReadOnlyList<string> RatingTokens(IgdbAgeRatingsGameDto dto)
    {
        if (dto.AgeRatings is not { Count: > 0 } rows)
        {
            return [];
        }

        var tokens = new List<string>();
        foreach (var row in rows)
        {
            var token = IgdbAgeRatingTokens.FromLegacyRating(row.Rating)
                        ?? IgdbAgeRatingTokens.FromLabels(row.Organization?.Name, row.RatingCategory?.Rating)
                        ?? IgdbAgeRatingTokens.FromLegacyOrganization(row.Category, row.RatingCategory?.Rating);

            if (token is not null && !tokens.Contains(token, StringComparer.OrdinalIgnoreCase))
            {
                tokens.Add(token);
            }
        }

        return tokens;
    }

    /// <summary>
    /// Cache key for a search result set. The term is lower-cased so a
    /// repeat with different casing is a hit, and the limit is in the key
    /// because a 5-result answer must not be served to a caller asking
    /// for 20.
    /// </summary>
    public static string SearchCacheKey(string term, int limit)
        => "search:" + limit.ToString(CultureInfo.InvariantCulture) + ":" + term.ToLowerInvariant();

    /// <summary>
    /// Shape version of a cached <c>search:</c> payload. The same
    /// bump-to-invalidate mechanism <see cref="GamePayloadVersion"/> and
    /// <see cref="AgeRatingsPayloadVersion"/> use. Version 1 is the
    /// first shape.
    /// </summary>
    public const int SearchPayloadVersion = 1;

    /// <summary>Versioned envelope a search result set is cached in.</summary>
    private sealed record SearchPayload(int Version, IReadOnlyList<IgdbSearchResult>? Results);

    public async Task<IReadOnlyList<IgdbSearchResult>> SearchGamesAsync(
        string title, int limit = 0, TimeSpan? cacheTtl = null, CancellationToken ct = default)
    {
        var term = Apicalypse.SearchTerm(title);
        if (term is null)
        {
            return [];
        }

        var wanted = Math.Clamp(
            limit > 0 ? limit : _options.SearchResultLimit, 1, Apicalypse.MaxLimit);
        var key = SearchCacheKey(term, wanted);

        var cached = await _cache.GetAsync(CacheProvider, key, ct);
        if (cached is { } entry && entry.FetchedAt >= Cutoff(cacheTtl ?? _options.SearchCacheTtl))
        {
            if (Deserialize<SearchPayload>(entry.PayloadJson) is
                { Version: SearchPayloadVersion, Results: { } hits })
            {
                return hits;
            }
        }

        if (!await IsConfiguredAsync(ct))
        {
            return [];
        }

        // PostAsync directly, not FetchAllAsync: FetchAllAsync follows
        // offset pages until a page comes back short. A search deliberately
        // wants the top N by relevance, and paging the tail would spend the
        // 4 req/s budget walking results nobody asked for.
        var page = await PostAsync<IgdbSearchGameDto>(
            "games", Apicalypse.SearchGames(term, wanted), ct);

        if (!page.Succeeded)
        {
            // A failed request is NOT cached: a 400 or a dropped connection
            // would otherwise record "IGDB knows nothing by that name" for
            // a whole TTL.
            return [];
        }

        // An empty result IS cached: IGDB answered, and "no such title"
        // is a real answer worth keeping for the search TTL.
        var results = page.Items
            .Select(dto => dto.ToDomain())
            .Where(result => result is not null)
            .Select(result => result!)
            .Take(wanted)
            .ToArray();

        await _cache.SetAsync(
            CacheProvider,
            key,
            Serialize(new SearchPayload(SearchPayloadVersion, results)),
            _clock.GetUtcNow().UtcDateTime,
            ct);

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
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, IgdbJson.Options); }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Cached shape of an <c>external_games</c> match. The store id is the cache
    /// key, so it is not duplicated inside the payload. This inner shape also
    /// reads legacy unversioned mappings for the offline fallback.
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
