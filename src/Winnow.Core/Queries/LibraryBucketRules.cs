namespace Winnow.Core.Queries;

/// <summary>
/// The §6.1 bucket rules as a pure function of one set of play facts. Carries
/// the CASE that used to live in the bucket query's SQL, verbatim and in the
/// same precedence order. It moved here because the rules now evaluate at two
/// grains: once per ownership row and once per game over the summed figures of
/// its store entries (TASK-70.6). Two evaluations must never be two
/// implementations, so there is exactly one, and both callers are the bucket
/// query.
///
/// <para>Buckets are still derived on read from stored facts and are still
/// never a stored column, which is what §6.1 requires. They are simply
/// derived one step later in the same read.</para>
/// </summary>
public static class LibraryBucketRules
{
    /// <summary>
    /// Bucket precedence, in the order tested.
    ///
    /// 1. never_played — zero minutes AND no last-played date. The one row
    ///    §5.2's "an unplayed game has nothing to be behind on" is about, and
    ///    so the one row allowed to outrank staleness. Zero minutes beside a
    ///    REAL date is not this: it is a source admitting it did not measure
    ///    the session, and unknown minutes are neither never-played nor
    ///    bounced (both claim a KNOWN number of minutes), so such a row falls
    ///    past every playtime test and is bucketed on staleness alone.
    /// 2. retired — at or above the retired floor. Outranks staleness, as it
    ///    always has: high-playtime games are excluded from surfacing even
    ///    when patched.
    /// 3. stale_but_patched — outranks bounced, because Bounced spans
    ///    everything between the refund line and the retired floor and would
    ///    otherwise swallow the rail's flagship bucket whole.
    /// 4. bounced — at or above the refund line, below retired.
    /// 5. active — the residue.
    ///
    /// <paramref name="majorUpdateAt"/> is the correlated build push the query
    /// found for this release (or the latest across a game's releases), already
    /// filtered by the acknowledgement watermark. A NULL
    /// <paramref name="lastPlayedAt"/> beside real playtime is Steam's 86400
    /// sentinel — "unknown, certainly ancient", which is maximally dormant and
    /// therefore stale rather than active.
    /// </summary>
    public static string Classify(
        long playtimeMinutes,
        DateTime? lastPlayedAt,
        DateTime? majorUpdateAt,
        BucketThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);

        if (playtimeMinutes == 0 && lastPlayedAt is null)
        {
            return LibraryBuckets.NeverPlayed;
        }

        if (playtimeMinutes >= thresholds.RetiredFloorMinutes)
        {
            return LibraryBuckets.Retired;
        }

        if (IsStale(lastPlayedAt, majorUpdateAt, thresholds.StaleWindowMonths))
        {
            return LibraryBuckets.StaleButPatched;
        }

        return playtimeMinutes >= thresholds.BouncedFloorMinutes
            ? LibraryBuckets.Bounced
            : LibraryBuckets.Active;
    }

    /// <summary>
    /// The staleness test, kept separate because the unread badge and the
    /// update rows on the details modal ask the same question of one update
    /// at a time.
    /// </summary>
    public static bool IsStale(DateTime? lastPlayedAt, DateTime? majorUpdateAt, int staleWindowMonths)
    {
        if (majorUpdateAt is not { } update)
        {
            return false;
        }

        if (lastPlayedAt is not { } played)
        {
            return true;
        }

        return Truncate(update) > Truncate(AddMonths(played, staleWindowMonths));
    }

    /// <summary>
    /// SQLite's <c>'+N months'</c> modifier, not .NET's
    /// <see cref="DateTime.AddMonths"/>. SQLite adds to the month field and
    /// then normalises the overflow, so 2024-03-31 plus six months is
    /// 2024-10-01; .NET clamps to the last valid day and answers 2024-09-30.
    /// The rule this reproduces is the one the query has always applied, so
    /// lifting the CASE into C# does not move a single row's bucket.
    /// </summary>
    public static DateTime AddMonths(DateTime value, int months)
    {
        var zeroBased = ((value.Year * 12) + value.Month - 1) + months;
        var year = Math.DivRem(zeroBased, 12, out var month);
        if (month < 0)
        {
            month += 12;
            year--;
        }

        // The day is carried across UNCHANGED and then allowed to overflow into
        // the following month, which is exactly what SQLite's own normalisation
        // does with an out-of-range day field.
        var daysInMonth = DateTime.DaysInMonth(year, month + 1);
        var day = value.Day;
        var shifted = new DateTime(year, month + 1, Math.Min(day, daysInMonth), 0, 0, 0, value.Kind)
            .Add(value.TimeOfDay);

        return day > daysInMonth ? shifted.AddDays(day - daysInMonth) : shifted;
    }

    /// <summary>
    /// SQLite's <c>datetime()</c> renders to whole seconds, and the comparison
    /// the CASE made was between two such strings. Truncating here keeps a
    /// sub-second fraction on a stored timestamp from deciding a bucket that
    /// SQLite would have called the other way.
    /// </summary>
    private static DateTime Truncate(DateTime value)
        => new(value.Ticks - (value.Ticks % TimeSpan.TicksPerSecond), value.Kind);
}
