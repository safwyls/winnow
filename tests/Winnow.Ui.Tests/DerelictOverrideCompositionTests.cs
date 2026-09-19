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
using Winnow.Core.Lifecycle;
using Winnow.Core.Queries;
using Winnow.Data;
using Winnow.Data.Repositories;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class DerelictOverrideCompositionTests
{
    [AvaloniaFact]
    public async Task Desktop_context_menu_removes_selected_group_only_in_Derelict()
    {
        using var db = new TempDatabase();
        await SeedAsync(db);
        await using var services = Services(db);
        await using var pendingLoads = new PendingFeedLoads(services);
        var shell = services.GetRequiredService<MainWindowViewModel>();
        var library = shell.Library;
        await library.LoadCommand.ExecuteAsync(null);
        var window = new MainWindow { DataContext = shell };
        try
        {
            window.Show();
            Assert.True(await window.StartupLibraryReady);
            Dispatcher.UIThread.RunJobs();
            var menu = window.GetVisualDescendants().OfType<Panel>()
                .Select(panel => panel.ContextMenu).Single(context => context is not null)!;
            menu.DataContext = shell;
            var remove = Assert.Single(menu.Items.OfType<MenuItem>(), item => Equals(item.Header, "Remove from Derelict"));
            library.SelectedTiles = [Assert.Single(library.AllTiles, tile => tile.Game.ResolvedWorkId == 1)];
            Dispatcher.UIThread.RunJobs();
            Assert.False(remove.IsVisible);
            Assert.False(library.RemoveSelectionFromDerelictCommand.CanExecute(null));

            SelectDerelict(library);
            library.SelectedTiles = [];
            Dispatcher.UIThread.RunJobs();
            Assert.False(remove.IsVisible);
            Assert.False(library.RemoveSelectionFromDerelictCommand.CanExecute(null));
            SelectGroup(library);
            Dispatcher.UIThread.RunJobs();
            Assert.True(remove.IsVisible);
            Assert.Same(library.RemoveSelectionFromDerelictCommand, remove.Command);
            Assert.True(remove.Command!.CanExecute(remove.CommandParameter));
            remove.Command.Execute(remove.CommandParameter);
            await library.RemoveSelectionFromDerelictCommand.ExecutionTask!;

            AssertRemovedGroup(library);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Fullscreen_library_options_activate_removal_for_selected_group()
    {
        using var db = new TempDatabase();
        await SeedAsync(db);
        await using var services = Services(db);
        using var context = FullscreenContext.Create(services, services.GetRequiredService<MainWindowViewModel>());
        await using var pendingLoads = new PendingFeedLoads(services, context);
        var library = context.Library;
        var desktop = services.GetRequiredService<LibraryViewModel>();
        await desktop.LoadCommand.ExecuteAsync(null);
        SelectDerelict(desktop);
        await library.LoadCommand.ExecuteAsync(null);
        SelectDerelict(library);
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
            Assert.Equal(1, Assert.Single(library.SelectedTiles).Game.ResolvedWorkId);
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(actions);
            var remove = Assert.Single(window.GetVisualDescendants().OfType<Button>(), button =>
                button.IsEffectivelyVisible && AutomationProperties.GetName(button) == "Remove from Derelict");
            Assert.True(remove.IsEnabled);
            Assert.True(remove.Focus());
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            await library.RemoveSelectionFromDerelictCommand.ExecutionTask!;

            AssertRemovedGroup(library);
            AssertRemovedGroup(desktop);
        }
        finally { window.Close(); actions?.Dispose(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Persistence_failure_keeps_group_in_Derelict_and_reports_the_problem(bool fullscreen)
    {
        using var db = new TempDatabase();
        await SeedAsync(db);
        using (var connection = db.Factory.Open())
            connection.Execute("""
                CREATE TRIGGER refuse_exemption BEFORE INSERT ON derelict_exemptions
                BEGIN SELECT RAISE(FAIL, 'simulated storage failure'); END;
                """);
        await using var services = Services(db);
        using var context = FullscreenContext.Create(services, services.GetRequiredService<MainWindowViewModel>());
        await using var pendingLoads = new PendingFeedLoads(services, context);
        var library = fullscreen ? context.Library : services.GetRequiredService<LibraryViewModel>();
        await library.LoadCommand.ExecuteAsync(null);
        SelectDerelict(library);
        SelectGroup(library);

        await library.RemoveSelectionFromDerelictCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrWhiteSpace(library.DerelictProblem));
        Assert.Equal(2, library.VisibleTiles.Count);
        Assert.Equal(LibraryBuckets.Derelict,
            Assert.Single(library.VisibleTiles, tile => tile.Game.ResolvedWorkId == 1).Game.Bucket);
        using var verification = db.Factory.Open();
        Assert.Equal(0, verification.ExecuteScalar<int>("SELECT COUNT(*) FROM derelict_exemptions;"));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Removal_survives_new_services_and_later_evidence_for_every_grouped_copy(bool fullscreen)
    {
        using var db = new TempDatabase();
        await SeedAsync(db);
        await using (var services = Services(db))
        {
            using var context = FullscreenContext.Create(services, services.GetRequiredService<MainWindowViewModel>());
            await using var pendingLoads = new PendingFeedLoads(services, context);
            var library = fullscreen ? context.Library : services.GetRequiredService<LibraryViewModel>();
            await library.LoadCommand.ExecuteAsync(null);
            SelectDerelict(library);
            SelectGroup(library);
            await library.RemoveSelectionFromDerelictCommand.ExecuteAsync(null);
            AssertRemovedGroup(library);
        }

        var lifecycle = new LifecycleRepository(db.Factory);
        foreach (var releaseId in new long[] { 1, 2 })
            await lifecycle.AppendAsync(Shutdown(releaseId, DateTime.UtcNow));

        await using var reopened = Services(db);
        var reloaded = reopened.GetRequiredService<LibraryViewModel>();
        await reloaded.LoadCommand.ExecuteAsync(null);
        SelectDerelict(reloaded);
        AssertRemovedGroup(reloaded);
        foreach (var releaseId in new long[] { 1, 2 })
            Assert.Equal(2, (await lifecycle.GetForReleaseAsync(releaseId)).Count);
    }

    private static void SelectDerelict(LibraryViewModel library) =>
        library.SelectedBucket = Assert.Single(library.Buckets, bucket => bucket.Key == LibraryBuckets.Derelict);

    private static void SelectGroup(LibraryViewModel library) =>
        library.SelectedTiles = [Assert.Single(library.VisibleTiles, tile => tile.Game.ResolvedWorkId == 1)];

    private static void AssertRemovedGroup(LibraryViewModel library)
    {
        Assert.Null(library.DerelictProblem);
        Assert.Equal(2, Assert.Single(library.VisibleTiles).Game.ResolvedWorkId);
        var restored = Assert.Single(library.AllTiles, tile => tile.Game.ResolvedWorkId == 1);
        Assert.Equal(LibraryBuckets.NeverPlayed, restored.Game.Bucket);
        Assert.Equal(new long[] { 1, 2 }, restored.ReleaseIds.Order());
    }

    private sealed class PendingFeedLoads(ServiceProvider services, FullscreenContext? context = null) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await DrainAsync(services.GetRequiredService<FeedViewModel>());
            if (context is not null) await DrainAsync(context.Feed);
        }

        private static async Task DrainAsync(FeedViewModel feed)
        {
            await (feed.LoadCommand.ExecutionTask ?? Task.CompletedTask);
            await feed.Backfilling;
            await feed.AdditionalShelvesLoading;
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

    private static async Task SeedAsync(TempDatabase db)
    {
        using (var connection = db.Factory.Open())
            connection.Execute("""
                INSERT INTO works (id, name, sort_name) VALUES (1, 'Grouped game', 'Grouped game'), (2, 'Unselected game', 'Unselected game');
                INSERT INTO releases (id, work_id, name, platform) VALUES (1, 1, 'Steam copy', 'windows'), (2, 1, 'Epic copy', 'windows'), (3, 2, 'Unselected game', 'windows');
                INSERT INTO ownerships (id, release_id, store, installed) VALUES (1, 1, 'steam', 0), (2, 2, 'epic', 0), (3, 3, 'gog', 0);
                """);
        var lifecycle = new LifecycleRepository(db.Factory);
        foreach (var releaseId in new long[] { 1, 2, 3 })
            await lifecycle.AppendAsync(Shutdown(releaseId, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)));
    }

    private static LifecycleObservation Shutdown(long releaseId, DateTime observedAt) => new()
    {
        ReleaseId = releaseId,
        Source = "official",
        ObservedAt = observedAt,
        Signals = new() { OfficialShutdownAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
    };
}
