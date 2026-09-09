using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Winnow.App.Design;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.Data.Repositories;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class HideBrowsingPositionTests
{
    [AvaloniaTheory]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    [InlineData(false, true, true)]
    public async Task Hiding_keeps_viewport_and_search_still_resets_it(bool grid, bool selection, bool atBottom)
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 100);
        var library = new LibraryViewModel(new LibraryQueryRepository(db.Factory),
            new OwnershipRepository(db.Factory), new ReleaseRepository(db.Factory),
            new WorkRepository(db.Factory), new UpdateEventRepository(db.Factory),
            hidden: new HiddenGameRepository(db.Factory));
        var shell = new MainWindowViewModel(library, PreviewData.MergeQueue, PreviewData.Stores,
            PreviewData.Appearance, new FeedViewModel(new PreviewFeedService(), library),
            PreviewData.AccountStats, PreviewData.LibrarySettings,
            applicationSettings: PreviewData.ApplicationSettings);
        await library.LoadCommand.ExecuteAsync(null);
        shell.ShowLibraryCommand.Execute(null);
        if (grid) library.ShowGridViewCommand.Execute(null);
        else library.ShowListViewCommand.Execute(null);
        var window = new MainWindow { Width = 1024, Height = 600 };
        try
        {
            window.Show();
            window.DataContext = shell;
            Dispatcher.UIThread.RunJobs();
            var scroll = grid ? window.FindControl<ScrollViewer>("GridScroll")!
                : (ScrollViewer)window.FindControl<ListBox>("ListRows")!.Scroll!;
            var target = library.VisibleTiles[20];
            if (selection)
            {
                library.SelectTile(target);
                library.SelectedTiles = [target, library.VisibleTiles.Last()];
                Dispatcher.UIThread.RunJobs();
            }
            scroll.Offset = new Vector(0, atBottom ? scroll.Extent.Height : 600);
            Dispatcher.UIThread.RunJobs();
            var before = scroll.Offset;
            Assert.True(before.Y > 0);
            if (selection)
            {
                await library.HideSelectionCommand.ExecuteAsync(null);
            }
            else
            {
                await library.HideGameCommand.ExecuteAsync(target);
            }
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(selection ? 98 : 99, library.VisibleTiles.Count);
            Assert.DoesNotContain(library.VisibleTiles, tile => tile.OwnershipId == target.OwnershipId);
            Assert.Equal(Math.Min(before.Y, scroll.Extent.Height - scroll.Viewport.Height), scroll.Offset.Y);
            library.SearchText = "Game 1";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, scroll.Offset.Y);
        }
        finally
        {
            window.Close();
        }
    }
}
