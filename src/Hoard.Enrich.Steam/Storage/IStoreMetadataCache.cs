namespace Hoard.Enrich.Steam.Storage;

/// <summary>One row of <c>metadata_cache</c>. A null payload is a cached miss.</summary>
/// <param name="PayloadJson">The stored payload, or null when the provider had no record.</param>
/// <param name="FetchedAt">When the row was written (UTC).</param>
public readonly record struct StoreCacheEntry(string? PayloadJson, DateTime FetchedAt);

/// <summary>
/// The <c>metadata_cache(provider, provider_id, payload_json, fetched_at)</c>
/// table (§6). Every store response Hoard fetches lands here first; a hit inside
/// the TTL must never reach the network.
///
/// <para>This is a deliberate peer of the IGDB module's equivalent rather than a
/// shared type. Both write the same table under different <c>provider</c> values,
/// but §5.1 keeps <c>Enrich.*</c> modules independent of one another, and this
/// one has to keep working when IGDB is not configured at all.</para>
/// </summary>
public interface IStoreMetadataCache
{
    /// <summary>
    /// The stored entry, or null when absent. Freshness is the caller's
    /// decision — the store does not know any TTL.
    /// </summary>
    Task<StoreCacheEntry?> GetAsync(string provider, string providerId, CancellationToken ct = default);

    /// <summary>Bulk form of <see cref="GetAsync"/>; one round trip for a whole batch.</summary>
    Task<IReadOnlyDictionary<string, StoreCacheEntry>> GetManyAsync(
        string provider, IEnumerable<string> providerIds, CancellationToken ct = default);

    /// <summary>Upserts one row. A null <paramref name="payloadJson"/> records a miss.</summary>
    Task SetAsync(
        string provider, string providerId, string? payloadJson, DateTime fetchedAt, CancellationToken ct = default);
}
