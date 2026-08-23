using Dapper;
using Hoard.Core.Queries;
using Hoard.Core.Repositories;

namespace Hoard.Data.Repositories;

/// <summary>
/// The §6.1 derived-bucket query. Buckets are computed on read from stored
/// facts (latest play record, correlated update events) with caller-supplied
/// thresholds — never persisted, so thresholds can be retuned freely.
/// </summary>
public sealed class LibraryQueryRepository : ILibraryQueryRepository
{
    private readonly ISqliteConnectionFactory _factory;

    public LibraryQueryRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<OwnershipBucket>> GetOwnershipBucketsAsync(
        BucketThresholds thresholds, CancellationToken ct = default)
    {
        // Bucket precedence (§6.1): never_touched, bounced, retired,
        // stale_but_patched, active. Retired outranks stale on purpose —
        // high-playtime games are excluded from surfacing even when patched.
        const string sql = """
            WITH latest_play AS (
                -- The newest play record per ownership. observed_at is stored to
                -- whole seconds, so two scans in one second tie; the higher id
                -- is the later write. Same rule as PlayRecordRepository
                -- .GetLatestAsync, which a bare-column MAX() would not have
                -- agreed with (SQLite would pick an arbitrary row of the tie).
                SELECT ownership_id, playtime_minutes, last_played_at
                FROM (
                    SELECT ownership_id,
                           playtime_minutes,
                           last_played_at,
                           ROW_NUMBER() OVER (
                               PARTITION BY ownership_id
                               ORDER BY observed_at DESC, id DESC) AS rn
                    FROM play_records
                )
                WHERE rn = 1
            ),
            major_update AS (
                -- §4.5 and pitfall 4: a "major update" is a build push AND an
                -- announcement within the same window. Neither alone qualifies —
                -- a lone depot push is a DRM bump, a localization file or a
                -- one-line hotfix, and announcing "MAJOR UPDATE" on the strength
                -- of one is the single most visible way this feature can lie.
                --
                -- Correlation happens HERE, at read time, not at ingest: §4.5
                -- stores both raw signals precisely so the heuristic can be
                -- retuned (@UpdateCorrelationWindowDays) without re-fetching.
                --
                -- The build push is the moment the user's game actually changed,
                -- so it — not the announcement, which may tease or recap — is the
                -- timestamp compared against last-played.
                SELECT push.release_id,
                       MAX(push.occurred_at) AS occurred_at
                FROM update_events push
                WHERE push.kind = 'build_push'
                  AND EXISTS (
                      SELECT 1
                      FROM update_events news
                      WHERE news.release_id = push.release_id
                        AND news.kind = 'announcement'
                        AND abs(julianday(news.occurred_at) - julianday(push.occurred_at))
                            <= @UpdateCorrelationWindowDays
                  )
                GROUP BY push.release_id
            )
            SELECT o.id                                AS OwnershipId,
                   o.release_id                        AS ReleaseId,
                   COALESCE(lp.playtime_minutes, 0)    AS PlaytimeMinutes,
                   lp.last_played_at                   AS LastPlayedAt,
                   CASE
                       -- Never touched means no evidence of play at all: no
                       -- minutes AND no last-played date. Zero minutes beside a
                       -- real last-played date is not evidence of no play — it
                       -- is a source admitting it did not measure the session.
                       -- The case that forces this is an appmanifest LastPlayed
                       -- on a machine whose userdata/ is unreadable: the game was
                       -- demonstrably launched and the minutes are unknown, not
                       -- zero. Unknown is neither "never touched" nor "bounced"
                       -- (which claims a small, KNOWN number of minutes), so such
                       -- a row is bucketed on staleness alone below.
                       WHEN COALESCE(lp.playtime_minutes, 0) = 0
                            AND lp.last_played_at IS NULL
                           THEN 'never_touched'
                       WHEN lp.playtime_minutes > 0
                            AND lp.playtime_minutes < @BouncedCeilingMinutes
                           THEN 'bounced'
                       WHEN lp.playtime_minutes >= @RetiredFloorMinutes
                           THEN 'retired'
                       -- A NULL last_played_at with real playtime is Steam's
                       -- 86400 sentinel: played before Steam tracked timestamps
                       -- (docs/spikes/steam-local-files.md). "Unknown, certainly
                       -- ancient" is maximally dormant, not active — treating it
                       -- as active structurally excluded the oldest pile in the
                       -- library from the one bucket built to resurface it.
                       WHEN mu.occurred_at IS NOT NULL
                            AND (lp.last_played_at IS NULL
                                 OR datetime(mu.occurred_at) >
                                    datetime(lp.last_played_at, '+' || @StaleWindowMonths || ' months'))
                           THEN 'stale_but_patched'
                       ELSE 'active'
                   END                                 AS Bucket
            FROM ownerships o
            LEFT JOIN latest_play   lp ON lp.ownership_id = o.id
            LEFT JOIN major_update  mu ON mu.release_id = o.release_id
            ORDER BY o.id;
            """;

        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<OwnershipBucket>(new CommandDefinition(sql, new
        {
            thresholds.BouncedCeilingMinutes,
            thresholds.RetiredFloorMinutes,
            thresholds.StaleWindowMonths,
            thresholds.UpdateCorrelationWindowDays,
        }, transaction: lease.Transaction, cancellationToken: ct));
        return rows.AsList();
    }
}
