using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views.Fullscreen;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenHomeLayoutTests
{
    [AvaloniaTheory]
    [InlineData(.7)]
    [InlineData(1)]
    [InlineData(1.4)]
    public void Description_length_preserves_home_cover_geometry(double scale)
    {
        using var feed = new FeedViewModel(new PreviewFeedService(), PreviewData.Library);
        feed.Shelves.Add(new FeedShelfViewModel("reasons", "Ready to play", "",
            new[] { "", "A new update arrived.", string.Join(" ", Enumerable.Repeat("Explore new regions and finish your adventure.", 30)) }
                .Select(reason => new FeedCardViewModel(PreviewData.Tile, reason))));
        using var context = new FullscreenContext(PreviewData.Library, feed, PreviewData.Shell) { TextScale = scale };
        using var television = new FullscreenView(context);
        var window = new Window { Width = 1280, Height = 720, Content = television };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var covers = television.GetVisualDescendants().OfType<FullscreenCover>().Select(c => c.Bounds).ToArray();
            Assert.NotEmpty(covers);
            for (var i = 0; i < 3; i++)
            {
                var reason = television.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "FullscreenHomeReason");
                Assert.Equal(72 * scale, reason.Height, 6);
                Assert.Equal(2, reason.MaxLines);
                Assert.Equal(TextTrimming.WordEllipsis, reason.TextTrimming);
                Assert.True(reason.TextLayout.TextLines.Count <= 2);
                Assert.Equal(covers, television.GetVisualDescendants().OfType<FullscreenCover>().Select(c => c.Bounds).ToArray());
                television.Handle(GamepadButtons.Right);
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1280, 720)]
    [InlineData(3840, 2160)]
    public void Long_home_hero_settles_after_returning_from_settings_at_large_text(double width, double height)
    {
        var source = PreviewData.Tile;
        var tile = new GameTileViewModel(source.Entries, source.Game,
            "The Forgotten Kingdom: Adventures Beyond the Distant Northern Mountains", DateTime.UtcNow);
        using var feed = new FeedViewModel(new PreviewFeedService(), PreviewData.Library);
        feed.Shelves.Add(new FeedShelfViewModel("long", "Patched while you were away", "",
            Enumerable.Range(0, 10).Select(_ => new FeedCardViewModel(tile,
                "A major expansion arrived while you were away. Return to explore new regions, finish your adventure and see what changed."))));
        using var context = new FullscreenContext(PreviewData.Library, feed, PreviewData.Shell)
            { TextScale = 1.4, SafeMarginPercent = 2 };
        using var television = new FullscreenView(context);
        var originalTheme = context.ThemeId;
        var page = television.CurrentPage;
        var changes = 0;
        page.PageChanged += (_, _) =>
        {
            // Fail within the dispatcher loop, rather than waiting for a hung test timeout.
            Assert.True(++changes < 80, "Home keeps rebuilding instead of settling its scaled layout.");
        };
        var window = new Window { Width = width, Height = height, Content = television };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            television.Handle(GamepadButtons.Previous);
            context.ThemeId = "bottle-green";
            television.Handle(GamepadButtons.Next);
            Dispatcher.UIThread.RunJobs();
            var settled = changes;
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(settled, changes);
            Assert.Equal("For you", television.CurrentPage.Title);
            Assert.NotNull(window.FocusManager!.GetFocusedElement());
        }
        finally { window.Close(); context.ThemeId = originalTheme; }
    }
}
