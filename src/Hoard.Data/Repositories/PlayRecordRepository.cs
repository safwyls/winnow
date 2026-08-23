using Dapper;
using Hoard.Core.Domain;
using Hoard.Core.Repositories;

namespace Hoard.Data.Repositories;

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
        using var conn = _factory.Open();
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO play_records (ownership_id, playtime_minutes, last_played_at, source, observed_at)
            VALUES (@OwnershipId, @PlaytimeMinutes, @LastPlayedAt, @Source, @ObservedAt)
            RETURNING id;
            """, record, cancellationToken: ct));
    }

    public async Task<PlayRecord?> GetLatestAsync(long ownershipId, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        return await conn.QuerySingleOrDefaultAsync<PlayRecord>(new CommandDefinition($"""
            SELECT {Columns}
            FROM play_records
            WHERE ownership_id = @ownershipId
            ORDER BY observed_at DESC, id DESC
            LIMIT 1;
            """, new { ownershipId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<PlayRecord>> GetByOwnershipAsync(long ownershipId, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        var rows = await conn.QueryAsync<PlayRecord>(new CommandDefinition($"""
            SELECT {Columns}
            FROM play_records
            WHERE ownership_id = @ownershipId
            ORDER BY observed_at, id;
            """, new { ownershipId }, cancellationToken: ct));
        return rows.AsList();
    }
}
