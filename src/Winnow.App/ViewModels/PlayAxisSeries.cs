using Winnow.Core.Domain;

namespace Winnow.App.ViewModels;

/// <summary>
/// One bar on the lifetime axis. Spans the true time between two consecutive
/// readings and carries the play gained between them, so a stretch the backfill
/// did not cover draws as one wide bar with the whole stretch's hours rather
/// than being compressed into an ordinal sequence that would misplace it in time.
/// </summary>
/// <param name="Start">Left edge, 0-1 on the axis.</param>
/// <param name="End">Right edge, 0-1 on the axis.</param>
/// <param name="Hours">Play gained in this stretch, in hours.</param>
public readonly record struct PlayAxisBar(double Start, double End, double Hours);

/// <summary>
/// The computation behind Band 2's lifetime axis: this game's release to today,
/// in two zones.
///
/// <para>The left zone is play whose AMOUNT Winnow knows and whose SHAPE it does
/// not. <see cref="PlaytimeSeriesReconstructor"/> stamps everything the covered
/// months do not explain as one value at the last second before the first covered
/// month. Its cumulative minutes become <see cref="UnmeasuredMinutes"/> and
/// <see cref="UnmeasuredFraction"/> marks where that boundary falls on the axis.
/// The control draws that zone as a flat band and never as bars or a slope.</para>
///
/// <para>The right zone is one bar per stretch, from the month-end readings that
/// are the only genuinely per-month data in the table.</para>
///
/// <para>Sessions are deliberately not an input. They exist only from Winnow's
/// own install and only for processes it watched, so mixing them in would make a
/// game heavily played for four years before that install look dormant for those
/// years.</para>
///
/// <para>Fewer than two month-end readings means no measured month and therefore
/// no axis at all: <see cref="None"/>, and Band 2 falls back to the shipped gap
/// rail. Drawing a single flat line instead would be the misleading drawing
/// TASK-115 acceptance criterion 3 rules out. No release year means no axis
/// either: <c>works.first_release_year</c> is a year, not a date, so the axis
/// starts at 1 January of that year and its label is the year.</para>
/// </summary>
public sealed record PlayAxisSeries
{
    /// <summary>
    /// Past 14 a rail is a smear, and the list below stays the exhaustive record.
    /// The same cap <see cref="GapRail"/> uses, for the same reason (§10.2).
    /// </summary>
    public const int MaxMarks = 14;

    /// <summary>No drawable axis. Band 2 falls back to the gap rail.</summary>
    public static readonly PlayAxisSeries None = new();

    /// <summary>True when the series has enough data to draw.</summary>
    public bool CanDraw { get; init; }

    /// <summary>Where the coverage boundary falls on the 0-1 axis. Everything left of this is unmeasured.</summary>
    public double UnmeasuredFraction { get; init; }

    /// <summary>
    /// Cumulative minutes at the floor point — an amount whose shape was never
    /// observed. The control draws this zone flat and never as bars or a slope.
    /// </summary>
    public long UnmeasuredMinutes { get; init; }

    /// <summary>One bar per stretch between consecutive month-end readings.</summary>
    public IReadOnlyList<PlayAxisBar> Bars { get; init; } = [];

    /// <summary>The last session's position on the axis, 0-1. Null when there is no last-played date.</summary>
    public double? StopFraction { get; init; }

    /// <summary>Unread update positions on the axis, 0-1, capped at <see cref="MaxMarks"/>.</summary>
    public IReadOnlyList<double> Marks { get; init; } = [];

    /// <summary>Left edge of the axis, UTC. 1 January of the release year, or earlier if data predates it.</summary>
    public DateTime AxisStartUtc { get; init; }

    /// <summary>The first month-end reading, UTC. Left edge of the measured zone.</summary>
    public DateTime CoverageStartUtc { get; init; }

    /// <summary>True when unmeasured play exists before the coverage boundary.</summary>
    public bool HasUnmeasured => UnmeasuredMinutes > 0;

    /// <summary>
    /// Turns stored playtime snapshots into a release-to-today axis in two zones.
    /// A series that predates the stated release year extends the axis back to
    /// that month rather than clipping the reading off the left edge.
    /// </summary>
    public static PlayAxisSeries Build(
        IReadOnlyList<PlaytimeSnapshot> snapshots,
        int? releaseYear,
        DateTime? lastPlayedUtc,
        IReadOnlyList<DateTime> markTimesUtc,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(markTimesUtc);

        if (releaseYear is not > 0 or > 9999)
        {
            return None;
        }

        var points = MonthEndPoints(snapshots);
        if (points.Count < 2)
        {
            return None;
        }

        var now = UpdateEventViewModel.AsUtc(nowUtc);
        var coverageStart = points[0].ObservedAt;

        var axisStart = new DateTime(releaseYear.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        if (coverageStart < axisStart)
        {
            axisStart = new DateTime(coverageStart.Year, coverageStart.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        if (lastPlayedUtc is { } played && UpdateEventViewModel.AsUtc(played) < axisStart)
        {
            var stopped = UpdateEventViewModel.AsUtc(played);
            axisStart = new DateTime(stopped.Year, stopped.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        var span = (now - axisStart).TotalSeconds;
        if (span <= 0)
        {
            return None;
        }

        double Fraction(DateTime at)
            => Math.Clamp((UpdateEventViewModel.AsUtc(at) - axisStart).TotalSeconds / span, 0.0, 1.0);

        var bars = new List<PlayAxisBar>(points.Count - 1);
        for (var i = 1; i < points.Count; i++)
        {
            var gained = Math.Max(0, points[i].PlaytimeMinutes - points[i - 1].PlaytimeMinutes);
            bars.Add(new PlayAxisBar(
                Fraction(points[i - 1].ObservedAt),
                Fraction(points[i].ObservedAt),
                gained / 60.0));
        }

        var marks = markTimesUtc
            .Select(UpdateEventViewModel.AsUtc)
            .OrderBy(at => at)
            .Select(Fraction)
            .Take(MaxMarks)
            .ToArray();

        return new PlayAxisSeries
        {
            CanDraw = true,
            UnmeasuredFraction = Fraction(coverageStart),
            UnmeasuredMinutes = points[0].PlaytimeMinutes,
            Bars = bars,
            StopFraction = lastPlayedUtc is { } stop ? Fraction(stop) : null,
            Marks = marks,
            AxisStartUtc = axisStart,
            CoverageStartUtc = coverageStart,
        };
    }

    /// <summary>
    /// Keeps only snapshots stamped at the last whole second of a month. Those
    /// are the ones <c>SteamPlaytimeBackfillService</c> writes from Steam Replay,
    /// reconstructed by <c>PlaytimeSeriesReconstructor</c>, and they are the only
    /// genuinely per-month readings in the table. A live snapshot is written only
    /// while Winnow is running, so a user who closes it for three weeks gets
    /// three weeks of accumulated play stamped on one instant; differencing those
    /// would draw a spike on the day the app reopened rather than on the days the
    /// play happened.
    /// </summary>
    private static List<PlaytimeSnapshot> MonthEndPoints(IReadOnlyList<PlaytimeSnapshot> snapshots)
    {
        var seen = new HashSet<DateTime>();
        var points = new List<PlaytimeSnapshot>();

        foreach (var snapshot in snapshots)
        {
            var observed = UpdateEventViewModel.AsUtc(snapshot.ObservedAt);
            if (!IsMonthEnd(observed) || !seen.Add(observed))
            {
                continue;
            }

            points.Add(snapshot with { ObservedAt = observed });
        }

        points.Sort(static (a, b) => a.ObservedAt.CompareTo(b.ObservedAt));
        return points;
    }

    /// <summary>
    /// Restates <c>SteamMonthlyPlaytime.MonthEnd</c>'s definition locally. A
    /// view model may not name an enrichment type (game-library-design.md §5.1,
    /// enforced by <c>ArchitectureBoundaryTests</c>), so the check is duplicated
    /// rather than shared.
    /// </summary>
    private static bool IsMonthEnd(DateTime observedUtc)
        => observedUtc
           == new DateTime(observedUtc.Year, observedUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc)
               .AddMonths(1)
               .AddSeconds(-1);
}
