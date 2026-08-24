using Dapper;
using Hoard.Core.Queries;
using Hoard.Core.Repositories;

namespace Hoard.Data.Repositories;

/// <summary>
/// The §6.1 derived-bucket query. Buckets are computed on read from stored
/// facts (latest play record, correlated update events) with caller-supplied
/// thresholds — never persisted, so thresholds can be retuned freely.
///
/// <para>Demo consolidation (<see cref="DemoConsolidation"/>) is derived here
/// for the same reason and in the same pass: a demo whose full game is also
/// owned is dropped from the result, so the library shows one entry per game
/// without the view knowing anything about demos. Nothing is written and
/// nothing is deleted — removing the base game makes the demo reappear on the
/// very next read.</para>
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
                   -- Demo consolidation reads these three; the SQL itself takes
                   -- no view on them. Title matching is a token-level question
                   -- (sequel ordinals, edition markers) that SQLite cannot ask
                   -- and that must be asked with the SAME normaliser the soft
                   -- matcher uses, so it happens in C# below over the rows this
                   -- join already had to read.
                   COALESCE(NULLIF(TRIM(r.name), ''), w.name)  AS Title,
                   w.name_is_provisional               AS NameIsProvisional,
                   w.first_release_year                AS FirstReleaseYear,
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
            JOIN releases           r  ON r.id = o.release_id
            JOIN works              w  ON w.id = r.work_id
            LEFT JOIN latest_play   lp ON lp.ownership_id = o.id
            LEFT JOIN major_update  mu ON mu.release_id = o.release_id
            ORDER BY o.id;
            """;

        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<BucketRow>(new CommandDefinition(sql, new
        {
            thresholds.BouncedCeilingMinutes,
            thresholds.RetiredFloorMinutes,
            thresholds.StaleWindowMonths,
            thresholds.UpdateCorrelationWindowDays,
        }, transaction: lease.Transaction, cancellationToken: ct));

        return Consolidate(rows.AsList());
    }

    /// <summary>
    /// Drops the demo rows the library already holds the full game for, and
    /// tells each surviving base row how many it absorbed.
    ///
    /// <para>Reads the rows the query returned and nothing else: the set of
    /// owned releases IS the set of releases with a row here, so a base game
    /// the user does not own cannot hide anything, and a base game removed
    /// tomorrow stops hiding its demo the moment this runs again. That is the
    /// whole reversibility guarantee, and it costs one pass over a few hundred
    /// rows.</para>
    /// </summary>
    private static IReadOnlyList<OwnershipBucket> Consolidate(List<BucketRow> rows)
    {
        // One entry per RELEASE — a release owned on two stores is one game,
        // and normalising its title twice would only produce the same answer.
        var owned = new Dictionary<long, DemoConsolidationEntry>();
        foreach (var row in rows)
        {
            owned.TryAdd(row.ReleaseId, new DemoConsolidationEntry
            {
                ReleaseId = row.ReleaseId,
                Title = row.Title ?? string.Empty,
                NameIsProvisional = row.NameIsProvisional,
                FirstReleaseYear = row.FirstReleaseYear,
            });
        }

        var consolidated = DemoConsolidation.Consolidate(owned.Values);

        var absorbedByBase = new Dictionary<long, int>();
        foreach (var baseReleaseId in consolidated.Values)
        {
            absorbedByBase[baseReleaseId] =
                absorbedByBase.TryGetValue(baseReleaseId, out var n) ? n + 1 : 1;
        }

        var result = new List<OwnershipBucket>(rows.Count);
        foreach (var row in rows)
        {
            if (consolidated.ContainsKey(row.ReleaseId))
            {
                // Suppressed from the LIBRARY VIEW only. The ownership, its
                // play records, its snapshots and its sessions are untouched
                // and still reachable through every other repository.
                continue;
            }

            result.Add(new OwnershipBucket
            {
                OwnershipId = row.OwnershipId,
                ReleaseId = row.ReleaseId,
                PlaytimeMinutes = row.PlaytimeMinutes,
                LastPlayedAt = row.LastPlayedAt,
                Bucket = row.Bucket,

                // Never a playtime sum — see OwnershipBucket.ConsolidatedDemoCount.
                ConsolidatedDemoCount = absorbedByBase.GetValueOrDefault(row.ReleaseId),
            });
        }

        return result;
    }

    /// <summary>
    /// The query's own row shape: an <see cref="OwnershipBucket"/> plus the
    /// three title columns consolidation needs. They stay off the public
    /// projection because they are inputs to a decision this repository has
    /// already made by the time the caller sees a row.
    /// </summary>
    private sealed record BucketRow
    {
        public long OwnershipId { get; init; }
        public long ReleaseId { get; init; }
        public long PlaytimeMinutes { get; init; }
        public DateTime? LastPlayedAt { get; init; }
        public string Bucket { get; init; } = string.Empty;
        public string? Title { get; init; }
        public bool NameIsProvisional { get; init; }
        public int? FirstReleaseYear { get; init; }
    }
}
