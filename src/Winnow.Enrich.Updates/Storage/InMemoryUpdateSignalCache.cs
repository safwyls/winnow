using System.Collections.Concurrent;

namespace Winnow.Enrich.Updates.Storage;

/// <summary>
/// Non-persistent <see cref="IUpdateSignalCache"/>. Useful for tests and for
/// hosts deliberately running without a database — but note that in production
/// this would defeat the staggering entirely: poll state lives in this cache, so
/// a non-persistent one makes every app look never-polled on every start and the
/// sweep would restart from its baseline each launch.
/// </summary>
public sealed class InMemoryUpdateSignalCache : IUpdateSignalCache
{
    private readonly ConcurrentDictionary<(string Provider, string Id), UpdateCacheEntry> _entries = new();

    public Task<UpdateCacheEntry?> GetAsync(string provider, string providerId, CancellationToken ct = default)
        => Task.FromResult<UpdateCacheEntry?>(
            _entries.TryGetValue((provider, providerId), out var entry) ? entry : null);

    public Task<IReadOnlyDictionary<string, UpdateCacheEntry>> GetManyAsync(
        string provider, IEnumerable<string> providerIds, CancellationToken ct = default)
    {
        var result = new Dictionary<string, UpdateCacheEntry>(StringComparer.Ordinal);
        foreach (var id in providerIds.Distinct(StringComparer.Ordinal))
        {
            if (_entries.TryGetValue((provider, id), out var entry))
            {
                result[id] = entry;
            }
        }

        return Task.FromResult<IReadOnlyDictionary<string, UpdateCacheEntry>>(result);
    }

    public Task SetAsync(
        string provider, string providerId, string? payloadJson, DateTime fetchedAt, CancellationToken ct = default)
    {
        _entries[(provider, providerId)] = new UpdateCacheEntry(payloadJson, fetchedAt.ToUniversalTime());
        return Task.CompletedTask;
    }
}
