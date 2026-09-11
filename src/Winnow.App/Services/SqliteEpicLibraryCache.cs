using Dapper;
using Winnow.Data;
using Winnow.Ingest.Epic.Web;

namespace Winnow.App.Services;

/// <summary>
/// Persists the authenticated Epic library response in <c>metadata_cache</c>.
/// This App-layer implementation keeps the ingest module free of a data-layer
/// reference while making the cache follow the selected data directory.
/// </summary>
public sealed class SqliteEpicLibraryCache : IEpicLibraryCache
{
    /// <summary>Namespace reserved for authenticated account-level library payloads.</summary>
    public const string Provider = "epic-library";

    private readonly ISqliteConnectionFactory _factory;

    public SqliteEpicLibraryCache(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<EpicCacheEntry?> GetAsync(string key, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var row = await lease.Connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition("""
            SELECT payload_json AS PayloadJson, fetched_at AS FetchedAt
            FROM metadata_cache
            WHERE provider = @Provider AND provider_id = @key;
            """,
            new { Provider, key },
            transaction: lease.Transaction,
            cancellationToken: ct));

        return row is null ? null : new EpicCacheEntry(row.PayloadJson, row.FetchedAt);
    }

    public async Task SetAsync(string key, string? payloadJson, DateTime fetchedAt, CancellationToken ct = default)
    {
        if (fetchedAt.Kind == DateTimeKind.Unspecified)
        {
            throw new ArgumentException("The cache timestamp must identify a time zone.", nameof(fetchedAt));
        }

        using var lease = _factory.Lease();
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO metadata_cache (provider, provider_id, payload_json, fetched_at)
            VALUES (@Provider, @key, @payloadJson, @fetchedAt)
            ON CONFLICT(provider, provider_id) DO UPDATE SET
                payload_json = excluded.payload_json,
                fetched_at   = excluded.fetched_at;
            """,
            new { Provider, key, payloadJson, fetchedAt = fetchedAt.ToUniversalTime() },
            transaction: lease.Transaction,
            cancellationToken: ct));
    }

    private sealed class Row
    {
        public string? PayloadJson { get; init; }

        public DateTime FetchedAt { get; init; }
    }
}
