using Hoard.Enrich.Igdb.Model;

namespace Hoard.Enrich.Igdb;

/// <summary>
/// Hoard's window onto IGDB. Every method is safe to call with no credentials
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
    Task<IReadOnlyDictionary<string, IgdbSteamMatch>> ResolveBySteamAppIdsAsync(
        IEnumerable<string> appIds, TimeSpan? cacheTtl = null, CancellationToken ct = default);

    /// <summary>
    /// Full metadata for known IGDB ids: name, cover, first release date,
    /// summary, genres, themes and publisher. Batched and cached like
    /// <see cref="ResolveBySteamAppIdsAsync"/>.
    /// </summary>
    Task<IReadOnlyList<IgdbGame>> GetGamesAsync(
        IEnumerable<long> igdbIds, TimeSpan? cacheTtl = null, CancellationToken ct = default);
}
