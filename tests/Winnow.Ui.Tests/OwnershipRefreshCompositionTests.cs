using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Winnow.App;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Data;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class OwnershipRefreshCompositionTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Scheduled_acquisition_and_later_metadata_reach_the_open_surface_despite_partial_failure(bool fullscreen, bool fail)
    {
        await using var fixture = new DatabaseFixture();
        var db = fixture.Database;
        var registrations = new ServiceCollection();
        registrations.AddLogging();
        registrations.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        Program.ConfigureServices(registrations, new(db.DatabasePath + "-data", db.DatabasePath, DataMigrationOutcome.Overridden));
        registrations.AddSingleton<ISqliteConnectionFactory>(db.Factory);
        await using var services = registrations.BuildServiceProvider();
        Assert.IsType<OwnershipRefreshCoordinator>(services.GetRequiredService<IRemoteOwnershipSync>());
        var desktop = services.GetRequiredService<LibraryViewModel>();
        using var context = FullscreenContext.Create(services, services.GetRequiredService<MainWindowViewModel>());
        context.SetActive(fullscreen);
        await desktop.LoadCommand.ExecuteAsync(null);
        await context.LoadAsync();
        var library = fullscreen ? context.Library : desktop;
        var feed = fullscreen ? context.Feed : services.GetRequiredService<FeedViewModel>();
        var metadataEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseMetadata = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = services.GetRequiredService<LibraryChangePublisher>();
        var pipeline = new LibraryRefreshPipeline([
            new("metadata", async ct =>
            {
                metadataEntered.TrySetResult();
                await releaseMetadata.Task.WaitAsync(ct);
                using var connection = db.Factory.Open();
                await connection.ExecuteAsync("UPDATE works SET name='Enriched acquisition',sort_name='Enriched acquisition' WHERE id=1");
            }),
            new("optional outage", _ => fail ? Task.FromException(new IOException("offline")) : Task.CompletedTask)],
            publisher.PublishAsync, NullLogger<LibraryRefreshPipeline>.Instance);
        var coordinator = new OwnershipRefreshCoordinator(new Remote(db, fail), pipeline, NullLogger<OwnershipRefreshCoordinator>.Instance);
        using var scheduler = new RemoteOwnershipSchedulerService(new RecordingRemote(coordinator, finished),
            Options.Create(new RemoteOwnershipSchedulerOptions { RunOnStartup = true }), NullLogger<RemoteOwnershipSchedulerService>.Instance);
        var window = new Window { Width = 1920, Height = 1080, Content = fullscreen
            ? new FullscreenBrowsePage(context, feed: true) : new FeedView { DataContext = feed } };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            await scheduler.StartAsync(CancellationToken.None);
            await metadataEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await UntilAsync(() => library.AllTiles.Any(tile => tile.Title == "New acquisition"));
            Assert.True(window.IsVisible);
            releaseMetadata.TrySetResult();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await UntilAsync(() => library.AllTiles.Any(tile => tile.Title == "Enriched acquisition"));
            await UntilAsync(() => window.GetVisualDescendants().OfType<TextBlock>()
                .Any(text => text.IsEffectivelyVisible && text.Text == "Enriched acquisition"));
            if (!fullscreen)
            {
                // The inactive TV context defers its refresh until entry.
                context.SetActive(true);
                await UntilAsync(() => context.Library.AllTiles.Any(tile => tile.Title == "Enriched acquisition"));
            }
        }
        finally
        {
            releaseMetadata.TrySetResult();
            await scheduler.StopAsync(CancellationToken.None);
            window.Close(); context.Dispose(); feed.Dispose();
            await (feed.LoadCommand.ExecutionTask ?? Task.CompletedTask);
            await (context.Feed.LoadCommand.ExecutionTask ?? Task.CompletedTask);
            await feed.Backfilling; await feed.AdditionalShelvesLoading;
        }
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition()) { await Task.Delay(10, timeout.Token); Dispatcher.UIThread.RunJobs(); }
    }
    private sealed class Remote(TempDatabase db, bool fail) : IRemoteOwnershipSync
    {
        public async Task<LibrarySyncReport> SyncAsync(CancellationToken ct = default)
        {
            using var connection = db.Factory.Open();
            await connection.ExecuteAsync("""
                INSERT INTO works(id,name,sort_name) VALUES(1,'New acquisition','New acquisition');
                INSERT INTO releases(id,work_id,name,platform) VALUES(1,1,'New acquisition','windows');
                INSERT INTO ownerships(id,release_id,store,installed) VALUES(1,1,'steam',0);
                """);
            if (fail) throw new IOException("Synthetic later failure after commit");
            return new(1, null, TimeSpan.Zero);
        }
        public Task<LibrarySyncReport> SyncAsync(LocalLibraryScan scan, CancellationToken ct = default) => SyncAsync(ct);
    }
    private sealed class RecordingRemote(IRemoteOwnershipSync inner, TaskCompletionSource finished) : IRemoteOwnershipSync
    {
        public async Task<LibrarySyncReport> SyncAsync(CancellationToken ct = default)
        {
            try { return await inner.SyncAsync(ct); }
            finally { finished.TrySetResult(); }
        }
        public Task<LibrarySyncReport> SyncAsync(LocalLibraryScan scan, CancellationToken ct = default) => SyncAsync(ct);
    }
    private sealed class DatabaseFixture : IAsyncDisposable
    {
        public TempDatabase Database { get; } = new();
        public async ValueTask DisposeAsync()
        {
            for (var attempt = 0; ; attempt++)
            {
                try { Database.Dispose(); return; }
                catch (IOException) when (attempt < 50) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            }
        }
    }
}
