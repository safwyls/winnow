using System.Collections.Concurrent;

namespace Winnow.Enrich.Steam.Storage;

/// <summary>
/// Non-persistent <see cref="IStoreMetadataCache"/>. Useful for tests and for
/// hosts that deliberately run without a database; everything cached here is
/// refetched on the next process start, which is correct but wasteful —
/// production wires <see cref="SqliteStoreMetadataCache"/>.
/// </summary>
public sealed class InMemoryStoreMetadataCache : IStoreMetadataCache
{
    private readonly ConcurrentDictionary<(string Provider, string Id), StoreCacheEntry> _entries = new();

    public Task<StoreCacheEntry?> GetAsync(string provider, string providerId, CancellationToken ct = default)
        => Task.FromResult<StoreCacheEntry?>(
            _entries.TryGetValue((provider, providerId), out var entry) ? entry : null);

    public Task<IReadOnlyDictionary<string, StoreCacheEntry>> GetManyAsync(
        string provider, IEnumerable<string> providerIds, CancellationToken ct = default)
    {
        var result = new Dictionary<string, StoreCacheEntry>(StringComparer.Ordinal);
        foreach (var id in providerIds.Distinct(StringComparer.Ordinal))
        {
            if (_entries.TryGetValue((provider, id), out var entry))
            {
                result[id] = entry;
            }
        }

        return Task.FromResult<IReadOnlyDictionary<string, StoreCacheEntry>>(result);
    }

    public Task SetAsync(
        string provider, string providerId, string? payloadJson, DateTime fetchedAt, CancellationToken ct = default)
    {
        _entries[(provider, providerId)] = new StoreCacheEntry(payloadJson, fetchedAt.ToUniversalTime());
        return Task.CompletedTask;
    }
}
