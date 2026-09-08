using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.Core.Queries;
using Xunit;

namespace Winnow.Ui.Tests;

/// <summary>
/// The design-time preview data (<c>src/Winnow.App/Design/</c>) is what the
/// Avalonia previewer in Rider draws when a developer opens a view without
/// running the app (TASK-150). These tests attach that data to its views so
/// a constructor change or a re-bucketed fixture game fails here, instead of
/// leaving the previewer silently empty again.
///
/// <para><see cref="PreviewData"/>'s instances are process-wide singletons,
/// which suits these tests: the previewer behaves the same way, and the
/// loads are idempotent.</para>
/// </summary>
public sealed class DesignTimePreviewTests
{
    [AvaloniaFact]
    public async Task Preview_library_loads_a_tile_for_every_fabricated_game()
    {
        await PreviewData.LoadShellAsync();

        // Nine ownerships, eight tiles: the GOG Witcher folds onto the Steam tile.
        Assert.Equal(8, PreviewData.Library.VisibleTiles.Count);
        Assert.Contains(
            PreviewData.Library.VisibleTiles,
            tile => tile.Title == "The Witcher 3: Wild Hunt" && tile.Stores.Count == 2);

        // The dataset's intent, asserted so a date edit that quietly
        // re-buckets a game fails loudly: Stardew is the stale-but-patched
        // story and wears the unread badge (the badge IS the bucket, §5.2),
        // Hollow Knight is the started-but-abandoned one.
        var stardew = PreviewData.Library.VisibleTiles.Single(tile => tile.Title == "Stardew Valley");
        Assert.Equal(LibraryBuckets.StaleButPatched, stardew.Bucket);
        Assert.True(stardew.HasUnread);
        var hollowKnight = PreviewData.Library.VisibleTiles.Single(tile => tile.Title == "Hollow Knight");
        Assert.Equal(LibraryBuckets.Bounced, hollowKnight.Bucket);
    }

    [AvaloniaFact]
    public async Task Preview_library_opens_a_populated_details_modal()
    {
        await PreviewData.LoadShellAsync();

        var library = PreviewData.Library;
        var tile = library.VisibleTiles.Single(t => t.Title == "Stardew Valley");
        await library.OpenDetailsCommand.ExecuteAsync(tile);

        var details = Assert.IsType<GameDetailsViewModel>(library.Details);
        Assert.Equal("Stardew Valley", details.Tile.Title);
        Assert.NotEmpty(details.Updates);
        Assert.NotNull(details.GogPatchNotes);
    }

    [AvaloniaFact]
    public async Task Preview_feed_draws_cards_from_the_loaded_library()
    {
        await PreviewData.LoadShellAsync();

        Assert.NotEmpty(PreviewData.Feed.Shelves);
        Assert.All(PreviewData.Feed.Shelves, shelf => Assert.NotEmpty(shelf.Cards));
    }

    /// <summary>
    /// Every surface the previewer can be pointed at. A view that stops
    /// composing with its preview context throws here on attach. Keyed by
    /// name because xUnit's theory adapter cannot carry the view factories
    /// themselves.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(nameof(GameDetailsView))]
    [InlineData(nameof(GameTileView))]
    [InlineData(nameof(RowCoverView))]
    [InlineData(nameof(FeedCardView))]
    [InlineData(nameof(FeedView))]
    [InlineData(nameof(ActionBarView))]
    [InlineData(nameof(FilterPanelView))]
    [InlineData(nameof(StoresView))]
    [InlineData(nameof(AppearanceView))]
    [InlineData(nameof(MergeQueueView))]
    [InlineData(nameof(AccountStatsView))]
    [InlineData(nameof(LibrarySettingsView))]
    [InlineData(nameof(ApplicationSettingsView))]
    public void Preview_surface_attaches_and_renders(string surface)
    {
        var (view, context) = surface switch
        {
            nameof(GameDetailsView) => ((Control)new GameDetailsView(), (object)PreviewData.GameDetails),
            nameof(GameTileView) => (new GameTileView(), PreviewData.Tile),
            nameof(RowCoverView) => (new RowCoverView(), PreviewData.Tile),
            nameof(FeedCardView) => (new FeedCardView(), PreviewData.FeedCard),
            nameof(FeedView) => (new FeedView(), PreviewData.Feed),
            nameof(ActionBarView) => (new ActionBarView(), PreviewData.Library),
            nameof(FilterPanelView) => (new FilterPanelView(), PreviewData.Filters),
            nameof(StoresView) => (new StoresView(), PreviewData.Stores),
            nameof(AppearanceView) => (new AppearanceView(), PreviewData.Appearance),
            nameof(MergeQueueView) => (new MergeQueueView(), PreviewData.MergeQueue),
            nameof(AccountStatsView) => (new AccountStatsView(), PreviewData.AccountStats),
            nameof(LibrarySettingsView) => (new LibrarySettingsView(), PreviewData.LibrarySettings),
            nameof(ApplicationSettingsView) => (new ApplicationSettingsView(), PreviewData.ApplicationSettings),
            _ => throw new ArgumentOutOfRangeException(nameof(surface), surface, null),
        };

        var window = new Window { Width = 1280, Height = 820 };
        try
        {
            view.DataContext = context;
            window.Content = view;
            window.Show();

            // Something bound drew: a rendered surface with nothing in it is
            // the empty-preview failure these tests exist to catch. The row
            // cover is the one surface that is art only, with no text.
            var descendants = view.GetVisualDescendants().OfType<Control>().ToList();
            Assert.NotEmpty(descendants);
            if (view is not RowCoverView)
            {
                Assert.Contains(descendants, descendant => descendant is TextBlock);
            }
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// The whole shell the way the previewer draws it: the design-time
    /// context assigned, the window shown, <c>OnOpened</c> driving the same
    /// loads it drives at runtime. This is the closest a test can stand to
    /// the previewer, which cannot run under xUnit.
    /// </summary>
    [AvaloniaFact]
    public async Task Shell_preview_opens_and_populates_the_wall()
    {
        var window = new MainWindow { DataContext = PreviewData.Shell };
        try
        {
            window.Show();

            // OnOpened awaits the loads; give the dispatcher room to finish them.
            for (var i = 0; i < 200 && !PreviewData.Library.HasTiles; i++)
            {
                await Task.Delay(25);
            }

            Assert.True(PreviewData.Library.HasTiles, "The shell's library never loaded.");

            // The window opens on the feed; switch to the library screen so
            // the wall materializes its tile views.
            PreviewData.Shell.ShowLibraryCommand.Execute(null);
            for (var i = 0; i < 200
                && !window.GetVisualDescendants().OfType<GameTileView>().Any(); i++)
            {
                await Task.Delay(25);
            }

            Assert.NotEmpty(window.GetVisualDescendants().OfType<GameTileView>());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Details_preview_renders_the_fabricated_game()
    {
        var window = new Window { Width = 1280, Height = 820 };
        try
        {
            var view = new GameDetailsView { DataContext = PreviewData.GameDetails };
            window.Content = view;
            window.Show();

            var text = view.GetVisualDescendants().OfType<TextBlock>()
                .Select(block => block.Text)
                .Where(value => value is not null)
                .ToList();
            Assert.Contains(text, value => value!.Contains("Stardew Valley"));

            // The repo's review convention: set WINNOW_UI_CAPTURE_DIR to get a
            // frame of exactly what the previewer shows.
            if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is { } directory)
            {
                Directory.CreateDirectory(directory);
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(directory, "preview-game-details.png"));
            }
        }
        finally
        {
            window.Close();
        }
    }
}
