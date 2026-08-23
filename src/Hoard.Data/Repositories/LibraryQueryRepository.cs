using Dapper;
using Hoard.Core.Queries;
using Hoard.Core.Repositories;

namespace Hoard.Data.Repositories;

/// <summary>
/// The §6.1 derived-bucket query. Buckets are computed on read from stored
/// facts (latest play record, latest update event) with caller-supplied
/// thresholds — never persisted, so thresholds can be retuned freely.
/// </summary>
public sealed class LibraryQueryRepository : ILibraryQueryRepository
{
    private readonly ISqliteConnectionFactory _factory;

    public LibraryQueryRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<OwnershipBucket>> GetOwnershipBucketsAsync(
        BucketThresholds thresholds, CancellationToken ct = default)
    {
        // latest_play: SQLite guarantees that bare columns accompanying a
        // bare MAX() come from the row that supplied the maximum.
        //
        // Bucket precedence (§6.1): never_touched, bounced, retired,
        // stale_but_patched, active. Retired outranks stale on purpose —
        // high-playtime games are excluded from surfacing even when patched.
        const string sql = """
            WITH latest_play AS (
                SELECT ownership_id,
                       playtime_minutes,
                       last_played_at,
                       MAX(observed_at) AS observed_at
                FROM play_records
                GROUP BY ownership_id
            ),
            latest_update AS (
                SELECT release_id,
                       MAX(occurred_at) AS occurred_at
                FROM update_events
                GROUP BY release_id
            )
            SELECT o.id                                AS OwnershipId,
                   o.release_id                        AS ReleaseId,
                   COALESCE(lp.playtime_minutes, 0)    AS PlaytimeMinutes,
                   lp.last_played_at                   AS LastPlayedAt,
                   CASE
                       WHEN COALESCE(lp.playtime_minutes, 0) = 0
                           THEN 'never_touched'
                       WHEN lp.playtime_minutes < @BouncedCeilingMinutes
                           THEN 'bounced'
                       WHEN lp.playtime_minutes >= @RetiredFloorMinutes
                           THEN 'retired'
                       WHEN lu.occurred_at IS NOT NULL
                            AND lp.last_played_at IS NOT NULL
                            AND datetime(lu.occurred_at) >
                                datetime(lp.last_played_at, '+' || @StaleWindowMonths || ' months')
                           THEN 'stale_but_patched'
                       ELSE 'active'
                   END                                 AS Bucket
            FROM ownerships o
            LEFT JOIN latest_play   lp ON lp.ownership_id = o.id
            LEFT JOIN latest_update lu ON lu.release_id = o.release_id
            ORDER BY o.id;
            """;

        using var conn = _factory.Open();
        var rows = await conn.QueryAsync<OwnershipBucket>(new CommandDefinition(sql, new
        {
            thresholds.BouncedCeilingMinutes,
            thresholds.RetiredFloorMinutes,
            thresholds.StaleWindowMonths,
        }, cancellationToken: ct));
        return rows.AsList();
    }
}
