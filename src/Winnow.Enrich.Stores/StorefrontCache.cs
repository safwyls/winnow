using Dapper;
using Winnow.Data;
using Winnow.Core.Repositories;

namespace Winnow.Enrich.Stores;

public sealed record StorefrontCacheEntry(string Payload, DateTime FetchedAt);

/// <summary>Stored responses only; safe to read while constructing the library.</summary>
public sealed class StorefrontCache(ISqliteConnectionFactory factory) : IStorefrontRepository
{
    public async Task<StorefrontCacheEntry?> GetAsync(string key, CancellationToken ct = default)
    {
        using var lease = factory.Lease();
        return await lease.Connection.QuerySingleOrDefaultAsync<StorefrontCacheEntry>(new CommandDefinition(
            "SELECT payload_json AS Payload, fetched_at AS FetchedAt FROM metadata_cache WHERE provider = 'storefront-v1' AND provider_id = @key",
            new { key }, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task SaveAsync(string key, string payload, DateTime fetchedAt, CancellationToken ct = default)
    {
        using var lease = factory.Lease();
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO metadata_cache(provider, provider_id, payload_json, fetched_at)
            VALUES ('storefront-v1', @key, @payload, @fetchedAt)
            ON CONFLICT(provider, provider_id) DO UPDATE SET payload_json=excluded.payload_json, fetched_at=excluded.fetched_at
            """, new { key, payload, fetchedAt }, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyDictionary<string, StorefrontDetails>> ReadAllAsync(CancellationToken ct = default)
    {
        using var lease = factory.Lease();
        var rows = await lease.Connection.QueryAsync<CacheRow>(new CommandDefinition(
            "SELECT provider_id AS Id, payload_json AS Payload FROM metadata_cache WHERE provider = 'storefront-v1'",
            transaction: lease.Transaction, cancellationToken: ct));
        var result = new Dictionary<string, StorefrontDetails>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (row.Id == "epic")
                foreach (var item in StorefrontClient.ParseEpic(row.Payload))
                    result["epic:" + item.Key] = new(item.Value, null);
            else if (row.Id.StartsWith("gog:", StringComparison.Ordinal))
                result[row.Id] = StorefrontClient.ParseGog(row.Payload);
            else if (row.Id.StartsWith("epic-namespace:", StringComparison.Ordinal)
                && StorefrontClient.TryParseEpicNamespace(row.Payload, out var url) && url is not null)
                result.TryAdd("epic:" + row.Id["epic-namespace:".Length..], new(url, null));
        }
        return result;
    }

    private sealed record CacheRow(string Id, string? Payload);
}
