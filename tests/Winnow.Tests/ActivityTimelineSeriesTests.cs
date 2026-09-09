using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Xunit;

namespace Winnow.Tests;

public sealed class ActivityTimelineSeriesTests
{
    private static readonly DateTime Now = Utc(2026, 9, 9);

    [Fact]
    public void Consecutive_months_preserve_known_zero_and_leave_missing_month_unknown()
    {
        var result = Build([Snapshot(1, 60), Snapshot(2, 60), Snapshot(4, 180)]);
        var bar = Assert.Single(result.Bars);
        Assert.Equal(Utc(2026, 2, 1), bar.StartUtc);
        Assert.Equal(0, bar.Hours);
        Assert.Single(result.Coverage);
        Assert.True(result.HasHistory);
    }

    [Fact]
    public void Monthly_history_wins_overlap_but_individual_sessions_remain_available()
    {
        var snapshots = new[] { Snapshot(1, 0), Snapshot(2, 600) };
        var sessions = new[] { Session(Utc(2026, 2, 3), 3600) };
        var lifetime = Build(snapshots, sessions);
        Assert.Equal(10, Assert.Single(lifetime.Bars).Hours);
        Assert.False(lifetime.Bars[0].IsTracked);
        var tracked = Build(snapshots, sessions, true);
        Assert.Equal(1, Assert.Single(tracked.Bars).Hours);
        Assert.True(tracked.Bars[0].IsTracked);
        Assert.Empty(tracked.Coverage);
    }

    [Fact]
    public void Session_crossing_month_boundary_is_split_without_losing_duration()
    {
        var result = Build([], [Session(Utc(2026, 1, 31).AddHours(23), 7200)]);
        Assert.Equal(2, result.Bars.Count);
        Assert.All(result.Bars, b => Assert.Equal(1, b.Hours));
        Assert.Equal(Utc(2026, 1, 1), result.Bars[0].StartUtc);
        Assert.Equal(Utc(2026, 2, 1), result.Bars[1].StartUtc);
        Assert.Empty(result.Coverage);
    }

    [Fact]
    public void Reset_and_conflicting_endpoint_are_unknown_not_zero()
    {
        Assert.Empty(Build([Snapshot(1, 600), Snapshot(2, 60)]).Bars);
        Assert.Empty(Build([Snapshot(1, 0), Snapshot(2, 60), Snapshot(2, 120)]).Bars);
        Assert.Empty(Build([Snapshot(1, 120), Snapshot(2, 240),
            new() { OwnershipId = 1, ObservedAt = Utc(2026, 2, 10), PlaytimeMinutes = 0 }]).Bars);
    }

    [Fact]
    public void Independent_ownership_counters_are_not_combined()
    {
        Assert.Empty(Build([Snapshot(1, 0), Snapshot(2, 60) with { OwnershipId = 2 }]).Bars);
    }

    [Fact]
    public void Future_incomplete_and_invalid_sessions_are_omitted()
    {
        Assert.Empty(Build([Snapshot(10, 0), Snapshot(11, 60)],
            [Session(Now.AddDays(1), 3600), Session(Now.AddDays(-1), 3600) with { EndedAt = null },
             Session(Now.AddDays(-1), 3600) with { DurationSeconds = -1 },
             Session(Now.AddDays(-1), 3600) with { DurationSeconds = 7200 }]).Bars);
    }

    [Fact]
    public void No_temporal_evidence_does_not_invent_a_release_date()
    {
        var result = Build([]);
        Assert.False(result.HasHistory);
        Assert.Equal(Now, result.StartUtc);
        Assert.Equal(Now, result.EndUtc);
        Assert.Equal(0, result.MaxHours);
    }

    [Fact]
    public void Acquisition_cannot_clip_earlier_play_and_future_dates_are_ignored()
    {
        var result = ActivityTimelineSeries.Build([], [Session(Utc(2026, 1, 1), 3600)],
            Utc(2026, 6, 1), Now.AddDays(1), Now);
        Assert.Equal(Utc(2026, 1, 1), result.StartUtc);
        Assert.Null(result.LastPlayedUtc);
    }

    [Fact]
    public void Tracked_range_keeps_older_sessions_and_gives_recent_sessions_room()
    {
        Assert.Equal(Now.AddDays(-30), Build([], [Session(Now.AddDays(-1), 3600)], true).StartUtc);
        Assert.Equal(Utc(2026, 1, 1), Build([], [Session(Utc(2026, 1, 1), 3600)], true).StartUtc);
    }

    [Fact]
    public void Tracked_summary_describes_sessions_instead_of_acquisition()
    {
        Assert.Equal("No completed sessions recorded yet", Build([], tracked: true).Summary);
        var result = Build([], [Session(Utc(2026, 1, 1), 3600), Session(Utc(2026, 2, 1), 3600)], true);
        Assert.StartsWith("2 observed sessions · ", result.Summary);
        Assert.DoesNotContain("Acquired", result.Summary);
    }

    [Fact]
    public void Baseline_summary_states_dated_counter_without_inventing_when_play_happened()
    {
        Assert.Contains("10h recorded by", Build([Snapshot(1, 600), Snapshot(3, 660)]).Summary);
        Assert.DoesNotContain("recorded by", Build([Snapshot(1, 600), Snapshot(1, 660)]).Summary);
        Assert.DoesNotContain("recorded by", Build([Snapshot(1, -1)]).Summary);
        Assert.DoesNotContain("recorded by", Build([Snapshot(1, 600), Snapshot(2, 660) with { OwnershipId = 2 }]).Summary);
    }

    private static ActivityTimelineSeries Build(IReadOnlyList<PlaytimeSnapshot> snapshots,
        IReadOnlyList<Session>? sessions = null, bool tracked = false) =>
        ActivityTimelineSeries.Build(snapshots, sessions ?? [], null, null, Now, tracked);
    private static PlaytimeSnapshot Snapshot(int month, long minutes) => new()
    {
        OwnershipId = 1, ObservedAt = Utc(2026, month, 1).AddMonths(1).AddSeconds(-1),
        PlaytimeMinutes = minutes
    };
    private static Session Session(DateTime start, long seconds) => new()
    {
        OwnershipId = 1, StartedAt = start, EndedAt = start.AddSeconds(seconds),
        DurationSeconds = seconds, DetectionMethod = "process"
    };
    private static DateTime Utc(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);
}
