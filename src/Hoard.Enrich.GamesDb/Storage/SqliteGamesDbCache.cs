using Dapper;
using Hoard.Data;

namespace Hoard.Enrich.GamesDb.Storage;

/// <summary>Dapper-backed <see cref="IGamesDbCache"/> over the <c>metadata_cache</c> table.</summary>
public sealed class SqliteGamesDbCache : IGamesDbCache
{
    /// <summary><c>metadata_cache.provider</c> value for everything this module stores.</summary>
    public const string CacheProvider = "gamesdb";

    /// <summary>SQLite's default parameter ceiling is 999; chunk bulk reads well under it.</summary>
    private const int MaxParametersPerQuery = 500;

    private readonly ISqliteConnectionFactory _factory;

    public SqliteGamesDbCache(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<GamesDbCacheEntry?> GetAsync(string key, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var row = await lease.Connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition("""
            SELECT payload_json AS PayloadJson, fetched_at AS FetchedAt
            FROM metadata_cache
            WHERE provider = @provider AND provider_id = @key;
            """,
            new { provider = CacheProvider, key },
            transaction: lease.Transaction,
            cancellationToken: ct));

        return row is null ? null : new GamesDbCacheEntry(row.PayloadJson, row.FetchedAt);
    }

    public async Task<IReadOnlyDictionary<string, GamesDbCacheEntry>> GetManyAsync(
        IEnumerable<string> keys, CancellationToken ct = default)
    {
        var ids = keys.Distinct(StringComparer.Ordinal).ToArray();
        var result = new Dictionary<string, GamesDbCacheEntry>(StringComparer.Ordinal);
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
                """,
                new { provider = CacheProvider, ids = chunk },
                transaction: lease.Transaction,
                cancellationToken: ct));

            foreach (var row in rows)
            {
                result[row.ProviderId ?? string.Empty] = new GamesDbCacheEntry(row.PayloadJson, row.FetchedAt);
            }
        }

        return result;
    }

    public async Task SetAsync(
        string key, string? payloadJson, DateTime fetchedAt, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO metadata_cache (provider, provider_id, payload_json, fetched_at)
            VALUES (@provider, @key, @payloadJson, @fetchedAt)
            ON CONFLICT(provider, provider_id) DO UPDATE SET
                payload_json = excluded.payload_json,
                fetched_at   = excluded.fetched_at;
            """,
            new { provider = CacheProvider, key, payloadJson, fetchedAt = fetchedAt.ToUniversalTime() },
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

/// <summary>
/// In-memory <see cref="IGamesDbCache"/>. Tests and any host that has not opened
/// a database yet; nothing here survives the process.
/// </summary>
public sealed class InMemoryGamesDbCache : IGamesDbCache
{
    private readonly Dictionary<string, GamesDbCacheEntry> _entries = new(StringComparer.Ordinal);

    public Task<GamesDbCacheEntry?> GetAsync(string key, CancellationToken ct = default)
    {
        lock (_entries)
        {
            return Task.FromResult(_entries.TryGetValue(key, out var entry) ? entry : (GamesDbCacheEntry?)null);
        }
    }

    public Task<IReadOnlyDictionary<string, GamesDbCacheEntry>> GetManyAsync(
        IEnumerable<string> keys, CancellationToken ct = default)
    {
        var result = new Dictionary<string, GamesDbCacheEntry>(StringComparer.Ordinal);
        lock (_entries)
        {
            foreach (var key in keys)
            {
                if (_entries.TryGetValue(key, out var entry))
                {
                    result[key] = entry;
                }
            }
        }

        return Task.FromResult<IReadOnlyDictionary<string, GamesDbCacheEntry>>(result);
    }

    public Task SetAsync(string key, string? payloadJson, DateTime fetchedAt, CancellationToken ct = default)
    {
        lock (_entries)
        {
            _entries[key] = new GamesDbCacheEntry(payloadJson, fetchedAt);
        }

        return Task.CompletedTask;
    }
}
