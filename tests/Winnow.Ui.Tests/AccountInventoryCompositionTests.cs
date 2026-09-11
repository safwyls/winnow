using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class AccountInventoryCompositionTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Library_and_feed_keep_unknown_ownership_until_a_complete_inventory_and_expand_after_failure(bool fullscreen, bool complete)
    {
        await using var fixture = new DatabaseFixture();
        var db = fixture.Database;
        using (var connection = db.Factory.Open()) connection.Execute("""
            INSERT INTO works(id,name,sort_name) VALUES(1,'My game','My game'),(2,'Household game','Household game');
            INSERT INTO releases(id,work_id,name,platform) VALUES(1,1,'My game','windows'),(2,2,'Household game','windows');
            INSERT INTO ownerships(id,release_id,store,installed) VALUES(1,1,'steam',0),(2,2,'steam',0);
            INSERT INTO ownership_accounts(ownership_id,account_ref,source,first_seen_at,last_seen_at)
                VALUES(1,'111','steam_local','2026-08-01','2026-08-01'),(2,'222','steam_local','2026-08-01','2026-08-01');
            INSERT INTO settings(key,value) VALUES('library.account_scope','own'),('steam.owned_account_ref','111');
            """);
        var registrations = new ServiceCollection();
        registrations.AddLogging();
        registrations.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        Program.ConfigureServices(registrations, new(db.DatabasePath + "-data", db.DatabasePath, DataMigrationOutcome.Overridden));
        registrations.AddSingleton<ISqliteConnectionFactory>(db.Factory);
        await using var services = registrations.BuildServiceProvider();
        using var context = FullscreenContext.Create(services, services.GetRequiredService<MainWindowViewModel>());
        var library = fullscreen ? context.Library : services.GetRequiredService<LibraryViewModel>();
        var feed = fullscreen ? context.Feed : services.GetRequiredService<FeedViewModel>();
        var inventories = services.GetRequiredService<IOwnershipInventoryRepository>();
        var attempt = await inventories.BeginAttemptAsync("steam", "111", OwnershipInventorySources.SteamOwnedGames);
        if (complete) Assert.True(await inventories.CompleteAsync(attempt, DateTime.UtcNow, 1));
        await library.LoadCommand.ExecuteAsync(null);
        await feed.LoadCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(complete ? 1 : 2, library.VisibleTiles.Count);
        Assert.Equal(complete ? 1 : 2, feed.Shelves.SelectMany(shelf => shelf.Cards).Count());
        var window = new Window { Width = 1920, Height = 1080, Content = fullscreen
            ? new FullscreenBrowsePage(context, feed: true) : new FeedView { DataContext = feed } };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text == "My game");
            // A new attempt which never completes cannot reuse the old assertion.
            await inventories.BeginAttemptAsync("steam", "111", OwnershipInventorySources.SteamOwnedGames);
            await library.LoadCommand.ExecuteAsync(null);
            await feed.LoadCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, library.VisibleTiles.Count);
            Assert.Contains(feed.Shelves.SelectMany(shelf => shelf.Cards), card => card.Tile.Title == "Household game");
            Assert.Equal(0, await services.GetRequiredService<ILibraryQueryRepository>().CountHiddenByAccountScopeAsync(BucketThresholds.Default));
        }
        finally
        {
            window.Close(); feed.Dispose();
            await (feed.LoadCommand.ExecutionTask ?? Task.CompletedTask);
            await (context.Feed.LoadCommand.ExecutionTask ?? Task.CompletedTask);
            await feed.Backfilling; await feed.AdditionalShelvesLoading;
        }
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
