using Winnow.Enrich.SteamWeb.Model;
using Xunit;

namespace Winnow.Tests.SteamWeb;

/// <summary>
/// M5's arithmetic, in isolation. Year in Review reports seconds played DURING
/// a month; <c>playtime_snapshots</c> holds a cumulative counter. The conversion
/// is the whole feature, so it is pinned against literal numbers with no
/// database, no clock and no HTTP anywhere near it.
/// </summary>
public class PlaytimeSeriesReconstructionTests
{
    private static SteamMonthlyPlaytime Month(int year, int month, long seconds)
        => new(year, month, seconds, Sessions: 0);

    /// <summary>
    /// The worked case. Four covered months against a present total of 817
    /// minutes: the newest point must BE 817 (the series has to converge on
    /// what the ordinary sync will write today), and every earlier point is
    /// that figure less the months in between.
    /// </summary>
    [Fact]
    public void The_series_is_reconstructed_exactly_and_ends_on_the_anchor()
    {
        var series = PlaytimeSeriesReconstructor.Reconstruct(
            anchorMinutes: 817,
            [
                Month(2024, 1, 6000),   // 100 minutes
                Month(2024, 2, 12000),  // 200
                Month(2024, 5, 3000),   // 50
                Month(2025, 3, 9000),   // 150
            ]);

        Assert.False(series.Clamped);
        Assert.Equal(
            [
                (new DateTime(2023, 12, 31, 23, 59, 59, DateTimeKind.Utc), 317L),
                (new DateTime(2024, 1, 31, 23, 59, 59, DateTimeKind.Utc), 417L),
                (new DateTime(2024, 2, 29, 23, 59, 59, DateTimeKind.Utc), 617L),
                (new DateTime(2024, 5, 31, 23, 59, 59, DateTimeKind.Utc), 667L),
                (new DateTime(2025, 3, 31, 23, 59, 59, DateTimeKind.Utc), 817L),
            ],
            series.Points.Select(p => (p.ObservedAt, p.PlaytimeMinutes)));

        // 317 minutes the covered months do not explain. This account played
        // this game before Steam Replay existed, and the series says so rather
        // than starting it at zero.
        Assert.Equal(317, series.RemainderMinutes);
    }

    /// <summary>
    /// Order in, order out. The importer merges months from four separate
    /// yearly responses and nothing guarantees they arrive sorted.
    /// </summary>
    [Fact]
    public void Months_arriving_out_of_order_produce_the_same_series()
    {
        var months = new[]
        {
            Month(2025, 3, 9000), Month(2024, 2, 12000), Month(2024, 5, 3000), Month(2024, 1, 6000),
        };

        Assert.Equal(
            PlaytimeSeriesReconstructor.Reconstruct(817, months.OrderBy(m => m.Ordinal)).Points,
            PlaytimeSeriesReconstructor.Reconstruct(817, months).Points);
    }

    /// <summary>
    /// Every point is a cumulative reading, so the series must never go
    /// backwards, the property the whole snapshot table is built on.
    /// </summary>
    [Fact]
    public void The_series_never_decreases()
    {
        var series = PlaytimeSeriesReconstructor.Reconstruct(
            817, [Month(2024, 1, 6000), Month(2024, 2, 12000), Month(2024, 5, 3000), Month(2025, 3, 9000)]);

        var minutes = series.Points.Select(p => p.PlaytimeMinutes).ToList();
        Assert.Equal(minutes, minutes.Order());
        Assert.Equal(series.Points.Select(p => p.ObservedAt), series.Points.Select(p => p.ObservedAt).Order());
    }

    /// <summary>
    /// A month with no play is still an observation: the point holds flat, which
    /// is what dormancy is measured from. It is not dropped and it does not
    /// break the walk.
    /// </summary>
    [Fact]
    public void A_month_with_no_play_holds_the_series_flat()
    {
        var series = PlaytimeSeriesReconstructor.Reconstruct(
            300, [Month(2024, 1, 6000), Month(2024, 2, 0), Month(2024, 3, 6000)]);

        Assert.Equal([100L, 200L, 200L, 300L], series.Points.Select(p => p.PlaytimeMinutes));
    }

    /// <summary>
    /// The pre-2022 clamp. Year in Review claims 500 minutes of play against a
    /// cumulative counter holding 120: the two figures come from different Valve
    /// systems and can genuinely disagree. The walk stops at the point it can
    /// still support rather than emitting a negative one or inventing a zero,
    /// and it says so.
    /// </summary>
    [Fact]
    public void A_backward_walk_that_would_cross_zero_clamps_and_stops()
    {
        var series = PlaytimeSeriesReconstructor.Reconstruct(
            anchorMinutes: 120,
            [
                Month(2024, 1, 18000),  // 300 minutes
                Month(2024, 2, 6000),   // 100
                Month(2024, 3, 6000),   // 100
            ]);

        Assert.True(series.Clamped);

        // March and February survive (120 → 20). January would have taken the
        // running total to -280, so it and the pre-coverage floor are dropped
        // rather than guessed at.
        Assert.Equal(
            [
                (new DateTime(2024, 2, 29, 23, 59, 59, DateTimeKind.Utc), 20L),
                (new DateTime(2024, 3, 31, 23, 59, 59, DateTimeKind.Utc), 120L),
            ],
            series.Points.Select(p => (p.ObservedAt, p.PlaytimeMinutes)));

        // The remainder is unknown, not zero: claiming zero would assert the
        // account had never played the game before 2024, which is exactly what
        // the disagreement makes unknowable.
        Assert.Null(series.RemainderMinutes);
        Assert.All(series.Points, p => Assert.True(p.PlaytimeMinutes >= 0));
    }

    /// <summary>The degenerate clamp: nothing survives except the anchor's own point.</summary>
    [Fact]
    public void A_zero_anchor_against_claimed_play_keeps_only_the_present()
    {
        var series = PlaytimeSeriesReconstructor.Reconstruct(0, [Month(2024, 1, 6000)]);

        Assert.True(series.Clamped);
        Assert.Equal([0L], series.Points.Select(p => p.PlaytimeMinutes));
    }

    /// <summary>
    /// No months is the ordinary case for a game untouched during the covered
    /// years. There is nothing the anchor does not already say, and the ordinary
    /// sync writes the anchor, so the reconstruction contributes nothing rather
    /// than inventing a point.
    /// </summary>
    [Fact]
    public void No_months_reconstructs_nothing()
    {
        Assert.Empty(PlaytimeSeriesReconstructor.Reconstruct(817, []).Points);
        Assert.Empty(PlaytimeSeriesReconstructor.Reconstruct(0, []).Points);
    }

    /// <summary>
    /// Arithmetic runs in seconds and only the emitted points are floored to
    /// minutes, so the per-month truncations cannot accumulate. Three months of
    /// 100 seconds each against a 100-minute anchor: the naive
    /// round-each-month-to-minutes version would subtract nothing at all and
    /// report a flat series.
    /// </summary>
    [Fact]
    public void Sub_minute_months_do_not_round_away()
    {
        var series = PlaytimeSeriesReconstructor.Reconstruct(
            100, [Month(2024, 1, 100), Month(2024, 2, 100), Month(2024, 3, 100)]);

        Assert.Equal([95L, 96L, 98L, 100L], series.Points.Select(p => p.PlaytimeMinutes));
        Assert.Equal(95, series.RemainderMinutes);
    }

    /// <summary>
    /// Two responses describing the same month are one month. Each yearly fetch
    /// can overlap the next, and double-counting would deflate every earlier
    /// point by a whole month of play.
    /// </summary>
    [Fact]
    public void A_month_reported_twice_is_counted_once()
    {
        var series = PlaytimeSeriesReconstructor.Reconstruct(
            200, [Month(2024, 1, 6000), Month(2024, 1, 6000)]);

        Assert.Equal([100L, 200L], series.Points.Select(p => p.PlaytimeMinutes));
    }

    /// <summary>
    /// A gap between covered months is not a gap in the series: nothing was
    /// played, so the cumulative total at the end of the earlier month is the
    /// total at the start of the later one.
    /// </summary>
    [Fact]
    public void A_gap_between_covered_months_carries_the_total_across_it()
    {
        var series = PlaytimeSeriesReconstructor.Reconstruct(
            300, [Month(2022, 3, 6000), Month(2025, 11, 6000)]);

        Assert.Equal(
            [
                (new DateTime(2022, 2, 28, 23, 59, 59, DateTimeKind.Utc), 100L),
                (new DateTime(2022, 3, 31, 23, 59, 59, DateTimeKind.Utc), 200L),
                (new DateTime(2025, 11, 30, 23, 59, 59, DateTimeKind.Utc), 300L),
            ],
            series.Points.Select(p => (p.ObservedAt, p.PlaytimeMinutes)));
    }

    /// <summary>
    /// December's floor point falls in the previous year, and the month-end
    /// stamp is the last whole second. <c>observed_at</c> is stored to whole
    /// seconds, so anything finer would not survive the round trip.
    /// </summary>
    [Fact]
    public void Month_ends_are_the_last_whole_second_of_the_month()
    {
        Assert.Equal(
            new DateTime(2024, 12, 31, 23, 59, 59, DateTimeKind.Utc), SteamMonthlyPlaytime.MonthEnd(2024, 12));
        Assert.Equal(
            new DateTime(2023, 12, 31, 23, 59, 59, DateTimeKind.Utc),
            Month(2024, 1, 0).PrecedingMonthEndUtc);
        Assert.Equal(DateTimeKind.Utc, SteamMonthlyPlaytime.MonthEnd(2024, 12).Kind);
    }
}
