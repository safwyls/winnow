namespace Winnow.Enrich.SteamWeb.Model;

/// <summary>One reconstructed point of the cumulative series.</summary>
/// <param name="ObservedAt">Month-end UTC, to whole seconds.</param>
/// <param name="PlaytimeMinutes">Cumulative minutes as of that instant. Never negative.</param>
public readonly record struct ReconstructedPoint(DateTime ObservedAt, long PlaytimeMinutes);

/// <summary>
/// The reconstructed cumulative series for one appid, plus what the
/// reconstruction had to give up to produce it.
/// </summary>
/// <param name="Points">Oldest first. Empty when there was nothing to reconstruct.</param>
/// <param name="RemainderMinutes">
/// Play the covered months do not explain: everything before the first covered
/// month, which for a 2022-onward fetch is the account's pre-2022 history. Null
/// when the walk was clamped and the remainder is therefore unknown.
/// </param>
/// <param name="Clamped">
/// True when subtracting a month's delta would have driven the running total
/// below zero. The months claimed more play than the account's own cumulative
/// counter holds, so the walk stopped rather than emit a negative point or
/// invent a zero.
/// </param>
public readonly record struct PlaytimeSeriesReconstruction(
    IReadOnlyList<ReconstructedPoint> Points,
    long? RemainderMinutes,
    bool Clamped)
{
    /// <summary>Nothing to reconstruct: no months, or no anchor.</summary>
    public static readonly PlaytimeSeriesReconstruction Empty = new([], null, false);
}

/// <summary>
/// Turns Year in Review's per-period monthly figures into the cumulative
/// series <c>playtime_snapshots</c> holds.
///
/// <para><b>Why the walk runs backwards.</b> A snapshot is a cumulative
/// counter; a Year in Review month is seconds played DURING that month.
/// Converting needs one known cumulative value, and only one is available:
/// <c>playtime_forever</c> from <c>ClientGetLastPlayedTimes</c>, the
/// account's present truth. The walk starts there and subtracts monthly
/// deltas going back in time, so the series converges exactly on the
/// number the ordinary sync writes today. A forward walk would assume a
/// zero baseline, correct for an account with no play before coverage and
/// silently wrong for every other account by the amount of pre-2022
/// history.</para>
///
/// <para><b>Arithmetic is in seconds.</b> The anchor arrives in minutes,
/// the months in seconds. Rounding each month to minutes first lets
/// per-month truncations accumulate, so the anchor is widened to seconds,
/// the walk runs there, and each emitted point is floored to minutes on
/// the way out. Flooring a decreasing sequence cannot increase it, so
/// monotonicity survives.</para>
///
/// <para>Pure, and deliberately so: no clock, no repository, no HTTP.
/// The whole contract is testable against literal numbers.</para>
/// </summary>
public static class PlaytimeSeriesReconstructor
{
    /// <summary>
    /// Reconstructs one appid's cumulative series.
    /// </summary>
    /// <param name="anchorMinutes">
    /// The account's cumulative total for this appid right now, from
    /// <c>ClientGetLastPlayedTimes</c>. Negative input is treated as zero.
    /// </param>
    /// <param name="months">
    /// Every covered month for this appid, across every year fetched, in any
    /// order. Duplicated (year, month) pairs collapse to the largest figure;
    /// two responses describing one month are one month.
    /// </param>
    public static PlaytimeSeriesReconstruction Reconstruct(
        long anchorMinutes, IEnumerable<SteamMonthlyPlaytime> months)
    {
        var ordered = Normalize(months);
        if (ordered.Count == 0)
        {
            // No monthly breakdown is the ordinary case for a game the user did
            // not touch during the covered years. There is nothing to say about
            // it that the anchor does not already say, and the ordinary sync
            // writes the anchor.
            return PlaytimeSeriesReconstruction.Empty;
        }

        var running = Math.Max(0, anchorMinutes) * 60L;
        var points = new List<ReconstructedPoint>(ordered.Count + 1);
        var clamped = false;

        for (var i = ordered.Count - 1; i >= 0; i--)
        {
            var month = ordered[i];

            // Cumulative total as of this month's last second. For the newest
            // covered month this is the anchor itself; play since then is
            // folded in rather than attributed to a month Steam has not
            // reported yet. The distortion is at most one month of play,
            // the price of converging on the present; the alternative
            // leaves the series at an unknown offset.
            points.Add(new ReconstructedPoint(month.MonthEndUtc, running / 60));

            var previous = running - month.PlaytimeSeconds;
            if (previous < 0)
            {
                // The months claim more play than playtime_forever holds. Both
                // figures come from Valve and can genuinely disagree:
                // family-shared sessions, a reset counter, licences that
                // changed. The series must never go negative and must never
                // invent a zero it cannot support, so the walk stops here
                // and every earlier month for this appid is dropped.
                clamped = true;
                break;
            }

            running = previous;
        }

        if (!clamped)
        {
            // The floor: everything the covered months do not explain, stamped
            // at the last second before the first covered month. On a
            // 2022-onward fetch this is the account's pre-2022 total, and it is
            // what stops the series from implying a game went from zero to its
            // first covered month in one step.
            points.Add(new ReconstructedPoint(ordered[0].PrecedingMonthEndUtc, running / 60));
        }

        points.Reverse();
        return new PlaytimeSeriesReconstruction(points, clamped ? null : running / 60, clamped);
    }

    /// <summary>
    /// Deduplicates by (year, month), drops unusable entries, and sorts oldest
    /// first. A month with zero seconds is kept: "played nothing in March" is an
    /// observation, and the flat point it produces is what dormancy is measured
    /// from.
    /// </summary>
    private static List<SteamMonthlyPlaytime> Normalize(IEnumerable<SteamMonthlyPlaytime> months)
        => months
            .Where(static m => m is { Month: >= 1 and <= 12, Year: > 0, PlaytimeSeconds: >= 0 })
            .GroupBy(static m => (m.Year, m.Month))
            .Select(static g => g.MaxBy(static m => m.PlaytimeSeconds)!)
            .OrderBy(static m => m.Ordinal)
            .ToList();
}
