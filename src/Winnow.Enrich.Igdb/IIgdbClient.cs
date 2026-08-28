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
    /// The same <c>external_games</c> hard join against any
    /// <c>external_game_source</c> — Steam is 1, GOG is 5, Epic is 26 — rather
    /// than Steam alone.
    ///
    /// <para><b>Why this is the general method and the Steam one is the
    /// special case.</b> The Steam-only shape above is what the whole enrichment
    /// path was built on, and it meant the 67 Epic and 14 GOG releases in the
    /// author's library had exactly zero <c>igdb_id</c>, cover, year and
    /// summary between them. GOG needs nothing more than this call with source
    /// 5: its product ids are IGDB's uids verbatim.</para>
    ///
    /// <para>Batching, caching and cached misses work exactly as they do for
    /// Steam, but under a source-scoped cache key — the same string can be a
    /// valid id on two storefronts, and a GOG miss must never be read back as a
    /// Steam answer.</para>
    ///
    /// <para><b>A failed batch is not a miss.</b> Nothing is cached for a
    /// request that did not complete, so a source with nothing to say leaves the
    /// caller with no entry rather than an authoritative empty one.</para>
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
    /// summary, genres, themes and publisher. Batched and cached like
    /// <see cref="ResolveBySteamAppIdsAsync"/>.
    /// </summary>
    Task<IReadOnlyList<IgdbGame>> GetGamesAsync(
        IEnumerable<long> igdbIds, TimeSpan? cacheTtl = null, CancellationToken ct = default);
}
