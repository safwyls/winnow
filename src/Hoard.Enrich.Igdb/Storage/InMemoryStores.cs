using System.Collections.Concurrent;

namespace Hoard.Enrich.Igdb.Storage;

/// <summary>
/// Non-persistent <see cref="ISettingsStore"/>. Useful for tests and for hosts
/// that deliberately run without a database; a token cached here is re-minted
/// on the next process start, which is correct but wasteful — production wires
/// <see cref="SqliteSettingsStore"/>.
/// </summary>
public sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly ConcurrentDictionary<string, string?> _values = new(StringComparer.Ordinal);

    public Task<string?> GetAsync(string key, CancellationToken ct = default)
        => Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);

    public Task SetAsync(string key, string? value, CancellationToken ct = default)
    {
        _values[key] = value;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _values.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}

/// <summary>Non-persistent <see cref="IMetadataCache"/>, same caveat as <see cref="InMemorySettingsStore"/>.</summary>
public sealed class InMemoryMetadataCache : IMetadataCache
{
    private readonly ConcurrentDictionary<(string Provider, string Id), MetadataCacheEntry> _entries = new();

    public Task<MetadataCacheEntry?> GetAsync(string provider, string providerId, CancellationToken ct = default)
        => Task.FromResult<MetadataCacheEntry?>(
            _entries.TryGetValue((provider, providerId), out var entry) ? entry : null);

    public Task<IReadOnlyDictionary<string, MetadataCacheEntry>> GetManyAsync(
        string provider, IEnumerable<string> providerIds, CancellationToken ct = default)
    {
        var result = new Dictionary<string, MetadataCacheEntry>(StringComparer.Ordinal);
        foreach (var id in providerIds.Distinct(StringComparer.Ordinal))
        {
            if (_entries.TryGetValue((provider, id), out var entry))
            {
                result[id] = entry;
            }
        }

        return Task.FromResult<IReadOnlyDictionary<string, MetadataCacheEntry>>(result);
    }

    public Task SetAsync(
        string provider, string providerId, string? payloadJson, DateTime fetchedAt, CancellationToken ct = default)
    {
        _entries[(provider, providerId)] = new MetadataCacheEntry(payloadJson, fetchedAt.ToUniversalTime());
        return Task.CompletedTask;
    }
}
