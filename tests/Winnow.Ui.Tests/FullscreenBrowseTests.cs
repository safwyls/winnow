using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.ViewModels.Filters;
using Winnow.App.ViewModels.Fullscreen;
using Winnow.App.ViewModels.Lists;
using Winnow.App.Views.Fullscreen;
using Winnow.Covers;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenBrowseTests
{
    [Fact]
    public void Paging_preserves_the_selected_cell_and_clamps_the_final_page()
    {
        var state = new FullscreenBrowseState();
        var releases = Enumerable.Range(1, 27).Select(i => (long)i).ToArray();
        state.Select(6, 5);
        state.Reconcile(releases);
        Assert.True(state.MovePage(1, releases));
        Assert.Equal(18, state.SelectedReleaseId);
        Assert.Equal(5, state.PositionOnPage);
        Assert.True(state.MovePage(1, releases));
        Assert.Equal(27, state.SelectedReleaseId);
        Assert.Equal(2, state.PositionOnPage);
        Assert.False(state.MovePage(1, releases));
    }

    [Fact]
    public void Reload_follows_game_identity_when_sorting_changes()
    {
        var state = new FullscreenBrowseState();
        var releases = Enumerable.Range(1, 30).Select(i => (long)i).ToArray();
        state.Select(28, 3);
        state.Reconcile(releases);
        Assert.Equal(2, state.Page);
        state.Reconcile(releases.Reverse().ToArray());
        Assert.Equal(0, state.Page);
        Assert.Equal(2, state.PositionOnPage);
        Assert.Equal(28, state.SelectedReleaseId);
    }

    [Fact]
    public void Removing_last_page_and_emptying_library_keeps_a_valid_position()
    {
        var state = new FullscreenBrowseState();
        state.Select(28, 3);
        state.Reconcile(Enumerable.Range(1, 30).Select(i => (long)i).ToArray());
        state.Reconcile([1, 2]);
        Assert.Equal(0, state.Page);
        Assert.Equal(2, state.SelectedReleaseId);
        state.Reconcile([]);
        Assert.Null(state.SelectedReleaseId);
        Assert.Equal(0, state.PositionOnPage);
        Assert.Equal(1, state.PageCount(0));
        Assert.False(state.MovePage(1, []));
    }

    [AvaloniaFact]
    public async Task Filter_draft_does_not_change_collection_until_apply_or_touch_desktop()
    {
        var library = CreateLibrary();
        var desktop = CreateLibrary();
        await library.LoadCommand.ExecuteAsync(null);
        await desktop.LoadCommand.ExecuteAsync(null);
        var before = library.VisibleTiles;
        var draft = new FullscreenBrowseFilterDraft(library);
        var installed = draft.Filters.Groups.Single(g => g.Key == FilterPanelViewModel.InstalledKey);
        installed.AllOptions.Single(o => o.Key == "on_disk").IsChecked = true;
        draft.Sort = LibrarySort.NameAscending;
        draft.Bucket = library.Buckets[0];
        Assert.Null(library.SelectedBucket);
        Assert.Same(before, library.VisibleTiles);
        Assert.Null(library.Filters.ToFilter().Installed);
        Assert.Equal(LibrarySort.DormantLongest, library.Sort);
        draft.Apply(library);
        Assert.True(library.Filters.ToFilter().Installed);
        Assert.All(library.VisibleTiles, tile => Assert.True(tile.IsOnDisk));
        Assert.Equal(LibrarySort.NameAscending, library.Sort);
        Assert.Same(draft.Bucket, library.SelectedBucket);
        Assert.Null(desktop.SelectedBucket);
        Assert.Null(desktop.Filters.ToFilter().Installed);
        Assert.Equal(LibrarySort.DormantLongest, desktop.Sort);
    }

    [AvaloniaFact]
    public async Task Library_directional_navigation_preserves_column_and_restores_selected_game()
    {
        var library = CreateLibrary();
        await library.LoadCommand.ExecuteAsync(null);
        var context = new FullscreenContext(library, new FeedViewModel(new PreviewFeedService(), library), PreviewData.Shell);
        var page = new FullscreenBrowsePage(context, false);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            page.FocusInitial();
            Assert.Equal(library.VisibleTiles[0].AutomationName, AutomationProperties.GetName((Control)window.FocusManager!.GetFocusedElement()!));
            page.Handle(GamepadButtons.Right);
            page.Handle(GamepadButtons.Down);
            var selected = (Control)window.FocusManager.GetFocusedElement()!;
            Assert.Equal(library.VisibleTiles[7].AutomationName, AutomationProperties.GetName(selected));
            window.Content = new Button { Content = "Another page" };
            window.Content = page;
            Dispatcher.UIThread.RunJobs();
            page.FocusInitial();
            Assert.Equal(library.VisibleTiles[7].AutomationName, AutomationProperties.GetName((Control)window.FocusManager.GetFocusedElement()!));
        }
        finally { window.Close(); }
    }

    private static LibraryViewModel CreateLibrary() => new(new PreviewLibraryQueryRepository(),
        new PreviewOwnershipRepository(), new PreviewReleaseRepository(),
        new PreviewWorkRepository(), new PreviewUpdateEventRepository());

    [AvaloniaFact]
    public async Task Manual_list_actions_reorder_and_remove_the_selected_game()
    {
        var library = CreateLibrary();
        await library.LoadCommand.ExecuteAsync(null);
        var ids = library.AllTiles.Take(3).Select(tile => tile.ReleaseId).ToArray();
        var list = new GameListViewModel(GameList.Manual("Weekend") with { Id = 999 }) { ReleaseIds = ids };
        library.Lists.Lists.Add(list);
        library.OpenListCommand.Execute(list);
        var context = new FullscreenContext(library, new FeedViewModel(new PreviewFeedService(), library), PreviewData.Shell);
        var page = new FullscreenBrowsePage(context, false);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        context.PageRequested += requested => window.Content = requested;
        context.BackRequested += () => window.Content = page;
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            page.FocusInitial();
            Click(window, "More");
            Click(window, "Move selected game later");
            Assert.Equal(ids[0], list.ReleaseIds[1]);
            Click(window, "More");
            Click(window, "Remove selected game from list");
            Assert.DoesNotContain(ids[0], list.ReleaseIds);
            Assert.Equal(2, list.ReleaseIds.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Live_list_actions_save_edited_rules_and_restore_saved_rules()
    {
        var library = CreateLibrary();
        await library.LoadCommand.ExecuteAsync(null);
        var list = new GameListViewModel(GameList.Live("Store", new LibraryFilter { Stores = ["steam"] }) with { Id = 999 });
        library.Lists.LiveLists.Add(list);
        library.OpenListCommand.Execute(list);
        var context = new FullscreenContext(library, new FeedViewModel(new PreviewFeedService(), library), PreviewData.Shell);
        var page = new FullscreenBrowsePage(context, false);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        context.PageRequested += requested => window.Content = requested;
        context.BackRequested += () => window.Content = page;
        window.Show();
        try
        {
            library.Filters.Apply(new LibraryFilter { Stores = ["gog"] });
            Dispatcher.UIThread.RunJobs();
            Click(window, "More");
            Click(window, "Save live-list changes");
            Assert.Equal(["gog"], list.Filter.Stores);
            library.Filters.Apply(new LibraryFilter { Stores = ["steam"] });
            Dispatcher.UIThread.RunJobs();
            Click(window, "More");
            Click(window, "Restore saved live-list filters");
            Assert.Equal(["gog"], library.Filters.ToFilter().Stores);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Empty_feed_still_reaches_response_history()
    {
        var library = CreateLibrary();
        var context = new FullscreenContext(library, new FeedViewModel(new PreviewFeedService(), library), PreviewData.Shell);
        var page = new FullscreenBrowsePage(context, true);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        context.PageRequested += requested => window.Content = requested;
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Click(window, "What you've told the feed");
            Assert.IsType<FullscreenBrowseHistoryPage>(window.Content);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Feed_history_undo_revokes_the_original_verdict_and_keeps_its_history_row()
    {
        var library = CreateLibrary();
        await library.LoadCommand.ExecuteAsync(null);
        var service = new HistoryFeed(library.AllTiles[0].ReleaseId);
        var feed = new FeedViewModel(service, library);
        var context = new FullscreenContext(library, feed, PreviewData.Shell);
        var page = new FullscreenBrowseHistoryPage(context);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        context.PageRequested += requested => window.Content = requested;
        context.BackRequested += () => window.Content = page;
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var response = Assert.Single(window.GetVisualDescendants().OfType<Button>(), button => AutomationProperties.GetName(button)?.Contains("NOT INTERESTED", StringComparison.Ordinal) == true);
            response.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Click(window, "Undo this response");
            Assert.True(service.Revoked);
            Assert.Equal(FeedVerdictStatus.Undone, Assert.Single(feed.History.Entries).Status);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Fullscreen_records_only_the_visible_feed_shelf_as_surfaced()
    {
        var library = CreateLibrary();
        await library.LoadCommand.ExecuteAsync(null);
        var service = new HistoryFeed(library.AllTiles[0].ReleaseId);
        var feed = new FeedViewModel(service, library);
        await feed.LoadCommand.ExecuteAsync(null);
        var context = new FullscreenContext(library, feed, PreviewData.Shell);
        var page = new FullscreenBrowsePage(context, true);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        Assert.Empty(service.Surfaced);
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            page.FocusInitial();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(feed.Shelves[0].Cards.Count, service.Surfaced.Count);
            Assert.All(service.Surfaced, id => Assert.Contains(feed.Shelves[0].Cards, card => card.Tile.ReleaseId == id));
            page.Handle(GamepadButtons.Down);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            page.FocusInitial();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(feed.Shelves.Sum(shelf => shelf.Cards.Count), service.Surfaced.Count);
        }
        finally { window.Close(); }
    }

    private sealed class HistoryFeed(long releaseId) : IFeedService
    {
        private readonly PreviewFeedService _preview = new();
        public bool Revoked { get; private set; }
        public HashSet<long> Surfaced { get; } = [];
        public Task<FeedSnapshot> GetShelvesAsync(CancellationToken ct = default) => _preview.GetShelvesAsync(ct);
        public Task RecordSurfacedAsync(long id, string shelfId, CancellationToken ct = default) { Surfaced.Add(id); return Task.CompletedTask; }
        public Task<FeedVerdictOutcome> RecordVerdictAsync(long id, FeedVerdictKind kind, CancellationToken ct = default) => _preview.RecordVerdictAsync(id, kind, ct);
        public Task<bool> RevokeVerdictAsync(long id, FeedVerdictKind kind, CancellationToken ct = default) { Revoked = id == releaseId && kind == FeedVerdictKind.NotInterested; return Task.FromResult(Revoked); }
        public Task<IReadOnlyList<FeedVerdictRecord>> GetHistoryAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<FeedVerdictRecord>>(
            [new(releaseId, FeedVerdictKind.NotInterested, DateTime.UtcNow.AddDays(-1), null, Revoked ? DateTime.UtcNow : null, Revoked ? FeedVerdictStatus.Undone : FeedVerdictStatus.Active)]);
    }

    private static void Click(Window window, string name)
    {
        var button = Assert.Single(window.GetVisualDescendants().OfType<Button>(), button => AutomationProperties.GetName(button) == name);
        Assert.True(button.IsEnabled);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Cover_leases_pixels_only_while_attached_and_repaints_dormancy_changes()
    {
        using var vivid = new RenderTargetBitmap(new PixelSize(2, 3));
        using var floor = new RenderTargetBitmap(new PixelSize(2, 3));
        var leases = new TestLeases(new CoverArt(vivid, floor));
        var ramp = new DormancyRamp();
        var now = DateTime.UtcNow;
        var tile = TileFixture.Tile(now, lastPlayedUtc: now.AddYears(-4), playtimeMinutes: 200,
            coverKey: CoverKey.Steam("42"), covers: leases, ramp: ramp);
        var cover = new FullscreenCover(tile) { Width = 200, Height = 300 };
        var window = new Window { Width = 500, Height = 500, Content = cover };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.True(leases.Active > 0);
            var images = cover.GetVisualDescendants().OfType<Image>().ToArray();
            Assert.Contains(images, image => ReferenceEquals(image.Source, vivid));
            var art = images.Single(image => ReferenceEquals(image.Source, vivid));
            Assert.True(art.Opacity < 1);
            ramp.DimsDormantCovers = false;
            tile.RefreshDormancy();
            Assert.Equal(1, art.Opacity);
            window.Content = null;
            Assert.Equal(0, leases.Active);
            Assert.All(images, image => Assert.Null(image.Source));
            window.Content = cover;
            Dispatcher.UIThread.RunJobs();
            Assert.True(leases.Active > 0);
        }
        finally { window.Close(); }
        Assert.Equal(0, leases.Active);
    }

    [AvaloniaFact]
    public void Backdrop_uses_repository_screenshot_and_releases_pixels_on_detach()
    {
        using var pixels = new RenderTargetBitmap(new PixelSize(16, 9));
        var leases = new TestLeases(new CoverArt(pixels, pixels));
        using var services = new ServiceCollection().AddSingleton<ICoverLeases>(leases)
            .AddSingleton<IWorkImageRepository>(new ScreenshotImages()).BuildServiceProvider();
        var library = CreateLibrary();
        using var context = new FullscreenContext(library, new FeedViewModel(new PreviewFeedService(), library), PreviewData.Shell, services);
        var backdrop = new FullscreenBackdrop(context, TileFixture.Tile(DateTime.UtcNow));
        var window = new Window { Width = 1920, Height = 1080, Content = backdrop };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(CoverKey.IgdbScreenshot("tv_screenshot"), leases.Keys);
            var image = Assert.Single(backdrop.Children.OfType<Image>());
            Assert.Same(pixels, image.Source);
            Assert.True(leases.Active > 0);
            window.Content = null;
            Assert.Equal(0, leases.Active);
            Assert.Null(image.Source);
        }
        finally { window.Close(); }
    }

    private sealed class ScreenshotImages : IWorkImageRepository
    {
        public Task UpsertAsync(WorkImages images, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(long workId, string source, string kind, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkImages>> GetForWorkAsync(long workId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<WorkImages>>([new() { WorkId = workId, Source = ImageSources.Igdb,
                Kind = ImageKinds.Screenshot, ImageIds = "tv_screenshot", ObservedAt = DateTime.UtcNow }]);
    }

    private sealed class TestLeases(CoverArt art) : ICoverLeases
    {
        public int Active { get; private set; }
        public List<CoverKey> Keys { get; } = [];
        public ICoverLease Acquire(CoverKey key, double displayWidthPixels, CoverLayers layers = CoverLayers.VividAndFloor)
        { Active++; Keys.Add(key); return new Lease(this, art, key, CoverImaging.SnapWidth(displayWidthPixels), layers); }
        private sealed class Lease(TestLeases owner, CoverArt art, CoverKey key, int width, CoverLayers layers) : ICoverLease
        {
            private bool _disposed;
            public CoverKey Key => key;
            public int Width => width;
            public CoverLayers Layers => layers;
            public bool TryGetArt(out CoverArt result) { result = art; return true; }
            public Task<CoverArt?> GetAsync(CancellationToken ct = default) => Task.FromResult<CoverArt?>(art);
            public void Dispose() { if (_disposed) return; _disposed = true; owner.Active--; }
        }
    }
}
