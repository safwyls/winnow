using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.Core.Domain;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class ActivityTrackerInteractionTests
{
    [AvaloniaTheory]
    [InlineData(620)]
    [InlineData(360)]
    public void Range_buttons_switch_units_and_keep_monthly_and_session_records_reachable(int width)
    {
        var now = new DateTime(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc);
        var model = new ActivityTrackerViewModel(
            [Snapshot(2022, 3, 360), Snapshot(2022, 4, 600), Snapshot(2022, 5, 780)],
            [Session(now.AddDays(-20), 3600), Session(now.AddDays(-15), 7200)],
            now.AddYears(-6), now.AddDays(-15), 960, now);
        var view = new ActivityTrackerView { DataContext = model };
        var window = new Window { Width = width, Height = 600, Content = view,
            Background = (IBrush)view.FindResource("Surface")! };
        window.Show();
        Flush();
        try
        {
            Assert.Equal("Hours per month", model.Series.PeriodLabel);
            Assert.Equal(3, model.Series.Bars.Count);
            var tracked = view.FindControl<ToggleButton>("TrackedSessionsButton")!;
            Click(tracked, window);
            Assert.True(model.IsTrackedSessions);
            Assert.True(tracked.IsChecked);
            Click(tracked, window);
            Assert.True(tracked.IsChecked);
            Assert.Equal("Hours per session", model.Series.PeriodLabel);
            Assert.Equal(2, model.Series.Bars.Count);
            Assert.InRange(view.FindControl<ActivityTimelinePlot>("TimelinePlot")!.Bounds.Height, 140, 155);
            Assert.All(view.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0),
                control => Assert.True(control.Bounds.Width <= width + 1, control.GetType().Name));
            var lifetime = view.FindControl<ToggleButton>("LifetimeButton")!;
            Assert.True(lifetime.Focus(NavigationMethod.Tab));
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            Flush();
            Assert.True(model.IsLifetime);
            Assert.True(lifetime.IsChecked);
            Click(lifetime, window);
            Assert.True(lifetime.IsChecked);
            var directory = Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                window.CaptureRenderedFrame()?.Save(Path.Combine(directory, $"activity-tracker-{width}.png"));
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Grouped_updates_do_not_hide_history_when_no_sessions_exist()
    {
        var now = new DateTime(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc);
        var model = new ActivityTrackerViewModel([], [], now.AddYears(-5), now.AddYears(-2), 120, now);
        model.RefreshUpdates(Enumerable.Range(1, 3).Select(index => UpdateEventViewModel.Create(new UpdateEvent
        {
            Id = index, ReleaseId = 1, Kind = UpdateEventKinds.Announcement,
            OccurredAt = now.AddDays(-index), Title = $"Update {index}"
        }, now.AddYears(-2))).ToArray());
        var view = new ActivityTrackerView { DataContext = model };
        var window = new Window { Width = 620, Height = 600, Content = view };
        window.Show();
        Flush();
        try
        {
            var group = Assert.Single(view.GetVisualDescendants().OfType<Button>(), button => button.Classes.Contains("activity-update"));
            Click(group, window);
            Assert.True(model.IsLifetime);
            Assert.True(model.ShowPlot);
            Assert.Contains("Update 1", model.DetailText, StringComparison.Ordinal);
            Assert.Contains("Update 3", model.DetailText, StringComparison.Ordinal);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Sparse_tracker_keeps_an_honest_session_empty_state()
    {
        var model = new ActivityTrackerViewModel([], [], null, null, 120, DateTime.UtcNow);
        var view = new ActivityTrackerView { DataContext = model };
        var window = new Window { Width = 620, Height = 400, Content = view };
        window.Show();
        Flush();
        try
        {
            Assert.False(model.ShowPlot);
            Assert.Equal("Last-played date unavailable", model.LastPlayedText);
            Click(view.FindControl<ToggleButton>("TrackedSessionsButton")!, window);
            Assert.Contains("No completed sessions", model.DetailText, StringComparison.Ordinal);
            Assert.False(view.FindControl<Expander>("RecordedHoursDisclosure")!.IsVisible);
        }
        finally { window.Close(); }
    }

    private static PlaytimeSnapshot Snapshot(int year, int month, long minutes) => new()
    {
        OwnershipId = 1, PlaytimeMinutes = minutes,
        ObservedAt = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1).AddSeconds(-1)
    };

    private static Session Session(DateTime start, long seconds) => new()
    {
        OwnershipId = 1, StartedAt = start, EndedAt = start.AddSeconds(seconds),
        DurationSeconds = seconds, DetectionMethod = "process"
    };

    private static void Click(Control control, Window window)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Flush();
    }

    private static void Flush()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
