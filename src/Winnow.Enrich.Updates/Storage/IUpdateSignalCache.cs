namespace Winnow.Enrich.Updates.Storage;

/// <summary>One row of <c>metadata_cache</c>. A null payload is a cached miss.</summary>
/// <param name="PayloadJson">The stored payload, or null when the provider had no record.</param>
/// <param name="FetchedAt">When the row was written (UTC).</param>
public readonly record struct UpdateCacheEntry(string? PayloadJson, DateTime FetchedAt);

/// <summary>
/// Access to the <c>metadata_cache</c> table for this module. Three providers:
/// <c>steam-news</c> (no-feed negatives), <c>steamcmd</c> (build-info bodies),
/// <c>update-poll</c> (per-appid poll state). Losing this table is safe -- it
/// costs requests on re-observation, never correctness.
/// </summary>
public interface IUpdateSignalCache
{
    /// <summary>The stored entry, or null when absent. The store knows no TTL; freshness is the caller's decision.</summary>
    Task<UpdateCacheEntry?> GetAsync(string provider, string providerId, CancellationToken ct = default);

    /// <summary>Bulk form of <see cref="GetAsync"/>; one round trip for a whole batch.</summary>
    Task<IReadOnlyDictionary<string, UpdateCacheEntry>> GetManyAsync(
        string provider, IEnumerable<string> providerIds, CancellationToken ct = default);

    /// <summary>Upserts one row. A null <paramref name="payloadJson"/> records a miss.</summary>
    Task SetAsync(
        string provider, string providerId, string? payloadJson, DateTime fetchedAt, CancellationToken ct = default);
}
