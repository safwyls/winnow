using Dapper;
using Hoard.Data;

namespace Hoard.Enrich.SteamWeb.Storage;

/// <summary>Dapper-backed <see cref="ISteamWebMetadataCache"/> over the <c>metadata_cache</c> table.</summary>
public sealed class SqliteSteamWebMetadataCache : ISteamWebMetadataCache
{
    private readonly ISqliteConnectionFactory _factory;

    public SqliteSteamWebMetadataCache(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<SteamWebCacheEntry?> GetAsync(
        string provider, string providerId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var row = await lease.Connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition("""
            SELECT payload_json AS PayloadJson, fetched_at AS FetchedAt
            FROM metadata_cache
            WHERE provider = @provider AND provider_id = @providerId;
            """, new { provider, providerId }, transaction: lease.Transaction, cancellationToken: ct));

        return row is null ? null : new SteamWebCacheEntry(row.PayloadJson, row.FetchedAt);
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
        public string? PayloadJson { get; init; }

        public DateTime FetchedAt { get; init; }
    }
}
