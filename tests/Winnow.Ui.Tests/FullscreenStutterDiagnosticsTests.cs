using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views.Fullscreen;
using Xunit;
using Xunit.Abstractions;

namespace Winnow.Ui.Tests;

public sealed class FullscreenStutterDiagnosticsTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public async Task Scheduled_render_frames_finish_the_row_transition()
    {
        using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        using var viewport = new FullscreenRowViewport(context);
        viewport.Configure(8, 1, row => new Border { Child = new Button { Content = $"Row {row}" } });
        var window = new Window { Width = 800, Height = 600, Content = viewport };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            viewport.Show(1);
            Assert.True(viewport.IsAnimating);
            var timeout = Stopwatch.StartNew();
            while (viewport.IsAnimating && timeout.Elapsed < TimeSpan.FromSeconds(3))
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(10);
            }
            window.UpdateLayout();
            Assert.False(viewport.IsAnimating);
            Assert.Equal(1, viewport.FirstRow);
            Assert.Equal(0, Top(viewport.GetRow(1)), 5);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Row_navigation_keeps_geometry_stable_before_layout()
    {
        using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        using var viewport = new FullscreenRowViewport(context);
        viewport.Configure(8, 1, row => new Border { Child = new Button { Content = $"Row {row}" } });
        var window = new Window { Width = 800, Height = 600, Content = viewport };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var row = viewport.GetRow(1);
            var initial = Top(row);
            viewport.Show(1);
            output.WriteLine($"Start={initial:F3}, after Show before layout={Top(row):F3}");
            Assert.Equal(initial, Top(row), 5);
            window.UpdateLayout();
            output.WriteLine($"After layout={Top(row):F3}");
            Assert.Equal(initial, Top(row), 5);
            viewport.AdvanceAnimation(TimeSpan.FromMilliseconds(90));
            var beforeReverse = Top(row);
            viewport.Show(0);
            output.WriteLine($"Before reverse={beforeReverse:F3}, after reverse before layout={Top(row):F3}");
            Assert.Equal(beforeReverse, Top(row), 5);
            window.UpdateLayout();
            Assert.Equal(beforeReverse, Top(row), 5);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(.7)]
    [InlineData(1)]
    [InlineData(1.4)]
    public void Home_navigation_preserves_geometry_and_unchanged_footer(double scale)
    {
        using var feed = new FeedViewModel(new PreviewFeedService(), PreviewData.Library);
        for (var shelf = 0; shelf < 10; shelf++)
            feed.Shelves.Add(new FeedShelfViewModel($"shelf-{shelf}", $"Shelf {shelf}", "",
                Enumerable.Range(0, 10).Select(_ => new FeedCardViewModel(PreviewData.Tile, "Ready to play."))));
        using var context = new FullscreenContext(PreviewData.Library, feed, PreviewData.Shell) { TextScale = scale };
        using var view = new FullscreenView(context);
        var window = new Window { Width = 1920, Height = 1080, Content = view };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var page = view.CurrentPage;
            var viewport = Assert.Single(page.GetVisualDescendants().OfType<FullscreenRowViewport>());
            var hints = view.GetVisualDescendants().OfType<ContentControl>().Single(c => c.Name == "FullscreenHints");
            var originalHints = hints.Content;
            var hero = page.GetLogicalDescendants().OfType<ContentControl>().Single(c =>
                c.Content is StackPanel stack && stack.Children.OfType<TextBlock>().Any(t => t.Name == "FullscreenHomeTitle"));
            var heroChanges = 0;
            hero.PropertyChanged += (_, e) => { if (e.Property == ContentControl.ContentProperty) heroChanges++; };
            var layouts = 0;
            var changes = 0;
            var resized = 0;
            page.LayoutUpdated += (_, _) => layouts++;
            page.PageChanged += (_, _) => changes++;
            viewport.SizeChanged += (_, _) => resized++;
            for (var step = 0; step < 8; step++)
            {
                layouts = changes = resized = heroChanges = 0;
                var before = viewport.Bounds;
                var watch = Stopwatch.StartNew();
                view.Handle(GamepadButtons.Down);
                var immediate = watch.Elapsed.TotalMilliseconds;
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                output.WriteLine($"scale={scale} step={step} inputMs={immediate:F2} settledMs={watch.Elapsed.TotalMilliseconds:F2} layoutEvents={layouts} pageChanged={changes} resized={resized} bounds={viewport.Bounds} sameViewport={ReferenceEquals(viewport, page.GetVisualDescendants().OfType<FullscreenRowViewport>().Single())} animating={viewport.IsAnimating}");
                Assert.Equal(before, viewport.Bounds);
                Assert.Same(originalHints, hints.Content);
                Assert.Equal(1, heroChanges);
                Assert.Same(viewport, page.GetVisualDescendants().OfType<FullscreenRowViewport>().Single());
                viewport.AdvanceAnimation(TimeSpan.FromMilliseconds(220));
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally { window.Close(); }
    }

    private static double Top(Control row) => row.Bounds.Y + Assert.IsType<TranslateTransform>(row.RenderTransform).Y;
}
