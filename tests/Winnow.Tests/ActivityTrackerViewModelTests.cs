using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Xunit;

namespace Winnow.Tests;

public sealed class ActivityTrackerViewModelTests
{
    [Fact]
    public void Acknowledgement_refreshes_plot_marks_without_changing_range()
    {
        var now = new DateTime(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc);
        var tracker = new ActivityTrackerViewModel([], [], now.AddYears(-2), now.AddDays(-20), 600, now);
        var update = UpdateEventViewModel.Create(new UpdateEvent
        {
            Id = 1, ReleaseId = 1, Kind = UpdateEventKinds.Announcement,
            OccurredAt = now.AddDays(-2), Title = "Exploration update"
        }, now.AddDays(-20));
        tracker.RefreshUpdates([update]);
        var previous = tracker.Updates;
        Assert.Equal("1 unread update", tracker.UpdateSummary);
        tracker.ShowTrackedSessionsCommand.Execute(null);
        update.IsAcknowledged = true;
        tracker.RefreshUpdates([update]);
        Assert.NotSame(previous, tracker.Updates);
        Assert.True(tracker.IsTrackedSessions);
        Assert.Equal("No unread updates", tracker.UpdateSummary);
    }

    [Fact]
    public void Linked_copy_scope_and_sparse_history_are_explicit()
    {
        var tracker = new ActivityTrackerViewModel([], [], null, null, 600, DateTime.UtcNow, "STEAM copy");
        Assert.Equal("10h", tracker.TotalText);
        Assert.Equal("played · STEAM copy", tracker.TotalLabel);
        Assert.Equal("Last-played date unavailable", tracker.LastPlayedText);
        Assert.False(tracker.ShowPlot);
        Assert.False(tracker.HasRecords);
    }
}
