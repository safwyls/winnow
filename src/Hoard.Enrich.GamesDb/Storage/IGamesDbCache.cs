namespace Hoard.Enrich.GamesDb.Storage;

/// <summary>One row of <c>metadata_cache</c>. A null payload is a cached miss.</summary>
/// <param name="PayloadJson">The stored projection, or null when gamesdb had no record.</param>
/// <param name="FetchedAt">When the row was written (UTC).</param>
public readonly record struct GamesDbCacheEntry(string? PayloadJson, DateTime FetchedAt);

/// <summary>
/// The <c>metadata_cache(provider, provider_id, payload_json, fetched_at)</c>
/// table (§6), scoped to this module's provider key.
///
/// <para>Its own interface rather than a shared one because §5.1 keeps
/// <c>Enrich.*</c> modules peers: the IGDB module owns its cache contract, the
/// update poller owns its own, and neither should become a dependency of the
/// other by way of a storage type.</para>
/// </summary>
public interface IGamesDbCache
{
    /// <summary>
    /// The stored entry, or null when absent. Freshness is the caller's
    /// decision — the store knows no TTL.
    /// </summary>
    Task<GamesDbCacheEntry?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>Bulk form of <see cref="GetAsync"/>; one round trip for a whole library.</summary>
    Task<IReadOnlyDictionary<string, GamesDbCacheEntry>> GetManyAsync(
        IEnumerable<string> keys, CancellationToken ct = default);

    /// <summary>Upserts one row. A null <paramref name="payloadJson"/> records a miss.</summary>
    Task SetAsync(string key, string? payloadJson, DateTime fetchedAt, CancellationToken ct = default);
}
