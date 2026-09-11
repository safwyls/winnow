using Avalonia.Controls;
using Avalonia.Layout;
using Winnow.App.Services;
using Winnow.App.ViewModels;

namespace Winnow.App.Views.Fullscreen;

/// <summary>The same recorded-play projection as desktop, paged for reading from a couch.</summary>
public sealed class FullscreenDetailsHistoryPage : FullscreenPage
{
    private readonly ActivityTrackerViewModel _tracker;
    private int _page;
    private const int PageSize = 6;
    public FullscreenDetailsHistoryPage(FullscreenContext context, ActivityTrackerViewModel tracker) : base(context)
    {
        _tracker = tracker;
        tracker.SnapshotChanged += TrackerSnapshotChanged;
        Render();
    }
    public override string Title => "Your play history";
    public override string Hints => "A Select   LT / RT Page   B Back";
    public override bool Handle(GamepadButtons buttons)
    {
        if (buttons.HasFlag(GamepadButtons.PagePrevious)) { _page = Math.Max(0, _page - 1); Render(); return true; }
        if (buttons.HasFlag(GamepadButtons.PageNext)) { _page = Math.Min(Math.Max(0, (_tracker.Series.Bars.Count - 1) / PageSize), _page + 1); Render(); return true; }
        return base.Handle(buttons);
    }
    private void TrackerSnapshotChanged(object? sender, EventArgs e)
    {
        var restore = PreserveFocus();
        Render(focus: false);
        restore();
    }
    public override void Dispose() { _tracker.SnapshotChanged -= TrackerSnapshotChanged; base.Dispose(); }

    private void Render(bool focus = true)
    {
        _page = Math.Clamp(_page, 0, Math.Max(0, (_tracker.Series.Bars.Count - 1) / PageSize));
        var lifetime = FullscreenUi.Button("Lifetime", () => { _tracker.ShowLifetimeCommand.Execute(null); _page = 0; Render(); });
        var tracked = FullscreenUi.Button("Tracked sessions", () => { _tracker.ShowTrackedSessionsCommand.Execute(null); _page = 0; Render(); });
        lifetime.Classes.Set("current", _tracker.IsLifetime);
        tracked.Classes.Set("current", _tracker.IsTrackedSessions);
        var modes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
        modes.Children.Add(lifetime);
        modes.Children.Add(tracked);
        var content = FullscreenUi.Stack(FullscreenUi.Text("Your play history", 32),
            FullscreenUi.Text($"{_tracker.TotalText} {_tracker.TotalLabel} · {_tracker.LastPlayedText}"), modes,
            FullscreenUi.Text(_tracker.Series.Summary, 24, "TextDim"),
            FullscreenUi.Text(_tracker.Series.CoverageNote, 24, "TextDim"));
        List<Control[]> rows = [[lifetime, tracked]];
        foreach (var bar in _tracker.Series.Bars.Reverse().Skip(_page * PageSize).Take(PageSize))
        {
            var record = FullscreenUi.Button(bar.Label, () => Context.Push(new FullscreenDetailsReadingPage(Context,
                bar.RecordDate, $"{bar.Label}\n{_tracker.Series.CoverageNote}")));
            content.Children.Add(record);
            rows.Add([record]);
        }
        if (!_tracker.HasRecords) content.Children.Add(FullscreenUi.Text("No recorded hours in this view."));
        content.Children.Add(FullscreenUi.Text(_tracker.UpdateSummary, 24, "TextDim"));
        Content = FullscreenUi.Scroll(content);
        SetFocusRows(rows.ToArray());
        if (focus) FocusInitial();
    }
}
