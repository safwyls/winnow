using Dapper;
using Winnow.Data;
using Winnow.Ingest.Epic.Web;

namespace Winnow.App.Services;

/// <summary>
/// Persists Epic catalog answers in the <c>metadata_cache</c> table. Lives in
/// the composition root because <c>Winnow.Ingest.Epic</c> does not reference
/// <c>Winnow.Data</c>. A null payload is a cached miss, not an empty row.
/// </summary>
public sealed class SqliteEpicCatalogCache : IEpicCatalogCache
{
    /// <summary>
    /// <c>metadata_cache.provider</c> for these rows. Namespaced away from
    /// <c>epic</c> so a future cache of a different Epic endpoint cannot collide
    /// with it on the same <c>(provider, provider_id)</c> primary key — the
    /// provider_id here is a catalog item id, and another endpoint keyed by the
    /// same id would silently overwrite these.
    /// </summary>
    public const string Provider = "epic-catalog";

    private readonly ISqliteConnectionFactory _factory;

    public SqliteEpicCatalogCache(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<EpicCatalogCacheEntry?> GetAsync(string catalogItemId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var row = await lease.Connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition("""
            SELECT payload_json AS PayloadJson, fetched_at AS FetchedAt
            FROM metadata_cache
            WHERE provider = @Provider AND provider_id = @catalogItemId;
            """,
            new { Provider, catalogItemId },
            transaction: lease.Transaction,
            cancellationToken: ct));

        return row is null ? null : new EpicCatalogCacheEntry(row.PayloadJson, row.FetchedAt);
    }

    public async Task SetAsync(
        string catalogItemId, string? payloadJson, DateTime fetchedAt, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO metadata_cache (provider, provider_id, payload_json, fetched_at)
            VALUES (@Provider, @catalogItemId, @payloadJson, @fetchedAt)
            ON CONFLICT(provider, provider_id) DO UPDATE SET
                payload_json = excluded.payload_json,
                fetched_at   = excluded.fetched_at;
            """,
            new { Provider, catalogItemId, payloadJson, fetchedAt = fetchedAt.ToUniversalTime() },
            transaction: lease.Transaction,
            cancellationToken: ct));
    }

    private sealed class Row
    {
        public string? PayloadJson { get; init; }

        public DateTime FetchedAt { get; init; }
    }
}
