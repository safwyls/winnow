using Winnow.Enrich.Igdb.Model;

namespace Winnow.Enrich.Igdb;

/// <summary>
/// Winnow's window onto IGDB. Every method is safe to call with no credentials
/// configured: the client then serves whatever is cached and returns empty for
/// the rest, because §5.1 forbids enrichment from blocking or breaking anything.
/// </summary>
public interface IIgdbClient
{
    /// <summary>
    /// Whether credentials resolved from any source. False is a normal state,
    /// not an error — it just means enrichment is off.
    /// </summary>
    ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default);

    /// <summary>
    /// Maps Steam appids to IGDB games through <c>external_games</c> — the
    /// high-precision hard join §4.4 calls the backbone of entity resolution.
    ///
    /// <para>Appids are batched into requests of
    /// <see cref="IgdbOptions.BatchSize"/> (500 by default, the Apicalypse
    /// maximum), so a 616-game library costs two requests rather than 616.
    /// Cached appids — hits and cached misses alike — are removed before
    /// batching, so a warm library costs none at all.</para>
    /// </summary>
    /// <param name="appIds">Steam appids as strings. Duplicates are collapsed.</param>
    /// <param name="cacheTtl">Overrides <see cref="IgdbOptions.CacheTtl"/> for this call.</param>
    /// <returns>Appid → match, containing only appids IGDB knows.</returns>
    Task<IReadOnlyDictionary<string, IgdbExternalMatch>> ResolveBySteamAppIdsAsync(
        IEnumerable<string> appIds, TimeSpan? cacheTtl = null, CancellationToken ct = default);

    /// <summary>
    /// The general <c>external_games</c> hard join against any
    /// <c>external_game_source</c> (Steam = 1, GOG = 5, Epic = 26). Batched
    /// and cached under source-scoped keys. A failed batch is not cached.
    /// </summary>
    /// <param name="externalGameSourceId">
    /// IGDB's <c>external_game_source</c> id. Take it from
    /// <see cref="IgdbOptions.ExternalGameSourceIdFor"/> rather than writing a
    /// literal, so an unmappable provider is a null the caller must handle.
    /// </param>
    /// <param name="uids">Store ids as strings. Duplicates are collapsed.</param>
    /// <param name="cacheTtl">Overrides <see cref="IgdbOptions.CacheTtl"/> for this call.</param>
    /// <returns>Store id → match, containing only ids IGDB knows under that source.</returns>
    Task<IReadOnlyDictionary<string, IgdbExternalMatch>> ResolveByExternalIdsAsync(
        int externalGameSourceId,
        IEnumerable<string> uids,
        TimeSpan? cacheTtl = null,
        CancellationToken ct = default);

    /// <summary>
    /// Full metadata for known IGDB ids: name, cover, first release date,
    /// summary, genres, themes, platforms and publisher. Batched and cached like
    /// <see cref="ResolveBySteamAppIdsAsync"/>.
    /// </summary>
    Task<IReadOnlyList<IgdbGame>> GetGamesAsync(
        IEnumerable<long> igdbIds, TimeSpan? cacheTtl = null, CancellationToken ct = default);

    /// <summary>
    /// Age-rating tokens for known IGDB ids. Rides its own Apicalypse query
    /// against <c>games</c> and its own cache namespace (<c>maturity:&lt;igdbId&gt;</c>),
    /// never the shared <see cref="GetGamesAsync"/> query, because the query
    /// names deprecated fields and a field IGDB finally removes would 400 the
    /// whole body — on the shared query that single 400 would cost name, cover
    /// art, genres, themes, game modes, perspectives and publisher for the
    /// entire library.
    ///
    /// <para>Returns only ids IGDB reported at least one mappable rating for.
    /// A game with no age ratings, or only ratings this client cannot name,
    /// is absent rather than present-and-empty.</para>
    /// </summary>
    Task<IReadOnlyDictionary<long, IgdbAgeRatings>> GetAgeRatingsAsync(
        IEnumerable<long> igdbIds, TimeSpan? cacheTtl = null, CancellationToken ct = default);

    /// <summary>
    /// Searches IGDB by title using the <c>search "…"</c> clause on the
    /// <c>games</c> endpoint. Rides its own query body and its own cache
    /// namespace (<c>search:</c>), never the shared
    /// <see cref="GetGamesAsync"/> query, because the query carries
    /// user-typed free text and a 400 on it must cost only the search.
    ///
    /// <para>A <paramref name="limit"/> of 0 means "use
    /// <see cref="IgdbOptions.SearchResultLimit"/>". An empty list is
    /// returned for a blank term, for an unconfigured client, and for a
    /// failed request; none of those three is an error.</para>
    /// </summary>
    Task<IReadOnlyList<IgdbSearchResult>> SearchGamesAsync(
        string title, int limit = 0, TimeSpan? cacheTtl = null, CancellationToken ct = default);
}
