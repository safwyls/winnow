using System.Collections.Concurrent;

namespace Winnow.Ingest.Epic.Web;

/// <summary>A cached catalog answer for one catalog item, and when it was written (UTC).</summary>
/// <param name="PayloadJson">
/// The stored entry, or <b>null meaning "Epic answered, and has no such catalog
/// item"</b> — a cached miss, which is an answer worth keeping. Distinguish it
/// from no row at all, which is "never asked".
/// </param>
/// <param name="FetchedAt">When the entry was written (UTC).</param>
public readonly record struct EpicCatalogCacheEntry(string? PayloadJson, DateTime FetchedAt);

/// <summary>
/// Per-item cache for Epic catalog service answers. In-memory by default; hosts
/// may register a persistent implementation. A null payload records a definite
/// miss; transport failures must not be cached.
/// </summary>
public interface IEpicCatalogCache
{
    /// <summary>
    /// The stored entry, or null when the item has never been asked about.
    /// Freshness is the caller's decision — the store knows no TTL.
    /// </summary>
    Task<EpicCatalogCacheEntry?> GetAsync(string catalogItemId, CancellationToken ct = default);

    /// <summary>
    /// Upserts one entry. A null <paramref name="payloadJson"/> records a
    /// definite miss, not an unanswered request; callers must not use it for the
    /// latter.
    /// </summary>
    Task SetAsync(
        string catalogItemId, string? payloadJson, DateTime fetchedAt, CancellationToken ct = default);
}

/// <summary>
/// The default <see cref="IEpicCatalogCache"/>: process-lifetime, in memory.
/// A restart costs one refetch per catalog item still outstanding.
/// </summary>
public sealed class InMemoryEpicCatalogCache : IEpicCatalogCache
{
    private readonly ConcurrentDictionary<string, EpicCatalogCacheEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<EpicCatalogCacheEntry?> GetAsync(string catalogItemId, CancellationToken ct = default)
        => Task.FromResult<EpicCatalogCacheEntry?>(
            _entries.TryGetValue(catalogItemId, out var entry) ? entry : null);

    public Task SetAsync(
        string catalogItemId, string? payloadJson, DateTime fetchedAt, CancellationToken ct = default)
    {
        _entries[catalogItemId] = new EpicCatalogCacheEntry(payloadJson, fetchedAt.ToUniversalTime());
        return Task.CompletedTask;
    }
}
