using System.Collections.Concurrent;

namespace Hoard.Ingest.Epic.Web;

/// <summary>A cached Epic library payload and when it was written (UTC).</summary>
/// <param name="PayloadJson">The stored payload, or null when the provider had no record.</param>
/// <param name="FetchedAt">When the entry was written (UTC).</param>
public readonly record struct EpicCacheEntry(string? PayloadJson, DateTime FetchedAt);

/// <summary>
/// Where a fetched Epic library is kept so a resync does not refetch it.
///
/// <para><b>Deliberately not the <c>metadata_cache</c> table.</b> The Steam Web,
/// IGDB and store modules each keep their own narrow view of that table, and this
/// module could too — at the cost of a <c>Hoard.Data</c> project reference in
/// <c>Hoard.Ingest.Epic</c>, whose local readers need nothing but the filesystem.
/// The boundary is worth more than the persistence: an in-memory cache is
/// entirely adequate for the way Hoard actually runs, since the app sits in the
/// tray and the snapshot scheduler resyncs every 15 minutes against a 6-hour TTL.
/// A restart costs exactly one refetch.</para>
///
/// <para>A host that wants the library cached across restarts registers its own
/// implementation before calling <c>AddEpicWebApi</c>; every registration there
/// is <c>TryAdd</c>.</para>
/// </summary>
public interface IEpicLibraryCache
{
    /// <summary>
    /// The stored entry, or null when absent. Freshness is the caller's
    /// decision — the store does not know any TTL.
    /// </summary>
    Task<EpicCacheEntry?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>Upserts one entry.</summary>
    Task SetAsync(string key, string? payloadJson, DateTime fetchedAt, CancellationToken ct = default);
}

/// <summary>
/// The default <see cref="IEpicLibraryCache"/>: process-lifetime, in memory.
/// </summary>
public sealed class InMemoryEpicLibraryCache : IEpicLibraryCache
{
    private readonly ConcurrentDictionary<string, EpicCacheEntry> _entries = new(StringComparer.Ordinal);

    public Task<EpicCacheEntry?> GetAsync(string key, CancellationToken ct = default)
        => Task.FromResult<EpicCacheEntry?>(_entries.TryGetValue(key, out var entry) ? entry : null);

    public Task SetAsync(string key, string? payloadJson, DateTime fetchedAt, CancellationToken ct = default)
    {
        _entries[key] = new EpicCacheEntry(payloadJson, fetchedAt.ToUniversalTime());
        return Task.CompletedTask;
    }
}
