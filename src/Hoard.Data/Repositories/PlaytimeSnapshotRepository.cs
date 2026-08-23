using Dapper;
using Hoard.Core.Domain;
using Hoard.Core.Repositories;

namespace Hoard.Data.Repositories;

public sealed class PlaytimeSnapshotRepository : IPlaytimeSnapshotRepository
{
    private readonly ISqliteConnectionFactory _factory;

    public PlaytimeSnapshotRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<long> InsertAsync(PlaytimeSnapshot snapshot, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO playtime_snapshots (ownership_id, playtime_minutes, observed_at)
            VALUES (@OwnershipId, @PlaytimeMinutes, @ObservedAt)
            RETURNING id;
            """, snapshot, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<PlaytimeSnapshot>> GetByOwnershipAsync(long ownershipId, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        var rows = await conn.QueryAsync<PlaytimeSnapshot>(new CommandDefinition("""
            SELECT id               AS Id,
                   ownership_id     AS OwnershipId,
                   playtime_minutes AS PlaytimeMinutes,
                   observed_at      AS ObservedAt
            FROM playtime_snapshots
            WHERE ownership_id = @ownershipId
            ORDER BY observed_at, id;
            """, new { ownershipId }, cancellationToken: ct));
        return rows.AsList();
    }
}
