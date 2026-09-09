using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.Interactivity;
using Avalonia.Media;
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
            Assert.Contains("Arrow", spine.Cursor!.ToString()!, StringComparison.OrdinalIgnoreCase);
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
            Assert.Equal(11, jumpToT.FontSize);
            var initialSection = LibraryViewModel.AlphabetSectionFor(library.VisibleTiles[0].Title);
            var currentLocation = spine.GetVisualDescendants().OfType<Button>().Single(button =>
                button.DataContext is AlphabetSectionViewModel section && section.Label == initialSection);
            Assert.Contains("alphalocation4", currentLocation.Classes);
            var currentGlyph = currentLocation.GetVisualDescendants().OfType<Border>()
                .Single(border => border.Classes.Contains("alphaglyph"));
            Assert.NotEqual(Brushes.Transparent, currentGlyph.Background);
            var glyphCenters = spine.GetVisualDescendants().OfType<Border>()
                .Where(border => border.Classes.Contains("alphaglyph"))
                .Select(border => border.TranslatePoint(new Point(border.Bounds.Width / 2, 0), window)!.Value.X)
                .ToArray();
            Assert.Equal(27, glyphCenters.Length);
            Assert.InRange(glyphCenters.Max() - glyphCenters.Min(), 0, 0.01);

            window.MouseMove(PointOnAlphabet(window, spine, 20));
            Flush();
            Assert.Contains("alphabetengaged", scrollbar.Classes);
            var thumb = scrollbar.GetVisualDescendants().OfType<Thumb>().Single();
            Assert.True(thumb.Bounds.Width >= 8, $"Engaged thumb width was {thumb.Bounds.Width}.");
            var centeredWave = Assert.IsType<Avalonia.Media.Transformation.TransformOperations>(
                currentLocation.RenderTransform).Value.M31;
            Assert.InRange(centeredWave, -13.01, -12.99);
            Assert.Contains("alphalocation4", currentLocation.Classes);

            window.MouseMove(PointOnAlphabet(window, spine, 2));
            window.MouseDown(PointOnAlphabet(window, spine, 2), Avalonia.Input.MouseButton.Left);
            Flush();
            var waveBeforeFractionalDrag = spine.GetVisualDescendants().OfType<Button>()
                .Select(WaveDisplacement)
                .ToArray();
            window.MouseMove(PointOnAlphabet(window, spine, 2.35));
            Flush();
            var waveAfterFractionalDrag = spine.GetVisualDescendants().OfType<Button>()
                .Select(WaveDisplacement)
                .ToArray();
            Assert.Contains(
                waveBeforeFractionalDrag.Zip(waveAfterFractionalDrag),
                pair => Math.Abs(pair.First - pair.Second) > 0.01);
            var sharedLocationRow = ExpectedAlphabetRow(library, scroll);
            var sharedLocationIndex = (int)Math.Round(sharedLocationRow);
            var sharedLocationButton = spine.GetVisualDescendants().OfType<Button>().ElementAt(sharedLocationIndex);
            Assert.Contains("alphalocation4", sharedLocationButton.Classes);
            Assert.InRange(
                WaveDisplacement(sharedLocationButton),
                ExpectedWaveDisplacement(sharedLocationIndex - sharedLocationRow) - 0.01,
                ExpectedWaveDisplacement(sharedLocationIndex - sharedLocationRow) + 0.01);
            window.MouseUp(PointOnAlphabet(window, spine, 2.35), Avalonia.Input.MouseButton.Left);
            Flush();
            Assert.True(jumpToT.Transitions is null or { Count: 0 });
            library.Ramp.ReducedMotion = true;
            Flush();
            Assert.True(jumpToT.Transitions is null or { Count: 0 });
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
            var locationButtons = spine.GetVisualDescendants().OfType<Button>().ToArray();
            var expectedLocationRow = ExpectedAlphabetRow(library, scroll);
            var locationIndex = (int)Math.Round(expectedLocationRow);
            Assert.Contains("alphalocation4", locationButtons[locationIndex].Classes);
            Assert.InRange(
                WaveDisplacement(locationButtons[locationIndex]),
                ExpectedWaveDisplacement(locationIndex - expectedLocationRow) - 0.01,
                ExpectedWaveDisplacement(locationIndex - expectedLocationRow) + 0.01);
            if (locationIndex > 0)
            {
                Assert.Contains("alphalocation3", locationButtons[locationIndex - 1].Classes);
            }
            if (locationIndex < locationButtons.Length - 1)
            {
                Assert.Contains("alphalocation3", locationButtons[locationIndex + 1].Classes);
            }

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
            Assert.All(spine.GetVisualDescendants().OfType<Button>(), button =>
                Assert.InRange(Math.Abs(Assert.IsType<Avalonia.Media.Transformation.TransformOperations>(
                    button.RenderTransform).Value.M31), 0, 0.01));

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

    private static Point PointOnAlphabet(Window window, Border spine, double row)
        => spine.TranslatePoint(
            new Point(spine.Bounds.Width / 2, spine.Bounds.Height * (row + 0.5) / 27),
            window)!.Value;

    private static double ExpectedAlphabetRow(LibraryViewModel library, ScrollViewer scroll)
    {
        var maximum = scroll.Extent.Height - scroll.Viewport.Height;
        var proportion = maximum <= 0 ? 0 : Math.Clamp(scroll.Offset.Y / maximum, 0, 1);
        var tilePosition = proportion * (library.VisibleTiles.Count - 1);
        var lowerTile = (int)Math.Floor(tilePosition);
        var upperTile = (int)Math.Ceiling(tilePosition);
        var sections = library.DisplayedAlphabetSections.ToArray();
        var lowerSection = LibraryViewModel.AlphabetSectionFor(library.VisibleTiles[lowerTile].Title);
        var upperSection = LibraryViewModel.AlphabetSectionFor(library.VisibleTiles[upperTile].Title);
        var lowerRow = Array.FindIndex(sections, section => section.Label == lowerSection);
        var upperRow = Array.FindIndex(sections, section => section.Label == upperSection);
        return lowerRow + ((upperRow - lowerRow) * (tilePosition - lowerTile));
    }

    private static double WaveDisplacement(Button button)
        => Assert.IsType<Avalonia.Media.Transformation.TransformOperations>(button.RenderTransform).Value.M31;

    private static double ExpectedWaveDisplacement(double signedDistance)
    {
        const double radius = 4;
        const double reach = 13;
        var distance = Math.Abs(signedDistance);
        return distance >= radius ? 0 : -reach * (1 + Math.Cos(Math.PI * distance / radius)) / 2;
    }

    private static void Flush() => Dispatcher.UIThread.RunJobs();
}
