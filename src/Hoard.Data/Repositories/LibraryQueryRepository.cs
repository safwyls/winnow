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
///
/// <para>The non-game filter (<see cref="NonGameEntries"/>) is derived here too,
/// and last: with <see cref="BucketThresholds.ShowNonGameEntries"/> off, the
/// tools, soundtracks and videos Valve typed as such never reach the caller. It
/// runs on the same rows the buckets and the counts are read from, which is the
/// whole point — the rail cannot report a total the grid does not show.</para>
/// </summary>
public sealed class LibraryQueryRepository : ILibraryQueryRepository
{
    private readonly ISqliteConnectionFactory _factory;

    public LibraryQueryRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<OwnershipBucket>> GetOwnershipBucketsAsync(
        BucketThresholds thresholds, CancellationToken ct = default)
    {
        // Bucket precedence (§6.1), in the order the CASE below tests:
        //
        //   1. never-opened  — zero minutes AND no last-played date
        //   2. retired       — at or above the retired floor
        //   3. stale_but_patched
        //   4. never_played  — below the refund line
        //   5. bounced       — refund line up to the retired floor
        //   6. active        — the residue
        //
        // Two of those orderings are load-bearing and neither is obvious.
        //
        // Retired outranks stale, as it always has: high-playtime games are
        // excluded from surfacing even when patched.
        //
        // Stale outranks never_played and bounced, which is NEW and is forced by
        // the refund-line boundary. Bounced now spans everything between the
        // refund line and the retired floor, so if it were still tested first it
        // would swallow `stale_but_patched` whole and the rail's flagship bucket
        // would be permanently empty. Testing staleness first is also what makes
        // §5.2 true: the badge is bucket membership, and a game with forty
        // minutes on it CAN be behind on a patch. Only case 1 — the game that
        // was never opened — has nothing to be behind on, which is why it, and
        // only it, is tested above staleness.
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
                   -- Valve's own classification of the appid (migration 0006),
                   -- verbatim. NULL is "nobody has read it", which is common:
                   -- some appids are unreadable without a Web API key.
                   w.steam_app_type                    AS SteamAppType,
                   -- Epic's own categories[].path list (migration 0009),
                   -- comma-joined and verbatim. Same contract as the column
                   -- above: NULL is "nobody has read it", which is the state of
                   -- every Epic work named from catcache.bin.
                   w.epic_categories                   AS EpicCategories,
                   CASE
                       -- NEVER OPENED: no evidence of play at all — no minutes
                       -- AND no last-played date. This is the one row §5.2's
                       -- "an unplayed game has nothing to be behind on" is about,
                       -- so it is the one row allowed to outrank staleness.
                       --
                       -- Zero minutes beside a REAL last-played date is not this.
                       -- It is a source admitting it did not measure the session:
                       -- an appmanifest LastPlayed on a machine whose userdata/ is
                       -- unreadable, where the game was demonstrably launched and
                       -- the minutes are unknown, not zero. Unknown minutes are
                       -- neither "never played" nor "bounced" (both of which claim
                       -- a KNOWN number of minutes), so such a row falls past
                       -- every playtime test below and is bucketed on staleness
                       -- alone.
                       WHEN COALESCE(lp.playtime_minutes, 0) = 0
                            AND lp.last_played_at IS NULL
                           THEN 'never_played'
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
                       -- Below the refund line the purchase was still reversible,
                       -- so however many minutes are on the clock, the game was
                       -- never really played. `> 0` only to leave the unknown-
                       -- minutes row above to the ELSE.
                       WHEN lp.playtime_minutes > 0
                            AND lp.playtime_minutes < @BouncedFloorMinutes
                           THEN 'never_played'
                       -- Refund line up to the retired floor, which the retired
                       -- test above has already carved off.
                       WHEN lp.playtime_minutes >= @BouncedFloorMinutes
                           THEN 'bounced'
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
            thresholds.BouncedFloorMinutes,
            thresholds.RetiredFloorMinutes,
            thresholds.StaleWindowMonths,
            thresholds.UpdateCorrelationWindowDays,
        }, transaction: lease.Transaction, cancellationToken: ct));

        return Consolidate(rows.AsList(), thresholds.ShowNonGameEntries);
    }

    public async Task<IReadOnlyList<FacetTarget>> GetFacetTargetsAsync(CancellationToken ct = default)
    {
        // One row per release, carrying the id each descriptor source is keyed
        // by. The Steam appid is a correlated subquery rather than a join
        // because external_ids can in principle hold more than one row per
        // release (gog, epic, igdb alongside steam) and a join would multiply
        // the result; LIMIT 1 keeps this at exactly one row per release.
        //
        // No filter on "already has facets": see the interface's own note on why
        // the backfill re-reads everything and lets the cache and the
        // read-before-write keep it cheap.
        const string sql = """
            SELECT r.work_id  AS WorkId,
                   r.id       AS ReleaseId,
                   w.igdb_id  AS IgdbId,
                   (SELECT e.provider_id
                    FROM external_ids e
                    WHERE e.release_id = r.id AND e.provider = 'steam'
                    LIMIT 1) AS SteamAppId
            FROM releases r
            JOIN works w ON w.id = r.work_id
            ORDER BY r.id;
            """;

        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<FacetTarget>(new CommandDefinition(
            sql, transaction: lease.Transaction, cancellationToken: ct));
        return rows.AsList();
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
    /// <remarks>
    /// The non-game filter is applied after all of that, and only when the
    /// caller asked for it. <b>The order is load-bearing.</b> Consolidation is
    /// fed every owned row regardless of the setting, so the demo/base map it
    /// returns is identical whether non-game entries are shown or hidden — the
    /// filter can move a tool off the screen but can never change which demo is
    /// folded into which game. (The one corner: a hidden non-game row that had
    /// absorbed a variant takes that variant's suppression with it. That needs
    /// an owned entry Valve typed <c>Tool</c> whose title is exactly an owned
    /// demo's base title, which the measured library contains nothing like, and
    /// un-hiding is one toggle away.)
    /// </remarks>
    private static IReadOnlyList<OwnershipBucket> Consolidate(
        List<BucketRow> rows, bool showNonGameEntries)
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
                SteamAppType = row.SteamAppType,
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

            if (!showNonGameEntries && NonGameEntries.IsNonGame(row.SteamAppType, row.EpicCategories))
            {
                // A tool, soundtrack, video or piece of hardware on Steam; an
                // Unreal Engine build, a marketplace asset pack or a cosmetic
                // entitlement on Epic. Either way something the user genuinely
                // owns and has not asked to see. Hidden from the LIBRARY VIEW
                // only, exactly like a consolidated demo: nothing is written and
                // nothing is deleted, so the next read with the setting on
                // returns it untouched — including its playtime, which for two
                // of the Epic rows in the author's library is not zero.
                //
                // ONE notion of "not a game", two sources of evidence: Valve
                // publishes a type string per appid and Epic a category list per
                // catalog item, neither expressible in the other's vocabulary,
                // and NonGameEntries.IsNonGame(steam, epic) is the single place
                // that reads both. The Epic half defers to
                // EpicGameFilter — the same predicate the local Epic scan
                // applies before a candidate is ever emitted — so the two halves
                // of Epic ingest cannot drift apart.
                //
                // A NULL or unrecognised value never reaches here on either
                // side: most of the library has no stored classification at all,
                // and "not known" is not "not a game".
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
        public string? SteamAppType { get; init; }
        public string? EpicCategories { get; init; }
    }
}
