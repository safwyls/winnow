using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Threading;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.Core.Domain;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class ActivityTimelinePlotTests
{
    private static DateTime Date(int month, int day = 1) => new(2026, month, day, 0, 0, 0, DateTimeKind.Utc);

    [AvaloniaFact]
    public void Lifetime_months_have_equal_width_and_named_pointer_and_keyboard_selection()
    {
        var plot = Plot(Series([
            new(Date(2), Date(3), 2, false, "February · 2h · monthly history"),
            new(Date(7), Date(8), 4, true, "July · 4h · Winnow sessions"),
        ]));
        var window = Show(plot, 700);
        try
        {
            var bars = Bars(plot);
            Assert.Equal(2, bars.Length);
            Assert.Equal(bars[0].Bounds.Width, bars[1].Bounds.Width, 3);
            Assert.True(bars[0].Bounds.Width > 10);
            string? selected = null;
            plot.DetailSelected += value => selected = value;
            Activate(window, bars[0]);
            Assert.Equal("February · 2h · monthly history", selected);
            bars[1].Focus(NavigationMethod.Tab);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.Equal("July · 4h · Winnow sessions", selected);
            Assert.NotNull(ToolTip.GetTip(bars[1]));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Session_bars_are_ten_pixels_and_collisions_preserve_all_observations()
    {
        var plot = Plot(Series([
            new(Date(6, 1), Date(6, 1).AddHours(2), 2, true, "First session"),
            new(Date(6, 1).AddHours(3), Date(6, 1).AddHours(4), 1, true, "Second session"),
            new(Date(8, 1), Date(8, 1).AddHours(4), 4, true, "Third session"),
        ]));
        plot.IsTrackedSessions = true;
        var window = Show(plot, 700);
        try
        {
            var bars = Bars(plot);
            Assert.Equal(2, bars.Length);
            Assert.All(bars, bar => Assert.Equal(10, bar.Bounds.Width));
            Assert.Contains("2 observed sessions", AutomationProperties.GetName(bars[0]));
            Assert.Contains("3h", AutomationProperties.GetName(bars[0]));
            Assert.Equal("Third session", AutomationProperties.GetName(bars[1]));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Updates_cluster_without_a_count_limit_and_request_tracked_range()
    {
        var plot = Plot(Series([]));
        plot.Updates = Enumerable.Range(0, 30).Select(i => Update(Date(8, 1).AddMinutes(i))).ToArray();
        var window = Show(plot, 700);
        try
        {
            var mark = Assert.Single(Updates(plot));
            Assert.Contains("30 updates · 30 unread", AutomationProperties.GetName(mark));
            Assert.Same(plot.UpdateBrush, mark.Background);
            var requested = false;
            string? detail = null;
            plot.TrackedSessionsRequested += () => requested = true;
            plot.DetailSelected += value => detail = value;
            Activate(window, mark);
            Assert.True(requested);
            Assert.Contains("Patch", detail);
            Assert.Contains(plot.Updates[0].DateText, detail);
            foreach (var update in plot.Updates) update.IsAcknowledged = true;
            plot.Updates = plot.Updates.ToArray();
            Flush();
            mark = Assert.Single(Updates(plot));
            Assert.Contains("0 unread", AutomationProperties.GetName(mark));
            Assert.Same(plot.TextBrush, mark.Background);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Narrow_layout_keeps_date_labels_and_update_targets_inside_plot()
    {
        var plot = Plot(Series([]));
        plot.Updates = [Update(Date(1)), Update(Date(9, 8))];
        var window = Show(plot, 180);
        try
        {
            var ticks = plot.Children.OfType<TextBlock>().OrderBy(t => t.Bounds.X).ToArray();
            Assert.Contains(ticks, t => t.Text == "Today");
            for (var i = 1; i < ticks.Length; i++)
                Assert.True(ticks[i - 1].Bounds.Right < ticks[i].Bounds.Left);
            Assert.All(plot.Children, child =>
            {
                Assert.True(child.Bounds.Left >= 0);
                Assert.True(child.Bounds.Right <= plot.Bounds.Width + .01);
                Assert.True(child.Bounds.Bottom <= 150);
            });
        }
        finally { window.Close(); }
    }

    private static ActivityTimelineSeries Series(IReadOnlyList<ActivityTimelineBar> bars) => new()
    {
        StartUtc = Date(1), EndUtc = Date(9, 8), Bars = bars, Coverage = [],
        Summary = "Recorded activity", CoverageNote = "No monthly record", PeriodLabel = "Hours per month",
    };

    private static ActivityTimelinePlot Plot(ActivityTimelineSeries series) => new()
    {
        Series = series, HistoryBrush = Brushes.Teal, TrackedBrush = Brushes.Aquamarine,
        LineBrush = Brushes.DarkSlateGray, TextBrush = Brushes.LightGray,
        UpdateBrush = Brushes.HotPink, SurfaceBrush = Brushes.Black,
    };

    private static UpdateEventViewModel Update(DateTime date) => UpdateEventViewModel.Create(new UpdateEvent
    {
        ReleaseId = 1, Kind = UpdateEventKinds.Announcement, OccurredAt = date, Title = "Patch",
    }, Date(1).AddDays(-1));

    private static Button[] Bars(ActivityTimelinePlot plot) => plot.Children.OfType<Button>()
        .Where(b => b.Classes.Contains("activity-bar")).ToArray();
    private static Button[] Updates(ActivityTimelinePlot plot) => plot.Children.OfType<Button>()
        .Where(b => b.Classes.Contains("activity-update")).ToArray();

    private static Window Show(ActivityTimelinePlot plot, double width)
    {
        var window = new Window { Width = width, Height = 200, Content = plot };
        window.Show();
        Flush();
        return window;
    }

    private static void Activate(Window window, Button button)
    {
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Flush();
    }

    private static void Flush() => Dispatcher.UIThread.RunJobs();
}
