using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Winnow.App.Design;
using Winnow.App.ViewModels.Lists;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.Core.Domain;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class ListBrowsingPositionTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Adding_to_a_list_keeps_the_scrolled_library_position(bool grid)
    {
        var library = new LibraryViewModel(new PreviewLibraryQueryRepository(),
            new PreviewOwnershipRepository(), new PreviewReleaseRepository(),
            new PreviewWorkRepository(), new PreviewUpdateEventRepository());
        var shell = new MainWindowViewModel(library, PreviewData.MergeQueue, PreviewData.Stores,
            PreviewData.Appearance, new FeedViewModel(new PreviewFeedService(), library),
            PreviewData.AccountStats, PreviewData.LibrarySettings,
            applicationSettings: PreviewData.ApplicationSettings);
        library.Prompt?.CancelCommand.Execute(null);
        library.CloseDetailsCommand.Execute(null);
        library.CloseListCommand.Execute(null);
        library.Filters.ClearCommand.Execute(null);
        library.SelectBucketCommand.Execute(null);
        library.SearchText = "";
        await library.LoadCommand.ExecuteAsync(null);
        shell.ShowLibraryCommand.Execute(null);
        if (grid) library.ShowGridViewCommand.Execute(null);
        else library.ShowListViewCommand.Execute(null);
        var target = new GameListViewModel(GameList.Manual("Scroll test") with { Id = -162 });
        library.Lists.Lists.Add(target);
        var window = new MainWindow { Width = 1024, Height = 600 };
        try
        {
            window.Show();
            window.DataContext = shell;
            Dispatcher.UIThread.RunJobs();
            var scroll = grid ? window.FindControl<ScrollViewer>("GridScroll")!
                : window.FindControl<ListBox>("ListRows")!.Scroll!;
            // Bound the list viewport so the eight-game fixture has overflow.
            if (!grid) ((Control)scroll).MaxHeight = 160;
            Dispatcher.UIThread.RunJobs();
            scroll.Offset = new Vector(0, 200);
            Dispatcher.UIThread.RunJobs();
            var before = scroll.Offset;
            Assert.True(before.Y > 0);
            library.SelectedTiles = [library.VisibleTiles.Last()];
            library.BeginAddToListCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            await library.Prompt!.ChooseCommand.ExecuteAsync(target);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before, scroll.Offset);
            Assert.Contains(library.VisibleTiles.Last().ReleaseId, target.ReleaseIds);
        }
        finally
        {
            library.Prompt?.CancelCommand.Execute(null);
            library.Lists.Lists.Remove(target);
            window.Close();
        }
    }
}
