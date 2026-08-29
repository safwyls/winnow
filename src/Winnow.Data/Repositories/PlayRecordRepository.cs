using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

public sealed class PlayRecordRepository : IPlayRecordRepository
{
    private const string Columns = """
        id               AS Id,
        ownership_id     AS OwnershipId,
        playtime_minutes AS PlaytimeMinutes,
        last_played_at   AS LastPlayedAt,
        source           AS Source,
        observed_at      AS ObservedAt
        """;

    private readonly ISqliteConnectionFactory _factory;

    public PlayRecordRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<long> InsertAsync(PlayRecord record, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        return await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO play_records (ownership_id, playtime_minutes, last_played_at, source, observed_at)
            VALUES (@OwnershipId, @PlaytimeMinutes, @LastPlayedAt, @Source, @ObservedAt)
            RETURNING id;
            """, record, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<long?> TryAppendAsync(PlayRecord record, CancellationToken ct = default)
    {
        // Untargeted DO NOTHING: covers the COALESCE expression index from 0013
        // without naming it, while FK/CHECK violations still throw.
        using var lease = _factory.Lease();
        return await lease.Connection.ExecuteScalarAsync<long?>(new CommandDefinition("""
            INSERT INTO play_records (ownership_id, playtime_minutes, last_played_at, source, observed_at)
            VALUES (@OwnershipId, @PlaytimeMinutes, @LastPlayedAt, @Source, @ObservedAt)
            ON CONFLICT DO NOTHING
            RETURNING id;
            """, record, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<PlayRecord?> GetLatestAsync(long ownershipId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        return await lease.Connection.QuerySingleOrDefaultAsync<PlayRecord>(new CommandDefinition($"""
            SELECT {Columns}
            FROM play_records
            WHERE ownership_id = @ownershipId
            ORDER BY observed_at DESC, id DESC
            LIMIT 1;
            """, new { ownershipId }, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<PlayRecord>> GetByOwnershipAsync(long ownershipId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<PlayRecord>(new CommandDefinition($"""
            SELECT {Columns}
            FROM play_records
            WHERE ownership_id = @ownershipId
            ORDER BY observed_at, id;
            """, new { ownershipId }, transaction: lease.Transaction, cancellationToken: ct));
        return rows.AsList();
    }
}
