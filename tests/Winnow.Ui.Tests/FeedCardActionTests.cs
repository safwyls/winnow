using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.Core.Domain;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FeedCardActionTests
{
    [AvaloniaFact]
    public async Task Built_in_shelf_headers_explain_their_membership_in_tooltips()
    {
        await PreviewData.LoadShellAsync();
        var model = PreviewData.Feed;
        await model.LoadCommand.ExecuteAsync(null);
        var view = new FeedView { DataContext = model };
        var window = new Window { Width = 1000, Height = 700, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var explanations = view.GetVisualDescendants().OfType<StackPanel>()
                .Select(ToolTip.GetTip).OfType<string>().ToArray();
            Assert.All(model.Shelves, shelf => Assert.Contains(shelf.Blurb, explanations));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(420)]
    [InlineData(560)]
    public void Install_card_keeps_feedback_in_a_separate_vertical_lane(double width)
    {
        var tile = TileFixture.Tile(DateTime.UtcNow, title: "A long game title that needs installing",
            steamAppId: "123", ownership: new Ownership { ReleaseId = 1, Store = "steam" });
        using var model = new FeedCardViewModel(tile, "You have never opened this game.",
            new FeedbackService(), _ => { });
        var view = new FeedCardView { DataContext = model };
        var window = new Window { Width = width, Height = 320, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var primary = view.FindControl<Button>("PrimaryAction")!;
            Assert.Equal("Install", primary.Content);
            var names = new[] { "AddToList", "NotNow", "NotInterested" };
            var labels = new[] { "Add to list", "Not now", "Not interested" };
            Rect? previous = null;
            var primaryRect = new Rect(primary.TranslatePoint(default, view)!.Value, primary.Bounds.Size);
            for (var i = 0; i < names.Length; i++)
            {
                var button = view.FindControl<Button>(names[i])!;
                Assert.True(button.IsEffectivelyVisible);
                Assert.Equal(labels[i], AutomationProperties.GetName(button));
                Assert.Equal(labels[i], ToolTip.GetTip(button));
                var bounds = new Rect(button.TranslatePoint(default, view)!.Value, button.Bounds.Size);
                Assert.True(bounds.Width >= 32 && bounds.Height >= 32);
                Assert.True(bounds.Right <= view.Bounds.Width);
                Assert.True(bounds.Left >= primaryRect.Right);
                Assert.False(bounds.Intersects(primaryRect));
                if (previous is { } earlier)
                {
                    Assert.Equal(earlier.Left, bounds.Left);
                    Assert.True(bounds.Top >= earlier.Bottom);
                }
                previous = bounds;
            }
        }
        finally { window.Close(); }
    }

    private sealed class FeedbackService : IFeedService
    {
        public Task<FeedSnapshot> GetShelvesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task RecordSurfacedAsync(long releaseId, string shelfId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<FeedVerdictOutcome> RecordVerdictAsync(long releaseId, FeedVerdictKind kind, CancellationToken ct = default)
            => Task.FromResult(new FeedVerdictOutcome(true, null));
        public Task<bool> RevokeVerdictAsync(long releaseId, FeedVerdictKind kind, CancellationToken ct = default)
            => Task.FromResult(true);
        public Task<IReadOnlyList<FeedVerdictRecord>> GetHistoryAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FeedVerdictRecord>>([]);
    }
}
