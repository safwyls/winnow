using Dapper;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

/// <summary>
/// SQLite implementation of the §6.2 achievement read. Groups by
/// <c>release_id</c> and by nothing else. There is no work join in this
/// file and there is no identity-link join in this file, and both absences
/// are the point — a same-game link folds identity everywhere else in the
/// read model, and it must not fold here, because two platforms' achievement
/// sets are two facts that stay two rows.
/// </summary>
public sealed class AchievementQueryRepository : IAchievementQueryRepository
{
    private readonly ISqliteConnectionFactory _factory;

    public AchievementQueryRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<ReleaseAchievementSummary>> GetSummariesAsync(
        IReadOnlyList<long> releaseIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(releaseIds);

        if (releaseIds.Count == 0)
        {
            return [];
        }

        // GROUP BY release_id: one row per release, never a row per work and
        // never a row per group. The unlock count is a correlated EXISTS rather
        // than a join so a release with no unlocks still reports its total.
        const string sql = """
            SELECT a.release_id AS ReleaseId,
                   COUNT(*)     AS Total,
                   SUM(CASE WHEN EXISTS (
                           SELECT 1 FROM achievement_unlocks u
                           WHERE u.release_id = a.release_id
                             AND u.provider_key = a.provider_key)
                       THEN 1 ELSE 0 END) AS Unlocked
            FROM achievements a
            WHERE a.release_id IN @ReleaseIds
            GROUP BY a.release_id
            ORDER BY a.release_id;
            """;

        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<ReleaseAchievementSummary>(
            new CommandDefinition(
                sql,
                new { ReleaseIds = releaseIds },
                transaction: lease.Transaction,
                cancellationToken: ct));

        return rows.AsList();
    }
}
