using Dapper;
using Hoard.Data;

namespace Hoard.Enrich.Updates.Storage;

/// <summary>One Steam game worth polling: a release, its appid, and why it qualified.</summary>
/// <param name="ReleaseId">The release update events are written against.</param>
/// <param name="AppId">Its Steam appid, from <c>external_ids</c>.</param>
/// <param name="PlaytimeMinutes">Highest playtime across the release's ownerships.</param>
/// <param name="LastPlayedAt">Most recent last-played across the release's ownerships, if known.</param>
public sealed record PollCandidate(long ReleaseId, string AppId, long PlaytimeMinutes, DateTime? LastPlayedAt);

/// <summary>
/// The eligible set — the "eliminate" of the spike's eliminate/cascade/stagger,
/// and the single biggest saving in the whole design.
/// </summary>
public interface IPollCandidateSource
{
    /// <summary>Every Steam release whose badge could ever be shown, ordered by release id.</summary>
    Task<IReadOnlyList<PollCandidate>> GetEligibleAsync(
        long retiredFloorMinutes, CancellationToken ct = default);
}

/// <summary><see cref="IPollCandidateSource"/> over the §6 schema.</summary>
public sealed class SqlitePollCandidateSource : IPollCandidateSource
{
    private readonly ISqliteConnectionFactory _factory;

    public SqlitePollCandidateSource(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<PollCandidate>> GetEligibleAsync(
        long retiredFloorMinutes, CancellationToken ct = default)
    {
        // Eligibility mirrors the bucket query's own exclusions, because a game
        // this query returns that the bucket query can never bucket as
        // `stale_but_patched` is a request spent to learn something unshowable.
        //
        //  - Never played is excluded. design-system.md §5.2: "Never on
        //    never-opened games; an unplayed game has nothing to be behind on."
        //    The test is the bucket query's own `never_touched` rule, negated —
        //    zero minutes AND no last-played date — NOT `playtime > 0`. A game
        //    with a real last-played date and zero recorded minutes was
        //    demonstrably launched on a machine whose userdata was unreadable;
        //    its minutes are unknown, not zero, and dropping it would silently
        //    exclude exactly the dormant titles this feature exists for.
        //
        //  - Retired is excluded. §6.1 gives `retired` precedence over
        //    `stale_but_patched`, so a 200-hour game cannot show the badge no
        //    matter what lands in update_events.
        //
        // `dead` has no column in the §6 schema yet, so there is nothing to
        // filter on; when it arrives it belongs in this WHERE clause.
        const string sql = """
            WITH latest_play AS (
                -- Newest play record per ownership, tie-broken by id, matching
                -- LibraryQueryRepository exactly. A bare MAX() would let SQLite
                -- pick an arbitrary row of a same-second tie and the two queries
                -- would disagree about the same library.
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
            )
            -- CAST because an aggregate expression has no column affinity for
            -- Microsoft.Data.Sqlite to report, so MAX(...) arrives as a BLOB and
            -- Dapper cannot bind it to the record's long / DateTime? parameters.
            SELECT e.release_id                                          AS ReleaseId,
                   e.provider_id                                         AS AppId,
                   CAST(MAX(COALESCE(lp.playtime_minutes, 0)) AS INTEGER) AS PlaytimeMinutes,
                   CAST(MAX(lp.last_played_at) AS TEXT)                   AS LastPlayedAt
            FROM external_ids e
            JOIN ownerships o        ON o.release_id = e.release_id
            LEFT JOIN latest_play lp ON lp.ownership_id = o.id
            WHERE e.provider = 'steam'
            -- One row per release, not per ownership: the same game owned on
            -- two Steam accounts shares one appid and one feed, so polling it
            -- twice would spend two requests for one answer.
            GROUP BY e.release_id, e.provider_id
            HAVING (MAX(COALESCE(lp.playtime_minutes, 0)) > 0
                    OR MAX(lp.last_played_at) IS NOT NULL)
               AND MAX(COALESCE(lp.playtime_minutes, 0)) < @retiredFloorMinutes
            ORDER BY e.release_id;
            """;

        using var lease = _factory.Lease();

        // Read into a settable class, not straight into the positional record.
        // Aggregate expressions carry no column affinity, so Microsoft.Data
        // .Sqlite reports MAX(...) as BLOB and Dapper's constructor matcher
        // rejects the record's (long, DateTime?) signature outright — even for
        // an empty result set, since it resolves the deserializer from the
        // reader's schema before the first row.
        var rows = await lease.Connection.QueryAsync<Row>(new CommandDefinition(
            sql,
            new { retiredFloorMinutes },
            transaction: lease.Transaction,
            cancellationToken: ct));

        return rows
            .Select(r => new PollCandidate(r.ReleaseId, r.AppId ?? string.Empty, r.PlaytimeMinutes, r.LastPlayedAt))
            .ToList();
    }

    private sealed class Row
    {
        public long ReleaseId { get; init; }

        public string? AppId { get; init; }

        public long PlaytimeMinutes { get; init; }

        public DateTime? LastPlayedAt { get; init; }
    }
}
