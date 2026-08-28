using Dapper;
using Winnow.Data;

namespace Winnow.Enrich.Igdb.Storage;

/// <summary>Dapper-backed <see cref="IMetadataCache"/> over the <c>metadata_cache</c> table.</summary>
public sealed class SqliteMetadataCache : IMetadataCache
{
    /// <summary>
    /// SQLite's default parameter ceiling is 999; chunk bulk reads well under it.
    /// </summary>
    private const int MaxParametersPerQuery = 500;

    private readonly ISqliteConnectionFactory _factory;

    public SqliteMetadataCache(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<MetadataCacheEntry?> GetAsync(
        string provider, string providerId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var row = await lease.Connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition("""
            SELECT payload_json AS PayloadJson, fetched_at AS FetchedAt
            FROM metadata_cache
            WHERE provider = @provider AND provider_id = @providerId;
            """, new { provider, providerId }, transaction: lease.Transaction, cancellationToken: ct));

        return row is null ? null : new MetadataCacheEntry(row.PayloadJson, row.FetchedAt);
    }

    public async Task<IReadOnlyDictionary<string, MetadataCacheEntry>> GetManyAsync(
        string provider, IEnumerable<string> providerIds, CancellationToken ct = default)
    {
        var ids = providerIds.Distinct(StringComparer.Ordinal).ToArray();
        var result = new Dictionary<string, MetadataCacheEntry>(StringComparer.Ordinal);
        if (ids.Length == 0)
        {
            return result;
        }

        using var lease = _factory.Lease();
        foreach (var chunk in ids.Chunk(MaxParametersPerQuery))
        {
            var rows = await lease.Connection.QueryAsync<Row>(new CommandDefinition("""
                SELECT provider_id AS ProviderId, payload_json AS PayloadJson, fetched_at AS FetchedAt
                FROM metadata_cache
                WHERE provider = @provider AND provider_id IN @ids;
                """, new { provider, ids = chunk }, transaction: lease.Transaction, cancellationToken: ct));

            foreach (var row in rows)
            {
                result[row.ProviderId ?? string.Empty] = new MetadataCacheEntry(row.PayloadJson, row.FetchedAt);
            }
        }

        return result;
    }

    public async Task SetAsync(
        string provider,
        string providerId,
        string? payloadJson,
        DateTime fetchedAt,
        CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO metadata_cache (provider, provider_id, payload_json, fetched_at)
            VALUES (@provider, @providerId, @payloadJson, @fetchedAt)
            ON CONFLICT(provider, provider_id) DO UPDATE SET
                payload_json = excluded.payload_json,
                fetched_at   = excluded.fetched_at;
            """,
            new { provider, providerId, payloadJson, fetchedAt = fetchedAt.ToUniversalTime() },
            transaction: lease.Transaction,
            cancellationToken: ct));
    }

    private sealed class Row
    {
        public string? ProviderId { get; init; }

        public string? PayloadJson { get; init; }

        public DateTime FetchedAt { get; init; }
    }
}
