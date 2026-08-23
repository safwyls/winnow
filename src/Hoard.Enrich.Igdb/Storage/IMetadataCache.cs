namespace Hoard.Enrich.Igdb.Storage;

/// <summary>One row of <c>metadata_cache</c>. A null payload is a cached miss.</summary>
/// <param name="PayloadJson">The stored payload, or null when the provider had no record.</param>
/// <param name="FetchedAt">When the row was written (UTC).</param>
public readonly record struct MetadataCacheEntry(string? PayloadJson, DateTime FetchedAt);

/// <summary>
/// The <c>metadata_cache(provider, provider_id, payload_json, fetched_at)</c>
/// table (§6). Every external response Hoard fetches lands here first; a hit
/// inside the TTL must never reach the network.
/// </summary>
public interface IMetadataCache
{
    /// <summary>
    /// The stored entry, or null when absent. Freshness is the caller's
    /// decision — the store does not know any TTL.
    /// </summary>
    Task<MetadataCacheEntry?> GetAsync(string provider, string providerId, CancellationToken ct = default);

    /// <summary>Bulk form of <see cref="GetAsync"/>; one round trip for a whole batch.</summary>
    Task<IReadOnlyDictionary<string, MetadataCacheEntry>> GetManyAsync(
        string provider, IEnumerable<string> providerIds, CancellationToken ct = default);

    /// <summary>Upserts one row. A null <paramref name="payloadJson"/> records a miss.</summary>
    Task SetAsync(
        string provider, string providerId, string? payloadJson, DateTime fetchedAt, CancellationToken ct = default);
}
