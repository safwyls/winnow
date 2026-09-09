using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Closing_details_restores_the_scrolled_library_position(bool grid)
    {
        var (library, shell) = await LoadedLibraryAsync(grid);
        var window = new MainWindow { Width = 1200, Height = 640, DataContext = shell };
        try
        {
            window.Show();
            Flush();
            var scroll = ScrollFor(window, grid);
            ((Control)scroll).MaxHeight = 160;
            Flush();
            scroll.Offset = new Vector(0, 200);
            Flush();
            var before = scroll.Offset;
            Assert.True(before.Y > 0);

            await library.OpenDetailsCommand.ExecuteAsync(library.VisibleTiles.Last());
            Flush();
            // Exercise the close-time restore rather than merely proving that
            // opening details happens not to disturb this synthetic viewport.
            scroll.Offset = default;
            Flush();
            Assert.Equal(0, scroll.Offset.Y);
            library.CloseDetailsCommand.Execute(null);
            Flush();

            Assert.Equal(before, scroll.Offset);
        }
        finally
        {
            library.CloseDetailsCommand.Execute(null);
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Alphabet_spine_switches_to_name_order_and_jumps_the_active_view(bool grid)
    {
        var (library, shell) = await LoadedLibraryAsync(grid);
        var window = new MainWindow { Width = 1200, Height = 640, DataContext = shell };
        try
        {
            window.Show();
            Flush();
            var scroll = ScrollFor(window, grid);
            ((Control)scroll).MaxHeight = 160;
            Flush();
            Assert.Equal(0, scroll.Offset.Y);

            var spine = window.FindControl<Border>("AlphabetSpine")!;
            Assert.False(spine.IsVisible);
            library.Sort = LibrarySort.NameAscending;
            Flush();
            Assert.True(spine.IsVisible);
            var scrollbar = scroll.GetVisualDescendants().OfType<ScrollBar>()
                .Single(bar => bar.Orientation == Avalonia.Layout.Orientation.Vertical);
            var spineBounds = new Rect(spine.TranslatePoint(default, window)!.Value, spine.Bounds.Size);
            var scrollbarBounds = new Rect(
                scrollbar.TranslatePoint(default, window)!.Value, scrollbar.Bounds.Size);
            Assert.True(spineBounds.Right <= scrollbarBounds.Left);
            Assert.InRange(scrollbarBounds.Left - spineBounds.Right, 0, 2);

            var buttons = window.GetVisualDescendants().OfType<Button>().ToArray();
            var jumpToT = buttons.Single(button => AutomationProperties.GetName(button) == "Jump to T");
            var jumpToA = buttons.Single(button => AutomationProperties.GetName(button) == "Jump to A");
            var jumpToSymbols = buttons.Single(button =>
                AutomationProperties.GetName(button) == "Jump to numbers and symbols");
            Assert.True(jumpToT.IsEnabled);
            Assert.False(jumpToA.IsEnabled);
            Assert.False(jumpToSymbols.IsEnabled);

            window.MouseMove(PointOnAlphabet(window, spine, 20));
            Flush();
            Assert.Contains("alphabetengaged", scrollbar.Classes);
            var thumb = scrollbar.GetVisualDescendants().OfType<Thumb>().Single();
            Assert.True(thumb.Bounds.Width >= 8, $"Engaged thumb width was {thumb.Bounds.Width}.");
            Assert.Contains("alphawave4", jumpToT.Classes);
            Assert.NotEmpty(jumpToT.Transitions!);
            library.Ramp.ReducedMotion = true;
            Flush();
            Assert.Empty(jumpToT.Transitions!);
            library.Ramp.ReducedMotion = false;
            Flush();

            jumpToT.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Flush();

            Assert.Equal(LibrarySort.NameAscending, library.Sort);
            Assert.True(scroll.Offset.Y > 0);
            Assert.StartsWith("T", library.VisibleTiles.Last().Title, StringComparison.OrdinalIgnoreCase);

            scroll.Offset = default;
            Flush();
            DragAlphabet(window, spine, fromRow: 2, toRow: 13);
            var scrollableHeight = scroll.Extent.Height - scroll.Viewport.Height;
            Assert.True(scrollableHeight > 0);
            Assert.InRange(scroll.Offset.Y / scrollableHeight, 0.48, 0.52);

            library.Sort = LibrarySort.NameDescending;
            Flush();
            scroll.Offset = default;
            var descendingButtons = window.GetVisualDescendants().OfType<Button>().ToArray();
            var jumpToC = descendingButtons.Single(button =>
                AutomationProperties.GetName(button) == "Jump to C");
            jumpToC.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Flush();
            Assert.Equal(LibrarySort.NameDescending, library.Sort);
            Assert.True(scroll.Offset.Y > 0);

            window.MouseMove(PointOnAlphabet(window, spine, 8));
            Flush();

            if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is { } directory)
            {
                Directory.CreateDirectory(directory);
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(directory, $"alphabet-{(grid ? "grid" : "list")}.png"));
            }

            window.MouseMove(new Point(400, 300));
            Flush();
            Assert.DoesNotContain("alphabetengaged", scrollbar.Classes);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(),
                button => button.Classes.Any(name => name?.StartsWith("alphawave", StringComparison.Ordinal) == true));

            library.Sort = LibrarySort.PlaytimeHighToLow;
            Flush();
            Assert.False(spine.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [Theory]
    [InlineData("Élan", "E")]
    [InlineData("  Zelda", "Z")]
    [InlineData("123 Robots", "#")]
    [InlineData("™Game", "#")]
    public void Alphabet_sections_fold_diacritics_and_group_non_letters(string title, string expected)
        => Assert.Equal(expected, LibraryViewModel.AlphabetSectionFor(title));

    private static async Task<(LibraryViewModel Library, MainWindowViewModel Shell)> LoadedLibraryAsync(bool grid)
    {
        var library = new LibraryViewModel(new PreviewLibraryQueryRepository(),
            new PreviewOwnershipRepository(), new PreviewReleaseRepository(),
            new PreviewWorkRepository(), new PreviewUpdateEventRepository());
        var shell = new MainWindowViewModel(library, PreviewData.MergeQueue, PreviewData.Stores,
            PreviewData.Appearance, new FeedViewModel(new PreviewFeedService(), library),
            PreviewData.AccountStats, PreviewData.LibrarySettings,
            applicationSettings: PreviewData.ApplicationSettings);
        await library.LoadCommand.ExecuteAsync(null);
        shell.ShowLibraryCommand.Execute(null);
        if (grid) library.ShowGridViewCommand.Execute(null);
        else library.ShowListViewCommand.Execute(null);
        return (library, shell);
    }

    private static ScrollViewer ScrollFor(MainWindow window, bool grid)
        => grid ? window.FindControl<ScrollViewer>("GridScroll")!
            : (ScrollViewer)window.FindControl<ListBox>("ListRows")!.Scroll!;

    private static void DragAlphabet(Window window, Border spine, int fromRow, int toRow)
    {
        var start = PointOnAlphabet(window, spine, fromRow);
        var end = PointOnAlphabet(window, spine, toRow);
        window.MouseMove(start);
        window.MouseDown(start, Avalonia.Input.MouseButton.Left);
        window.MouseMove(end);
        Flush();
        window.MouseUp(end, Avalonia.Input.MouseButton.Left);
        Flush();
    }

    private static Point PointOnAlphabet(Window window, Border spine, int row)
        => spine.TranslatePoint(
            new Point(spine.Bounds.Width / 2, spine.Bounds.Height * (row + 0.5) / 27),
            window)!.Value;

    private static void Flush() => Dispatcher.UIThread.RunJobs();
}
