using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

public sealed class PlaytimeSnapshotRepository : IPlaytimeSnapshotRepository
{
    private readonly ISqliteConnectionFactory _factory;

    public PlaytimeSnapshotRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<long> InsertAsync(PlaytimeSnapshot snapshot, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        return await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO playtime_snapshots (ownership_id, playtime_minutes, observed_at)
            VALUES (@OwnershipId, @PlaytimeMinutes, @ObservedAt)
            RETURNING id;
            """, snapshot, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<PlaytimeSnapshot?> GetLatestAsync(long ownershipId, CancellationToken ct = default)
    {
        // Same ordering as PlayRecordRepository.GetLatestAsync: observed_at is
        // stored to whole-second resolution, so two scans in the same second
        // tie and the higher id is the later write.
        using var lease = _factory.Lease();
        return await lease.Connection.QuerySingleOrDefaultAsync<PlaytimeSnapshot>(new CommandDefinition("""
            SELECT id               AS Id,
                   ownership_id     AS OwnershipId,
                   playtime_minutes AS PlaytimeMinutes,
                   observed_at      AS ObservedAt
            FROM playtime_snapshots
            WHERE ownership_id = @ownershipId
            ORDER BY observed_at DESC, id DESC
            LIMIT 1;
            """, new { ownershipId }, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<PlaytimeSnapshot>> GetByOwnershipAsync(long ownershipId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<PlaytimeSnapshot>(new CommandDefinition("""
            SELECT id               AS Id,
                   ownership_id     AS OwnershipId,
                   playtime_minutes AS PlaytimeMinutes,
                   observed_at      AS ObservedAt
            FROM playtime_snapshots
            WHERE ownership_id = @ownershipId
            ORDER BY observed_at, id;
            """, new { ownershipId }, transaction: lease.Transaction, cancellationToken: ct));
        return rows.AsList();
    }
}
