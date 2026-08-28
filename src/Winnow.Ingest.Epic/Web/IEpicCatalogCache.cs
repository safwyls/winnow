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
/// Where answers from Epic's catalog service are kept so a resync does not
/// refetch them.
///
/// <para><b>Per item, unlike <see cref="IEpicLibraryCache"/>.</b> The library is
/// one blob that is only ever wanted whole; the catalog is asked about whichever
/// handful of catalog item ids an enrichment slice happens to hold, and those
/// slices overlap across runs. One row per catalog item is what makes the second
/// run of a partially-enriched library free.</para>
///
/// <para><b>What may be cached, and what may not.</b> A parsed entry may. A
/// definite miss — the service answered and the id was not in the response — may,
/// as a null payload, because "Epic has no such item" does not become true later
/// in a way a shorter TTL would catch. A 5xx, a 429 the retries could not
/// outlast, a dead socket or an unparseable body may <b>not</b>: caching those
/// would record a transport failure as a fact about the user's library for a
/// whole TTL, which is the mistake this codebase has already paid for twice.</para>
///
/// <para><b>The default is in-memory, for the module-boundary reason recorded on
/// <see cref="IEpicLibraryCache"/>:</b> <c>Winnow.Ingest.Epic</c> does not
/// reference <c>Winnow.Data</c>. A host that wants these answers in the §6
/// <c>metadata_cache</c> table — which the app does — registers its own
/// implementation before calling <c>AddEpicWebApi</c>; every registration there
/// is <c>TryAdd</c>.</para>
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
