using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
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
using Winnow.Data.Repositories;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class UpdateAcknowledgementCompositionTests
{
    [AvaloniaFact]
    public async Task Selection_acknowledges_only_patches_visible_before_a_new_push_arrives()
    {
        using var db = new TempDatabase();
        Seed(db);
        await using var services = Services(db);
        var library = services.GetRequiredService<LibraryViewModel>();
        await library.LoadCommand.ExecuteAsync(null);
        library.SelectedBucket = Assert.Single(library.Buckets, bucket => bucket.Key == LibraryBuckets.StaleButPatched);
        var selected = Assert.Single(library.VisibleTiles);
        library.SelectedTiles = [selected];
        var watermark = selected.Game.MajorUpdateAt;
        var later = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var updates = new UpdateEventRepository(db.Factory);
        await updates.InsertAsync(new UpdateEvent { ReleaseId = 1, Kind = UpdateEventKinds.BuildPush, OccurredAt = later });
        await updates.InsertAsync(new UpdateEvent { ReleaseId = 1, Kind = UpdateEventKinds.Announcement, OccurredAt = later.AddDays(1) });

        await library.MarkSelectionAsReadCommand.ExecuteAsync(null);

        Assert.Null(library.PatchReadProblem);
        var remaining = Assert.Single(library.VisibleTiles);
        Assert.True(remaining.HasUnread);
        Assert.Equal(1, remaining.UnreadUpdateCount);
        using var connection = db.Factory.Open();
        var acknowledgements = connection.Query<DateTime>("SELECT acknowledged_through FROM update_acknowledgements;").ToArray();
        Assert.Equal(2, acknowledgements.Length);
        Assert.All(acknowledgements, through => Assert.True(through <= watermark));
    }

    [AvaloniaFact]
    public async Task Desktop_context_menu_binds_mark_as_read_only_for_Patched_selection()
    {
        using var db = new TempDatabase();
        Seed(db);
        await using var services = Services(db);
        var shell = services.GetRequiredService<MainWindowViewModel>();
        var library = shell.Library;
        await library.LoadCommand.ExecuteAsync(null);
        var window = new MainWindow { DataContext = shell };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var menu = window.GetVisualDescendants().OfType<Panel>()
                .Select(panel => panel.ContextMenu).Single(context => context is not null)!;
            menu.DataContext = shell;
            var mark = Assert.Single(menu.Items.OfType<MenuItem>(), item => Equals(item.Header, "Mark as read"));
            library.SelectedTiles = [Assert.Single(library.AllTiles, tile => tile.Game.ResolvedWorkId == 1)];
            Dispatcher.UIThread.RunJobs();
            Assert.False(mark.IsVisible);
            library.SelectedBucket = Assert.Single(library.Buckets, bucket => bucket.Key == LibraryBuckets.StaleButPatched);
            library.SelectedTiles = [Assert.Single(library.VisibleTiles)];
            Dispatcher.UIThread.RunJobs();
            Assert.True(mark.IsVisible);
            Assert.Same(library.MarkSelectionAsReadCommand, mark.Command);
            Assert.True(mark.Command!.CanExecute(mark.CommandParameter));
            mark.Command.Execute(mark.CommandParameter);
            await library.MarkSelectionAsReadCommand.ExecutionTask!;
            Assert.Empty(library.VisibleTiles);
        }
        finally
        {
            window.Close();
            await (services.GetRequiredService<FeedViewModel>().LoadCommand.ExecutionTask ?? Task.CompletedTask);
        }
    }

    [AvaloniaFact]
    public async Task Fullscreen_library_options_activate_mark_as_read_for_selected_game()
    {
        using var db = new TempDatabase();
        Seed(db);
        await using var services = Services(db);
        using var context = FullscreenContext.Create(services, services.GetRequiredService<MainWindowViewModel>());
        await using var pendingLoads = new PendingFeedLoads(services, context);
        var library = context.Library;
        await library.LoadCommand.ExecuteAsync(null);
        library.SelectedBucket = Assert.Single(library.Buckets, bucket => bucket.Key == LibraryBuckets.StaleButPatched);
        using var page = new FullscreenBrowsePage(context, feed: false);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        FullscreenPage? actions = null;
        context.PageRequested += opened => { actions = opened; window.Content = opened; };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            page.FocusInitial();
            Assert.True(page.Handle(GamepadButtons.Keyboard));
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(actions);
            Click(window, "Mark as read");
            await library.MarkSelectionAsReadCommand.ExecutionTask!;
            Assert.Null(library.PatchReadProblem);
            Assert.Empty(library.VisibleTiles);
        }
        finally { window.Close(); actions?.Dispose(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Patched_selection_marks_grouped_and_multiple_games_read_leaves_unselected_and_allows_later_patches(bool fullscreen)
    {
        using var db = new TempDatabase();
        Seed(db);
        using (var connection = db.Factory.Open())
        {
            connection.Execute("""
                INSERT INTO works (id, name, sort_name) VALUES (4, 'Selected game', 'Selected game'), (5, 'Unselected game', 'Unselected game');
                INSERT INTO releases (id, work_id, name, platform) VALUES (4, 4, 'Selected game', 'windows'), (5, 5, 'Unselected game', 'windows');
                INSERT INTO ownerships (id, release_id, store, installed) VALUES (4, 4, 'steam', 0), (5, 5, 'steam', 0);
                INSERT INTO play_records (ownership_id, playtime_minutes, last_played_at, source, observed_at) VALUES
                    (4, 600, '2025-01-01', 'steam_local', '2026-09-01'), (5, 600, '2025-01-01', 'steam_local', '2026-09-01');
                INSERT INTO update_events (release_id, kind, occurred_at) VALUES
                    (4, 'build_push', '2026-08-01'), (4, 'announcement', '2026-08-02'),
                    (5, 'build_push', '2026-08-01'), (5, 'announcement', '2026-08-02');
                """);
        }

        await using var services = Services(db);
        using var context = FullscreenContext.Create(services, services.GetRequiredService<MainWindowViewModel>());
        await using var pendingLoads = new PendingFeedLoads(services, context);
        var library = fullscreen ? context.Library : services.GetRequiredService<LibraryViewModel>();
        await library.LoadCommand.ExecuteAsync(null);
        library.SelectedBucket = Assert.Single(library.Buckets, bucket => bucket.Key == LibraryBuckets.StaleButPatched);
        var grouped = Assert.Single(library.VisibleTiles, tile => tile.Game.ResolvedWorkId == 1);
        var second = Assert.Single(library.VisibleTiles, tile => tile.Game.ResolvedWorkId == 4);
        library.SelectedTiles = [grouped, second];
        Assert.True(library.CanMarkSelectionAsRead);
        Assert.True(library.MarkSelectionAsReadCommand.CanExecute(null));

        await library.MarkSelectionAsReadCommand.ExecuteAsync(null);

        Assert.Null(library.PatchReadProblem);
        Assert.False(Assert.Single(library.AllTiles, tile => tile.Game.ResolvedWorkId == 1).HasUnread);
        Assert.False(Assert.Single(library.AllTiles, tile => tile.Game.ResolvedWorkId == 4).HasUnread);
        Assert.Equal(5, Assert.Single(library.VisibleTiles).Game.ResolvedWorkId);
        Assert.True(Assert.Single(library.VisibleTiles).HasUnread);
        using (var connection = db.Factory.Open())
            Assert.Equal(new long[] { 1, 2, 4 }, connection.Query<long>("SELECT release_id FROM update_acknowledgements ORDER BY release_id;"));

        var later = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var updates = new UpdateEventRepository(db.Factory);
        await updates.InsertAsync(new UpdateEvent { ReleaseId = 1, Kind = UpdateEventKinds.BuildPush, OccurredAt = later });
        await updates.InsertAsync(new UpdateEvent { ReleaseId = 1, Kind = UpdateEventKinds.Announcement, OccurredAt = later.AddDays(1) });
        await library.LoadCommand.ExecuteAsync(null);
        Assert.True(Assert.Single(library.VisibleTiles, tile => tile.Game.ResolvedWorkId == 1).HasUnread);
        Assert.DoesNotContain(library.VisibleTiles, tile => tile.Game.ResolvedWorkId == 4);
    }

    [AvaloniaFact]
    public async Task Mark_selection_as_read_requires_an_unread_selection_in_Patched()
    {
        using var db = new TempDatabase();
        Seed(db);
        await using var services = Services(db);
        var library = services.GetRequiredService<LibraryViewModel>();
        await library.LoadCommand.ExecuteAsync(null);
        var unread = Assert.Single(library.AllTiles, tile => tile.Game.ResolvedWorkId == 1);
        var read = Assert.Single(library.AllTiles, tile => tile.Game.ResolvedWorkId == 2);
        library.SelectedTiles = [unread];
        Assert.False(library.CanMarkSelectionAsRead);
        Assert.False(library.MarkSelectionAsReadCommand.CanExecute(null));

        library.SelectedBucket = Assert.Single(library.Buckets, bucket => bucket.Key == LibraryBuckets.StaleButPatched);
        library.SelectedTiles = [unread];
        Assert.True(library.CanMarkSelectionAsRead);
        library.SelectedTiles = [];
        Assert.False(library.CanMarkSelectionAsRead);
        library.SelectedTiles = [read];
        Assert.False(library.CanMarkSelectionAsRead);
        Assert.False(library.MarkSelectionAsReadCommand.CanExecute(null));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Patched_selection_reports_persistence_failure_and_keeps_unread_games(bool fullscreen)
    {
        using var db = new TempDatabase();
        Seed(db);
        using (var connection = db.Factory.Open())
            connection.Execute("""
                CREATE TRIGGER refuse_acknowledgement BEFORE INSERT ON update_acknowledgements
                BEGIN SELECT RAISE(FAIL, 'simulated storage failure'); END;
                """);
        await using var services = Services(db);
        using var context = FullscreenContext.Create(services, services.GetRequiredService<MainWindowViewModel>());
        await using var pendingLoads = new PendingFeedLoads(services, context);
        var library = fullscreen ? context.Library : services.GetRequiredService<LibraryViewModel>();
        await library.LoadCommand.ExecuteAsync(null);
        library.SelectedBucket = Assert.Single(library.Buckets, bucket => bucket.Key == LibraryBuckets.StaleButPatched);
        library.SelectedTiles = [Assert.Single(library.VisibleTiles, tile => tile.Game.ResolvedWorkId == 1)];

        await library.MarkSelectionAsReadCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrWhiteSpace(library.PatchReadProblem));
        Assert.True(Assert.Single(library.VisibleTiles, tile => tile.Game.ResolvedWorkId == 1).HasUnread);
        using var verification = db.Factory.Open();
        Assert.Equal(0, verification.ExecuteScalar<int>("SELECT COUNT(*) FROM update_acknowledgements;"));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Production_details_marks_and_restores_only_the_displayed_group_releases(bool fullscreen)
    {
        using var db = new TempDatabase();
        Seed(db);
        await using var services = Services(db);
        var desktop = services.GetRequiredService<LibraryViewModel>();
        using var context = FullscreenContext.Create(services, services.GetRequiredService<MainWindowViewModel>());
        await using var pendingLoads = new PendingFeedLoads(services, context);
        var library = fullscreen ? context.Library : desktop;
        await library.LoadCommand.ExecuteAsync(null);
        var tile = Assert.Single(library.AllTiles, tile => tile.Game.ResolvedWorkId == 1);
        Assert.True(tile.HasUnread);
        Assert.Equal(2, tile.UnreadUpdateCount);
        Assert.Equal(0, Assert.Single(library.AllTiles, tile => tile.Game.ResolvedWorkId == 2).UnreadUpdateCount);
        await library.OpenDetailsCommand.ExecuteAsync(tile);
        var details = Assert.IsType<GameDetailsViewModel>(library.Details);
        Assert.Equal(tile.UnreadUpdateCount, details.UnreadUpdateCount);
        details.SelectedTabIndex = 2;
        var window = new Window { Width = 1920, Height = 1080, Content = fullscreen
            ? new FullscreenDetailsPage(context, details, selectedSection: 1)
            : new GameDetailsView { DataContext = details } };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Click(window, "Mark as read");
            await details.DismissFlagCommand.ExecutionTask!;
            Dispatcher.UIThread.RunJobs();
            Assert.Null(details.FlagProblem);
            Assert.False(details.FlagIsRaised);
            Assert.True(details.ShowRestoreFlag);
            Assert.Equal(0, details.UnreadUpdateCount);
            Assert.DoesNotContain(details.Updates, row => row.IsUnread);
            Assert.False(Assert.Single(library.AllTiles, item => item.Game.ResolvedWorkId == 1).HasUnread);
            using (var connection = db.Factory.Open())
                Assert.Equal(new long[] { 1, 2 }, connection.Query<long>("SELECT release_id FROM update_acknowledgements ORDER BY release_id;"));

            Click(window, "Show it again");
            await details.RestoreFlagCommand.ExecutionTask!;
            Dispatcher.UIThread.RunJobs();
            Assert.Null(details.FlagProblem);
            Assert.True(details.ShowDismissFlag);
            Assert.Equal(2, details.UnreadUpdateCount);
            Assert.True(Assert.Single(library.AllTiles, item => item.Game.ResolvedWorkId == 1).HasUnread);
        }
        finally
        {
            window.Close(); details.Dispose();
            await (services.GetRequiredService<FeedViewModel>().LoadCommand.ExecutionTask ?? Task.CompletedTask);
            await (context.Feed.LoadCommand.ExecutionTask ?? Task.CompletedTask);
        }
    }

    [AvaloniaFact]
    public async Task A_push_arriving_after_details_open_is_not_acknowledged_and_reopening_reads_its_watermark()
    {
        using var db = new TempDatabase();
        Seed(db);
        await using var services = Services(db);
        var library = services.GetRequiredService<LibraryViewModel>();
        await library.LoadCommand.ExecuteAsync(null);
        await library.OpenDetailsCommand.ExecuteAsync(Assert.Single(library.AllTiles, tile => tile.Game.ResolvedWorkId == 1));
        var opened = library.Details!;
        var repository = new UpdateEventRepository(db.Factory);
        var later = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        await repository.InsertAsync(new UpdateEvent { ReleaseId = 1, Kind = UpdateEventKinds.BuildPush, OccurredAt = later });
        await repository.InsertAsync(new UpdateEvent { ReleaseId = 1, Kind = UpdateEventKinds.Announcement, OccurredAt = later.AddDays(1) });
        await opened.DismissFlagCommand.ExecuteAsync(null);
        Assert.True(Assert.Single(library.AllTiles, tile => tile.Game.ResolvedWorkId == 1).HasUnread);
        await library.OpenDetailsCommand.ExecuteAsync(Assert.Single(library.AllTiles, tile => tile.Game.ResolvedWorkId == 1));
        var reopened = library.Details!;
        Assert.Equal(1, reopened.UnreadUpdateCount);
        Assert.True(reopened.ShowDismissFlag);
        Assert.All(reopened.Updates.Where(row => row.ReleaseId == 2), row => Assert.False(row.IsUnread));
        Assert.Contains(reopened.Updates, row => row.ReleaseId == 1 && row.OccurredAtUtc == later && row.IsUnread);
        reopened.Dispose();
    }

    [AvaloniaTheory]
    [InlineData(0, false, 0)]
    [InlineData(600, false, 3)]
    [InlineData(0, true, 1)]
    [InlineData(600, true, 1)]
    public async Task Counts_respect_never_played_unknown_date_and_the_effective_play_boundary(long minutes, bool hasDate, int expected)
    {
        using var db = new TempDatabase();
        Seed(db);
        using (var connection = db.Factory.Open())
        {
            connection.Execute("DELETE FROM ownerships WHERE id = 2;");
            connection.Execute("UPDATE play_records SET playtime_minutes = @minutes, last_played_at = @played WHERE ownership_id = 1;",
                new { minutes, played = hasDate ? "2025-01-01 00:00:00" : null });
        }
        var rows = await new LibraryQueryRepository(db.Factory).GetOwnershipBucketsAsync(BucketThresholds.Default);
        Assert.Equal(expected, Assert.Single(rows, row => row.ReleaseId == 1).Game.UnreadUpdateCount);
    }

    private sealed class PendingFeedLoads(ServiceProvider services, FullscreenContext context) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await (services.GetRequiredService<FeedViewModel>().LoadCommand.ExecutionTask ?? Task.CompletedTask);
            await (context.Feed.LoadCommand.ExecutionTask ?? Task.CompletedTask);
        }
    }

    private static ServiceProvider Services(TempDatabase db)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        Program.ConfigureServices(services, new(db.DatabasePath + "-data", db.DatabasePath, DataMigrationOutcome.Overridden));
        services.AddSingleton<ISqliteConnectionFactory>(db.Factory);
        return services.BuildServiceProvider();
    }

    private static void Click(Window window, string name)
    {
        var button = Assert.Single(window.GetVisualDescendants().OfType<Button>(),
            button => button.IsEffectivelyVisible && AutomationProperties.GetName(button) == name);
        Assert.True(button.IsEnabled);
        Assert.True(button.Focus());
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
    }

    private static void Seed(TempDatabase db)
    {
        using var connection = db.Factory.Open();
        connection.Execute("""
            INSERT INTO works (id, name, sort_name) VALUES (1, 'Grouped game', 'Grouped game'), (2, 'Never played', 'Never played');
            INSERT INTO releases (id, work_id, name, platform) VALUES (1, 1, 'Steam copy', 'windows'), (2, 1, 'Epic copy', 'windows'), (3, 2, 'Never played', 'windows');
            INSERT INTO ownerships (id, release_id, store, installed) VALUES (1, 1, 'steam', 0), (2, 2, 'epic', 0), (3, 3, 'gog', 0);
            INSERT INTO play_records (ownership_id, playtime_minutes, last_played_at, source, observed_at) VALUES
                (1, 600, '2024-01-01', 'steam_local', '2026-09-01'), (2, 600, '2025-01-01', 'epic_local', '2026-09-01');
            INSERT INTO update_events (release_id, kind, occurred_at) VALUES
                (1, 'build_push', '2024-07-01'), (1, 'announcement', '2024-07-02'),
                (1, 'build_push', '2025-01-01'), (1, 'announcement', '2025-01-02'),
                (1, 'build_push', '2026-06-01'), (1, 'announcement', '2026-06-02'),
                (2, 'build_push', '2026-06-01'), (2, 'announcement', '2026-06-02'),
                (2, 'build_push', '2026-08-01'), (2, 'announcement', '2026-08-02'),
                (3, 'build_push', '2026-06-01'), (3, 'announcement', '2026-06-02');
            """);
    }
}
