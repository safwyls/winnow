using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class DesktopFeedShelfTests
{
    [AvaloniaTheory]
    [InlineData(1600)]
    [InlineData(900)]
    public void Covers_remain_in_one_row_and_narrow_shelves_scroll(int width)
    {
        using var feed = CreateFeed();
        var view = new FeedView { DataContext = feed };
        var window = new Window { Width = width, Height = 1000, Content = view };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var grids = view.GetVisualDescendants().OfType<FeedGrid>().ToArray();
            Assert.Equal(2, grids.Length);
            foreach (var grid in grids)
            {
                Assert.Equal(6, grid.Children.Count);
                Assert.All(grid.Children, child => Assert.Equal(0, child.Bounds.Top));
                Assert.All(grid.Children, child => Assert.InRange(child.Bounds.Width, 180, 240));
                var scroll = grid.GetVisualAncestors().OfType<ScrollViewer>().First();
                if (width == 900) Assert.True(scroll.Extent.Width > scroll.Viewport.Width);
                else Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Keyboard_scrolls_hidden_cards_into_view_and_keeps_the_column_between_shelves()
    {
        using var feed = CreateFeed();
        var view = new FeedView { DataContext = feed };
        var window = new Window { Width = 900, Height = 700, Content = view };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var grids = view.GetVisualDescendants().OfType<FeedGrid>().ToArray();
            for (var i = 0; i < 6; i++)
            {
                Assert.True(view.HandleNavigationKey(new KeyEventArgs { Key = Key.Right }));
                Dispatcher.UIThread.RunJobs();
            }
            AssertFocusedCard(0, 5);
            Assert.True(view.HandleNavigationKey(new KeyEventArgs { Key = Key.Down }));
            Dispatcher.UIThread.RunJobs();
            AssertFocusedCard(1, 5);
            Assert.True(view.HandleNavigationKey(new KeyEventArgs { Key = Key.Up }));
            Dispatcher.UIThread.RunJobs();
            AssertFocusedCard(0, 5);

            void AssertFocusedCard(int shelf, int index)
            {
                var focused = Assert.IsAssignableFrom<Control>(window.FocusManager!.GetFocusedElement());
                var card = focused.GetVisualAncestors().OfType<FeedCardView>().First();
                Assert.Same(feed.Shelves[shelf].Cards[index], card.DataContext);
                var scroll = grids[shelf].GetVisualAncestors().OfType<ScrollViewer>().First();
                Assert.True(scroll.Offset.X > 0);
                var point = card.TranslatePoint(default, scroll)!.Value;
                Assert.InRange(point.X, -1, scroll.Viewport.Width - card.Bounds.Width + 1);
            }
        }
        finally { window.Close(); }
    }

    private static FeedViewModel CreateFeed()
    {
        var feed = new FeedViewModel(new PreviewFeedService(), PreviewData.Library) { IsLoading = false, Message = null };
        for (var shelf = 0; shelf < 2; shelf++)
            feed.Shelves.Add(new FeedShelfViewModel($"shelf-{shelf}", $"Shelf {shelf + 1}", "Curated games from your library.",
                Enumerable.Range(0, 6).Select(_ => new FeedCardViewModel(PreviewData.Tile, "An update arrived."))));
        return feed;
    }
}
