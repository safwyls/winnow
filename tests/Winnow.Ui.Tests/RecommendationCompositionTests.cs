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
using Winnow.Core.Repositories;
using Winnow.Data;
using Winnow.Recommend;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class RecommendationCompositionTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Production_feed_renders_cold_games_and_keeps_action_impression_verdict_provenance(bool fullscreen, bool installedSibling)
    {
        await using var fixture = new DatabaseFixture();
        var db = fixture.Database;
        using (var connection = db.Factory.Open())
            connection.Execute("""
                INSERT INTO works(id,name,sort_name) VALUES (1,'Kept title','Kept title');
                INSERT INTO releases(id,work_id,name,platform) VALUES (1,1,'Kept title','windows'),(2,1,'Epic copy','windows');
                INSERT INTO ownerships(id,release_id,store,installed) VALUES (1,1,'steam',0),(2,2,'epic',@installed);
                """, new { installed = installedSibling ? 1 : 0 });
        var registrations = new ServiceCollection();
        registrations.AddLogging();
        registrations.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        Program.ConfigureServices(registrations, new(db.DatabasePath + "-data", db.DatabasePath, DataMigrationOutcome.Overridden));
        registrations.AddSingleton<ISqliteConnectionFactory>(db.Factory);
        await using var services = registrations.BuildServiceProvider();
        using var context = FullscreenContext.Create(services, services.GetRequiredService<MainWindowViewModel>());
        var library = fullscreen ? context.Library : services.GetRequiredService<LibraryViewModel>();
        var feed = fullscreen ? context.Feed : services.GetRequiredService<FeedViewModel>();
        await library.LoadCommand.ExecuteAsync(null);
        await feed.LoadCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        var shelf = Assert.Single(feed.Shelves);
        Assert.Equal(installedSibling ? ShelfIds.ReadyToPlay : ShelfIds.WaitingToBeOpened, shelf.Id);
        var card = Assert.Single(shelf.Cards);
        var expectedRelease = installedSibling ? 2 : 1;
        Assert.Equal(expectedRelease, card.SurfacingReleaseId);
        Assert.Equal(expectedRelease, card.Tile.PlayableEntry.ReleaseId);
        Assert.Equal(1, card.Tile.ReleaseId);
        Assert.Null(feed.Message);
        var window = new Window { Width = 1920, Height = 1080, Content = fullscreen
            ? new FullscreenBrowsePage(context, feed: true)
            : new FeedView { DataContext = feed } };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible
                && text.Text == shelf.Title);
            // The visual observation command and both action controls use the same release.
            await services.GetRequiredService<IFeedService>().RecordSurfacedAsync(card.SurfacingReleaseId, shelf.Id);
            await card.NotInterestedCommand.ExecuteAsync(null);
            var repository = services.GetRequiredService<IFeedFeedbackRepository>();
            Assert.Equal(expectedRelease, Assert.Single(await repository.GetActiveVerdictsAsync(DateTime.UtcNow)).ReleaseId);
            Assert.Contains(await repository.GetSurfacedSinceAsync(DateOnly.FromDateTime(DateTime.UtcNow)),
                item => item.ReleaseId == expectedRelease && item.ShelfId == shelf.Id);
            await card.UndoCommand.ExecuteAsync(null);
            Assert.Empty(await repository.GetActiveVerdictsAsync(DateTime.UtcNow));
            Assert.False(card.IsSetAside);
        }
        finally
        {
            window.Close();
            feed.Dispose();
            await (services.GetRequiredService<FeedViewModel>().LoadCommand.ExecutionTask ?? Task.CompletedTask);
            await (context.Feed.LoadCommand.ExecutionTask ?? Task.CompletedTask);
            await (feed.History.LoadCommand.ExecutionTask ?? Task.CompletedTask);
            await feed.Backfilling;
        }
    }

    private sealed class DatabaseFixture : IAsyncDisposable
    {
        public TempDatabase Database { get; } = new();

        public async ValueTask DisposeAsync()
        {
            // Viewport observation is deliberately fire-and-forget in both views.
            // After detaching, allow already-started SQLite calls to release their handles.
            for (var attempt = 0; ; attempt++)
            {
                try { Database.Dispose(); return; }
                catch (IOException) when (attempt < 50)
                {
                    await Task.Delay(10);
                    Dispatcher.UIThread.RunJobs();
                }
            }
        }
    }
}
