using System.Collections.Concurrent;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dapper;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class LibraryRefreshOrderingTests
{
    [AvaloniaTheory]
    [InlineData(false, "maturity")]
    [InlineData(true, "maturity")]
    [InlineData(false, "account")]
    [InlineData(true, "account")]
    [InlineData(false, "non-game")]
    [InlineData(true, "non-game")]
    public async Task Slower_manual_reload_cannot_replace_newer_settings_and_metadata_refresh(bool fullscreen, string preference)
    {
        using var fixture = new Fixture();
        await fixture.SeedPreferenceAsync(preference);
        var library = fixture.Library;
        await library.LoadCommand.ExecuteAsync(null);
        await library.OpenDetailsCommand.ExecuteAsync(library.AllTiles.Single(tile => tile.OwnershipId == 1));
        var details = library.Details!;
        await details.MetadataEditor!.OpenCommand.ExecuteAsync(null);
        var summary = details.MetadataEditor.Rows.Single(row => row.Field == WorkFields.Summary);
        var observed = 0;
        library.TilesChanged += (_, _) => observed++;
        using var context = new FullscreenContext(library, PreviewData.Feed, PreviewData.Shell);
        using var tv = new FullscreenBrowsePage(context, false);
        var wall = new CoverWall { ItemTemplate = new FuncDataTemplate<GameTileViewModel>((_, _) => new GameTileView()) };
        wall.Bind(CoverWall.ItemsSourceProperty, new Binding(nameof(LibraryViewModel.VisibleTiles)) { Source = library });
        var count = new TextBlock();
        count.Bind(TextBlock.TextProperty, new Binding(nameof(LibraryViewModel.VisibleCountText)) { Source = library });
        var desktop = new DockPanel();
        DockPanel.SetDock(count, Dock.Top); desktop.Children.Add(count); desktop.Children.Add(new ScrollViewer { Content = wall });
        var window = new Window { Width = 1920, Height = 1080, Content = fullscreen ? tv : desktop };
        var delayed = fixture.Queries.HoldNext();
        Task? oldLoad = null;
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            oldLoad = library.LoadCommand.ExecuteAsync(null);
            await delayed.Captured.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await fixture.RestrictAsync(preference);
            summary.Draft = "Current summary";
            // This callback invokes the internal refresh path while the public load is still running.
            await summary.SaveCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            AssertWinning();
            delayed.Release.TrySetResult();
            await oldLoad;
            Dispatcher.UIThread.RunJobs();
            AssertWinning();
            Assert.Equal(1, observed);
        }
        finally { delayed.Release.TrySetResult(); if (oldLoad is not null) await oldLoad; window.Close(); }

        void AssertWinning()
        {
            Assert.Equal(1, library.TotalCount);
            Assert.Equal(1, library.AllGames.Count);
            Assert.Equal(1, Assert.Single(library.VisibleTiles).OwnershipId);
            Assert.Equal(1, library.SelectedTile!.OwnershipId);
            Assert.Same(details, library.Details);
            Assert.Same(library.AllTiles[0], details.Tile);
            Assert.Equal("Current summary", details.Summary);
            if (fullscreen)
            {
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.StartsWith("1 games ·", StringComparison.Ordinal) == true);
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button => AutomationProperties.GetName(button)?.StartsWith("Game 2,", StringComparison.Ordinal) == true);
            }
            else
            {
                Assert.Equal("1", count.Text);
                var visible = Assert.Single(wall.GetVisualDescendants().OfType<GameTileView>(), tile => tile.IsEffectivelyVisible);
                Assert.Equal(1, Assert.IsType<GameTileViewModel>(visible.DataContext).OwnershipId);
            }
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Winning_visibility_snapshot_closes_hidden_details_and_keeps_valid_selection(bool fullscreen)
    {
        using var fixture = new Fixture();
        await fixture.SeedPreferenceAsync("non-game");
        var library = fixture.Library;
        await library.LoadCommand.ExecuteAsync(null);
        await library.OpenDetailsCommand.ExecuteAsync(library.AllTiles.Single(tile => tile.OwnershipId == 2));
        library.SelectTile(library.AllTiles.Single(tile => tile.OwnershipId == 1));
        using var context = new FullscreenContext(library, PreviewData.Feed, PreviewData.Shell);
        GameDetailsViewModel? observed = library.Details;
        context.DetailsChanged += details => observed = details;
        using var tv = new FullscreenDetailsPage(context, library.Details!);
        var view = new GameDetailsView { DataContext = library.Details };
        var window = new Window { Width = 1920, Height = 1080, Content = fullscreen ? tv : view };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            await fixture.RestrictAsync("non-game");
            await library.LoadCommand.ExecuteAsync(null);
            Assert.Null(library.Details);
            Assert.Null(observed);
            Assert.Equal(1, library.SelectedTile!.OwnershipId);
            Assert.Single(library.VisibleTiles);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancelled_or_disposed_load_never_publishes_even_when_the_reader_ignores_cancellation(bool dispose)
    {
        using var fixture = new Fixture();
        var library = fixture.Library;
        using var context = new FullscreenContext(library, PreviewData.Feed, PreviewData.Shell);
        var changes = 0;
        library.TilesChanged += (_, _) => changes++;
        var delayed = fixture.Queries.HoldNext();
        var pending = library.LoadCommand.ExecuteAsync(null);
        await delayed.Captured.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (dispose) context.Dispose(); else library.LoadCommand.Cancel();
        delayed.Release.TrySetResult();
        await pending;
        Assert.Empty(library.AllTiles);
        Assert.Equal(0, library.TotalCount);
        Assert.Equal(0, changes);
    }

    [AvaloniaTheory]
    [InlineData("close")]
    [InlineData("new selection")]
    [InlineData("hidden")]
    [InlineData("new facts")]
    public async Task Pending_details_honor_close_new_selection_and_newly_published_visibility(string change)
    {
        using var fixture = new Fixture();
        await fixture.SeedPreferenceAsync("non-game");
        var library = fixture.Library;
        await library.LoadCommand.ExecuteAsync(null);
        var delayed = fixture.Updates.HoldNext();
        var pending = library.OpenDetailsCommand.ExecuteAsync(library.AllTiles.Single(tile => tile.OwnershipId == 2));
        await delayed.Captured.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            if (change == "close") library.CloseDetailsCommand.Execute(null);
            else if (change == "new selection") await library.OpenDetailsCommand.ExecuteAsync(library.AllTiles.Single(tile => tile.OwnershipId == 1));
            else
            {
                if (change == "hidden") await fixture.RestrictAsync("non-game");
                using (var connection = fixture.Database.Factory.Open()) connection.Execute("UPDATE works SET summary='Fresh fact' WHERE id=2;");
                await library.LoadCommand.ExecuteAsync(null);
            }
            delayed.Release.TrySetResult();
            await pending;
            if (change is "close" or "hidden") Assert.Null(library.Details);
            else if (change == "new selection") Assert.Equal(1, library.Details!.Tile.OwnershipId);
            else { Assert.Equal(2, library.Details!.Tile.OwnershipId); Assert.Equal("Fresh fact", library.Details.Summary); }
        }
        finally { delayed.Release.TrySetResult(); await pending; }
    }

    private sealed class Fixture : IDisposable
    {
        public TempDatabase Database { get; } = new();
        public DelayedQueries Queries { get; }
        public DelayedUpdates Updates { get; }
        public LibraryViewModel Library { get; }
        public Fixture()
        {
            LibraryReadFixtures.Seed(Database, 2);
            Queries = new DelayedQueries(new LibraryQueryRepository(Database.Factory));
            Updates = new DelayedUpdates(new UpdateEventRepository(Database.Factory));
            var works = new WorkRepository(Database.Factory);
            Library = new LibraryViewModel(Queries, new OwnershipRepository(Database.Factory),
                new ReleaseRepository(Database.Factory), works, Updates, lists: new GameListRepository(Database.Factory),
                metadataEdits: new WorkMetadataEditService(works, new WorkFieldSourceRepository(Database.Factory)));
        }
        public async Task SeedPreferenceAsync(string preference)
        {
            if (preference == "non-game")
            {
                using var connection = Database.Factory.Open();
                connection.Execute("UPDATE works SET steam_app_type='tool' WHERE id=2;");
                Library.ShowNonGameEntries = true;
            }
            else if (preference == "maturity")
                await new WorkMaturityRepository(Database.Factory).UpsertAsync(new WorkMaturity {
                    WorkId = 2, Source = MaturitySources.SteamStore, Ratings = MaturityRatingCodes.EsrbMature, ObservedAt = DateTime.UtcNow });
            else
            {
                var accounts = new OwnershipAccountRepository(Database.Factory);
                await accounts.UpsertAsync(new(1, "10001", 0, null, "steam_local", DateTime.UtcNow));
                await accounts.UpsertAsync(new(2, "10002", 0, null, "steam_local", DateTime.UtcNow));
                await new SettingsRepository(Database.Factory).SetAsync(SteamOwnedAccount.RefSettingKey, "10001");
                var inventories = new OwnershipInventoryRepository(Database.Factory);
                var attempt = await inventories.BeginAttemptAsync("steam", "10001", OwnershipInventorySources.SteamOwnedGames);
                await inventories.CompleteAsync(attempt, DateTime.UtcNow, 1);
            }
        }
        public async Task RestrictAsync(string preference)
        {
            if (preference == "non-game") Library.ShowNonGameEntries = false;
            else if (preference == "maturity") Library.MaturityCap = MaturityTier.Everyone;
            else await new SettingsRepository(Database.Factory).SetAsync(AccountScope.SettingKey, AccountScope.Own);
        }
        public void Dispose() { Library.Dispose(); Database.Dispose(); }
    }

    private sealed class Gate
    {
        public TaskCompletionSource Captured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task WaitAsync() { Captured.TrySetResult(); await Release.Task; }
    }
    private sealed class DelayedUpdates(IUpdateEventRepository inner) : IUpdateEventRepository
    {
        private readonly ConcurrentQueue<Gate> _holds = new();
        public Gate HoldNext() { var gate = new Gate(); _holds.Enqueue(gate); return gate; }
        public Task<long> InsertAsync(UpdateEvent row, CancellationToken ct = default) => inner.InsertAsync(row, ct);
        public async Task<IReadOnlyList<UpdateEvent>> GetByReleaseAsync(long releaseId, CancellationToken ct = default)
        {
            var rows = await inner.GetByReleaseAsync(releaseId, ct);
            if (_holds.TryDequeue(out var gate)) await gate.WaitAsync();
            return rows;
        }
    }
    private sealed class DelayedQueries(ILibraryQueryRepository inner) : ILibraryQueryRepository
    {
        private readonly ConcurrentQueue<Gate> _holds = new();
        public Gate HoldNext() { var gate = new Gate(); _holds.Enqueue(gate); return gate; }
        public async Task<LibrarySnapshot> GetSnapshotAsync(BucketThresholds thresholds, CancellationToken ct = default)
        {
            var snapshot = await inner.GetSnapshotAsync(thresholds, ct);
            if (_holds.TryDequeue(out var gate)) await gate.WaitAsync();
            return snapshot;
        }
        public Task<IReadOnlyList<OwnershipBucket>> GetOwnershipBucketsAsync(BucketThresholds thresholds, CancellationToken ct = default) => inner.GetOwnershipBucketsAsync(thresholds, ct);
        public Task<int> CountHiddenByAccountScopeAsync(BucketThresholds thresholds, CancellationToken ct = default) => inner.CountHiddenByAccountScopeAsync(thresholds, ct);
        public Task<int> CountHiddenByExplicitFilterAsync(BucketThresholds thresholds, CancellationToken ct = default) => inner.CountHiddenByExplicitFilterAsync(thresholds, ct);
        public Task<int> CountHiddenByRatingCapAsync(BucketThresholds thresholds, CancellationToken ct = default) => inner.CountHiddenByRatingCapAsync(thresholds, ct);
        public Task<IReadOnlyList<FacetTarget>> GetFacetTargetsAsync(CancellationToken ct = default) => inner.GetFacetTargetsAsync(ct);
    }
}
