using Winnow.Enrich.GamesDb.Model;

namespace Winnow.Enrich.GamesDb;

/// <summary>
/// "Which other stores sell this same game?" — answered by exact identifier,
/// never by title.
///
/// <para><b>Why Winnow needs this at all.</b> §4.4 makes IGDB the metadata
/// backbone and §5.3 layer 1 makes <c>external_games</c> the hard join. That
/// works for Steam (source 1) and for GOG (source 5). It does <b>not</b> work
/// for Epic: IGDB's source-26 uids are Epic store <i>offer</i> ids and CMS
/// <i>page</i> ids, while the launcher writes <c>CatalogItemId</c> — different
/// id spaces for the same game, measured at 0 matches out of 67 owned titles,
/// with some titles (ABZU) carrying no source-26 row at all. So an Epic title
/// cannot reach IGDB directly, and the alternative — matching on normalised
/// title — is the one thing §5.3 says must never be automated.</para>
///
/// <para>This is the exact-identifier route that closes the gap: gamesdb's graph
/// resolves <c>epic/Bluebird</c> and <c>steam/224760</c> to the same
/// <c>game_id</c>, which turns an Epic title into a Steam appid, which is a
/// lookup that already works for 946 titles. Measured on the author's library:
/// 67 of 67 Epic titles resolved, 62 carrying a Steam id.</para>
///
/// <para>Every implementation must be soft-failing. Nothing here is a documented
/// API and §5.1 forbids enrichment from breaking a caller: an outage, a shape
/// change or a dead host must degrade to "no answer, ask again next run", never
/// to an exception and never to a cached "this game does not exist".</para>
/// </summary>
public interface IGameIdentityGraph
{
    /// <summary>
    /// Resolves one store id, or returns null when the graph has no release
    /// under it.
    ///
    /// <para><b>Null is ambiguous by design and callers must not disambiguate
    /// it.</b> It covers both "gamesdb answered 404" and "gamesdb could not be
    /// reached", and the only safe reading of either is "learned nothing this
    /// run". Writing anything into the database on the strength of a null here
    /// would be recording a source's silence as an answer.</para>
    /// </summary>
    /// <param name="platform">A <see cref="GamesDbPlatforms"/> value.</param>
    /// <param name="externalId">
    /// That platform's id. For Epic this is the manifest's <c>AppName</c>, not
    /// the catalog item id — the catalog item id 404s.
    /// </param>
    /// <param name="ct">Cancellation.</param>
    Task<GamesDbGame?> ResolveAsync(string platform, string externalId, CancellationToken ct = default);
}
