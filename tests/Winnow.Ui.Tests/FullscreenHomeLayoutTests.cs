using Avalonia;
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
    [InlineData(5, 1)]
    [InlineData(10, 1.4)]
    public void Reducing_interface_scale_reduces_rendered_home_covers(double margin, double textScale)
    {
        using var feed = new FeedViewModel(new PreviewFeedService(), PreviewData.Library);
        for (var i = 0; i < 6; i++)
            feed.Shelves.Add(new FeedShelfViewModel($"shelf-{i}", $"Shelf {i + 1}", "",
                Enumerable.Range(0, 20).Select(_ => new FeedCardViewModel(PreviewData.Tile, "An update arrived."))));
        using var context = new FullscreenContext(PreviewData.Library, feed, PreviewData.Shell)
            { SafeMarginPercent = margin, TextScale = textScale };
        using var view = new FullscreenView(context);
        var window = new Window { Width = 1920, Height = 1080, Content = view };
        double CoverHeight()
        {
            var cover = view.CurrentPage.GetVisualDescendants().OfType<FullscreenCover>().First();
            return cover.Bounds.Height * cover.TransformToVisual(window)!.Value.M22;
        }
        void AssertBottomAlignment()
        {
            var page = view.CurrentPage;
            var cover = page.GetVisualDescendants().OfType<FullscreenCover>().First();
            var tile = cover.GetVisualAncestors().OfType<Button>().First();
            var wall = Assert.IsType<Grid>(tile.GetVisualParent());
            var bottom = wall.TranslatePoint(new Point(0, wall.Bounds.Height), page)!.Value.Y;
            Assert.Equal(page.Bounds.Height, bottom, 3);
            var heading = page.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Shelf 1" || t.Text == "Shelf 2");
            var headingBottom = heading.TranslatePoint(new Point(0, heading.Bounds.Height), page)!.Value.Y;
            var wallTop = wall.TranslatePoint(default, page)!.Value.Y;
            Assert.InRange(wallTop - headingBottom, 11, 13);
            var indicator = page.GetVisualDescendants().OfType<FullscreenShelfIndicator>().Single();
            var railCenter = indicator.TranslatePoint(new Point(0, indicator.Bounds.Height / 2), page)!.Value.Y;
            var wallCenter = wall.TranslatePoint(new Point(0, wall.Bounds.Height / 2), page)!.Value.Y;
            Assert.InRange(Math.Abs(railCenter - wallCenter), 0, 1);
        }
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var original = CoverHeight();
            Assert.True(original > 100);
            AssertBottomAlignment();
            context.UiScale = .8; Dispatcher.UIThread.RunJobs();
            AssertBottomAlignment();
            Assert.InRange(CoverHeight() / original, .78, .82);
            view.Handle(GamepadButtons.Down); Dispatcher.UIThread.RunJobs();
            Assert.Contains(view.CurrentPage.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Shelf 2");
            Assert.InRange(CoverHeight() / original, .78, .82);
            context.UiScale = 1; Dispatcher.UIThread.RunJobs();
            Assert.InRange(CoverHeight() / original, .98, 1.02);
            context.UiScale = 1.2; Dispatcher.UIThread.RunJobs();
            AssertBottomAlignment();
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(.7)]
    [InlineData(1)]
    [InlineData(1.4)]
    public void Long_title_keeps_single_line_cover_geometry(double scale)
    {
        var source = PreviewData.Tile;
        using var feed = new FeedViewModel(new PreviewFeedService(), PreviewData.Library);
        feed.Shelves.Add(new FeedShelfViewModel("titles", "Ready to play", "",
            new[] { "A short title", string.Join(" ", Enumerable.Repeat("The Forgotten Kingdom", 10)) }
                .Select(name => new FeedCardViewModel(new GameTileViewModel(source.Entries, source.Game, name, DateTime.UtcNow), "An update arrived."))));
        using var context = new FullscreenContext(PreviewData.Library, feed, PreviewData.Shell) { TextScale = scale };
        using var television = new FullscreenView(context);
        var window = new Window { Width = 1280, Height = 720, Content = television };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var initial = television.CurrentPage.GetVisualDescendants().OfType<FullscreenCover>()
                .Select(c => (c.Bounds, c.TranslatePoint(default, television))).ToArray();
            Assert.NotEmpty(initial);
            television.Handle(GamepadButtons.Right); Dispatcher.UIThread.RunJobs();
            var title = television.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "FullscreenHomeTitle");
            Assert.True(title.Text!.Length > 100);
            Assert.Single(title.TextLayout.TextLines);
            Assert.Equal(64, title.FontSize);
            Assert.Equal(TextTrimming.WordEllipsis, title.TextTrimming);
            Assert.Equal(initial, television.CurrentPage.GetVisualDescendants().OfType<FullscreenCover>()
                .Select(c => (c.Bounds, c.TranslatePoint(default, television))).ToArray());
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Root_backdrops_fill_canvas_outside_safe_margins(bool ultrawide)
    {
        using var feed = new FeedViewModel(new PreviewFeedService(), PreviewData.Library);
        feed.Shelves.Add(new FeedShelfViewModel("art", "Ready to play", "",
            new[] { new FeedCardViewModel(PreviewData.Tile, "An update arrived.") }));
        using var context = new FullscreenContext(PreviewData.Library, feed, PreviewData.Shell)
            { SafeMarginPercent = 10 };
        context.SetFitUltrawide(ultrawide);
        using var television = new FullscreenView(context);
        var window = new Window { Width = ultrawide ? 2560 : 1920, Height = 1080, Content = television };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            for (var i = 0; i < 4; i++)
            {
                var backdrop = television.CurrentPage.Backdrop!;
                Assert.NotNull(backdrop);
                var origin = backdrop.TranslatePoint(default, television)!.Value;
                Assert.Equal(0, origin.X, 4);
                Assert.Equal(0, origin.Y, 4);
                Assert.Equal(television.Bounds.Width, backdrop.Bounds.Width, 4);
                Assert.Equal(1080, backdrop.Bounds.Height, 4);
                var pageOrigin = television.CurrentPage.TranslatePoint(default, television)!.Value;
                Assert.True(pageOrigin.X > 0);
                Assert.True(pageOrigin.Y > 0);
                television.Handle(GamepadButtons.Next); Dispatcher.UIThread.RunJobs();
            }
        }
        finally { window.Close(); }
    }

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
