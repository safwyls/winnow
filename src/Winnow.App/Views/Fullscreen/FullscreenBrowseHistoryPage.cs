using Avalonia.Automation;
using Avalonia.Controls;
using Winnow.App.ViewModels;

namespace Winnow.App.Views.Fullscreen;

/// <summary>Verdicts remain inspectable and reversible after a recommendation leaves its shelf.</summary>
public sealed class FullscreenBrowseHistoryPage : FullscreenPage
{
    private readonly List<Button> _rows = [];
    private int _position;
    private bool _disposed;
    public FullscreenBrowseHistoryPage(FullscreenContext context) : base(context)
    {
        Content = FullscreenUi.Text("Loading your feed responses…", 32);
        AttachedToVisualTree += async (_, _) =>
        {
            await Context.Feed.History.LoadCommand.ExecuteAsync(null);
            if (_disposed) return;
            Build();
            FocusInitial();
        };
    }
    public override string Title => "What you've told the feed";
    public override string Hints => "A  Actions    B  Back";
    public override void Dispose() { _disposed = true; base.Dispose(); }
    public override void FocusInitial()
    {
        if (_rows.Count > 0) FocusControl(_rows[Math.Min(_position, _rows.Count - 1)]);
    }
    private void Build()
    {
        _rows.Clear();
        var body = FullscreenUi.Stack(FullscreenUi.Text(Title, 48));
        foreach (var entry in Context.Feed.History.Entries)
        {
            var position = _rows.Count;
            var label = $"{entry.Title} · {entry.KindLabel} · {entry.StatusNote} {entry.StatusDate}";
            var button = FullscreenUi.Button(label, () => Actions(entry));
            button.Content = FullscreenUi.Stack(FullscreenUi.Text(entry.Title, 32),
                FullscreenUi.Text($"{entry.KindLabel} · {entry.StatusNote} {entry.StatusDate}", 24, "TextDim"));
            AutomationProperties.SetName(button, label);
            button.GotFocus += (_, _) => _position = position;
            body.Children.Add(button);
            _rows.Add(button);
        }
        if (_rows.Count == 0) body.Children.Add(FullscreenUi.Text(Context.Feed.History.Message));
        var back = FullscreenUi.Button("Back to For you", Context.Back);
        body.Children.Add(back);
        _rows.Add(back);
        Content = FullscreenUi.Scroll(body);
        SetFocusRows(_rows.Select(button => new Control[] { button }).ToArray());
    }
    private void Actions(FeedHistoryEntryViewModel entry)
    {
        var actions = new List<FullscreenAction>();
        if (entry.CanUndo)
            actions.Add(new("Undo this response", async () =>
            {
                await entry.UndoCommand.ExecuteAsync(null);
                if (_disposed) return;
                Build();
                FocusInitial();
            }));
        if (Context.Library.TileForRelease(entry.ReleaseId) is { } tile)
            actions.Add(new("Open game", () => Context.OpenGame(tile)));
        actions.Add(new("Back to responses", () => { }));
        Context.ShowActions(entry.Title, actions);
    }
}
