using Dapper;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

public sealed class GroupHeaderPreferenceRepository(ISqliteConnectionFactory factory) : IGroupHeaderPreferenceRepository
{
    public async Task<IReadOnlyDictionary<long, string?>> GetAllAsync(CancellationToken ct = default)
    {
        using var lease = factory.Lease();
        var rows = await lease.Connection.QueryAsync<PreferenceRow>(new CommandDefinition("""
            WITH ranked AS (
                SELECT COALESCE(link.parent_work_id, preference.work_id) AS RootWorkId,
                       preference.preferred_store AS Store,
                       ROW_NUMBER() OVER (
                           PARTITION BY COALESCE(link.parent_work_id, preference.work_id)
                           ORDER BY preference.revision DESC, preference.work_id) AS priority
                FROM group_header_preferences preference
                LEFT JOIN identity_links link ON link.child_work_id = preference.work_id
                    AND link.kind = 'same_game' AND link.retracted_at IS NULL
            )
            SELECT RootWorkId, Store FROM ranked WHERE priority = 1;
            """, transaction: lease.Transaction, cancellationToken: ct));
        return rows.ToDictionary(row => row.RootWorkId, row => row.Store);
    }

    public async Task<bool> SetAsync(long rootWorkId, string? store, CancellationToken ct = default)
    {
        using var lease = factory.Lease();
        return await lease.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO group_header_preferences (work_id, preferred_store, revision)
            SELECT @rootWorkId, @store, COALESCE((SELECT MAX(revision) FROM group_header_preferences), 0) + 1
            WHERE EXISTS (SELECT 1 FROM works WHERE id = @rootWorkId)
              AND NOT EXISTS (SELECT 1 FROM identity_links WHERE child_work_id = @rootWorkId AND retracted_at IS NULL)
              AND EXISTS (SELECT 1 FROM identity_links
                  WHERE parent_work_id = @rootWorkId AND kind = 'same_game' AND retracted_at IS NULL)
              AND (@store IS NULL OR EXISTS (
                  SELECT 1 FROM ownerships ownership JOIN releases release ON release.id = ownership.release_id
                  LEFT JOIN identity_links link ON link.child_work_id = release.work_id
                      AND link.kind = 'same_game' AND link.retracted_at IS NULL
                  WHERE COALESCE(link.parent_work_id, release.work_id) = @rootWorkId AND ownership.store = @store))
            ON CONFLICT(work_id) DO UPDATE SET preferred_store = excluded.preferred_store, revision = excluded.revision;
            """, new { rootWorkId, store }, transaction: lease.Transaction, cancellationToken: ct)) > 0;
    }

    private sealed record PreferenceRow
    {
        public long RootWorkId { get; init; }
        public string? Store { get; init; }
    }
}
