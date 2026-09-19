using Avalonia;
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
using Winnow.Core.Domain;
using Winnow.Core.Lifecycle;
using Winnow.Core.Queries;
using Winnow.Data;
using Winnow.Data.Repositories;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class LibraryMultiSelectionTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Reload_sort_and_view_switch_preserve_selection_and_filtering_drops_hidden_games(bool grid)
    {
        await using var fixture = await Fixture.CreateAsync(grid, derelict: true);
        fixture.Click(1, control: true);
        fixture.Click(2, control: true);
        await fixture.Library.LoadCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        fixture.AssertSelection(1, 2);

        fixture.Library.SortByTitleCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        fixture.AssertSelection(1, 2);
        if (grid) fixture.Library.ShowListViewCommand.Execute(null);
        else fixture.Library.ShowGridViewCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        fixture.AssertSelection(1, 2);

        fixture.Library.SearchText = "Bravo";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, Assert.Single(fixture.Library.VisibleTiles).Game.ResolvedWorkId);
        fixture.AssertSelection(2);
        fixture.Library.SearchText = "";
        Dispatcher.UIThread.RunJobs();
        fixture.AssertSelection(2);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Ctrl_click_toggles_selection_without_opening_details_and_context_click_preserves_it(bool grid)
    {
        await using var fixture = await Fixture.CreateAsync(grid, derelict: true);
        fixture.Click(1, control: true);
        fixture.Click(2, control: true);
        fixture.AssertSelection(1, 2);
        Assert.False(fixture.Library.IsDetailsOpen);

        fixture.Click(1, control: true);
        fixture.AssertSelection(2);
        fixture.Click(1, control: true);
        fixture.AssertSelection(1, 2);
        Assert.False(fixture.Library.IsDetailsOpen);

        fixture.Click(1, right: true);
        Assert.True(fixture.Menu.IsOpen);
        fixture.AssertSelection(1, 2);
        fixture.Menu.Close();
        Dispatcher.UIThread.RunJobs();

        fixture.Click(3, right: true);
        fixture.AssertSelection(3);
        fixture.Menu.Close();
    }

    [AvaloniaTheory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task Context_action_updates_every_selected_game_and_leaves_unselected_game(bool grid, bool derelict)
    {
        await using var fixture = await Fixture.CreateAsync(grid, derelict);
        fixture.Click(1, control: true);
        fixture.Click(2, control: true);
        fixture.Click(1, right: true);
        fixture.AssertSelection(1, 2);
        Assert.True(fixture.Menu.IsOpen);
        var action = Assert.Single(fixture.Menu.Items.OfType<MenuItem>(), item =>
            Equals(item.Header, derelict ? "Remove from Derelict" : "Mark as read"));
        Assert.True(action.IsVisible);
        Assert.True(action.Command!.CanExecute(action.CommandParameter));
        action.Command.Execute(action.CommandParameter);
        await (derelict ? fixture.Library.RemoveSelectionFromDerelictCommand.ExecutionTask!
            : fixture.Library.MarkSelectionAsReadCommand.ExecutionTask!);
        fixture.Menu.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(3, Assert.Single(fixture.Library.VisibleTiles).Game.ResolvedWorkId);
        fixture.AssertSelection();
        foreach (var id in new long[] { 1, 2 })
        {
            var changed = Assert.Single(fixture.Library.AllTiles, tile => tile.Game.ResolvedWorkId == id);
            Assert.NotEqual(derelict ? LibraryBuckets.Derelict : LibraryBuckets.StaleButPatched, changed.Game.Bucket);
            if (!derelict) Assert.False(changed.HasUnread);
        }
        await fixture.Library.LoadCommand.ExecuteAsync(null);
        Assert.Equal(3, Assert.Single(fixture.Library.VisibleTiles).Game.ResolvedWorkId);
    }

    private sealed class Fixture(TempDatabase database, ServiceProvider services, MainWindow window,
        LibraryViewModel library) : IAsyncDisposable
    {
        public LibraryViewModel Library { get; } = library;
        public ContextMenu Menu => window.GetVisualDescendants().OfType<Panel>()
            .Select(panel => panel.ContextMenu).Single(menu => menu is not null)!;

        public static async Task<Fixture> CreateAsync(bool grid, bool derelict)
        {
            var database = new TempDatabase();
            using (var connection = database.Factory.Open())
                connection.Execute("""
                    INSERT INTO works (id, name, sort_name) VALUES (1, 'Alpha', 'Alpha'), (2, 'Bravo', 'Bravo'), (3, 'Charlie', 'Charlie');
                    INSERT INTO releases (id, work_id, name, platform) VALUES (1, 1, 'Alpha', 'windows'), (2, 2, 'Bravo', 'windows'), (3, 3, 'Charlie', 'windows');
                    INSERT INTO ownerships (id, release_id, store, installed) VALUES (1, 1, 'steam', 0), (2, 2, 'steam', 0), (3, 3, 'steam', 0);
                    INSERT INTO play_records (ownership_id, playtime_minutes, last_played_at, source, observed_at) VALUES
                        (1, 600, '2024-01-01', 'steam_local', '2026-09-01'), (2, 600, '2024-01-01', 'steam_local', '2026-09-01'), (3, 600, '2024-01-01', 'steam_local', '2026-09-01');
                    INSERT INTO update_events (release_id, kind, occurred_at) VALUES
                        (1, 'build_push', '2026-06-01'), (1, 'announcement', '2026-06-02'),
                        (2, 'build_push', '2026-06-01'), (2, 'announcement', '2026-06-02'),
                        (3, 'build_push', '2026-06-01'), (3, 'announcement', '2026-06-02');
                    """);
            if (derelict)
            {
                var lifecycle = new LifecycleRepository(database.Factory);
                foreach (var releaseId in new long[] { 1, 2, 3 })
                    await lifecycle.AppendAsync(new()
                    {
                        ReleaseId = releaseId,
                        Source = "official",
                        ObservedAt = DateTime.UtcNow,
                        Signals = new() { OfficialShutdownAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                    });
            }
            var collection = new ServiceCollection();
            collection.AddLogging();
            collection.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            Program.ConfigureServices(collection, new(database.DatabasePath + "-data", database.DatabasePath, DataMigrationOutcome.Overridden));
            collection.AddSingleton<ISqliteConnectionFactory>(database.Factory);
            var services = collection.BuildServiceProvider();
            var shell = services.GetRequiredService<MainWindowViewModel>();
            var library = shell.Library;
            await library.LoadCommand.ExecuteAsync(null);
            var window = new MainWindow { DataContext = shell, Width = 1280, Height = 800 };
            window.Show();
            Assert.True(await window.StartupLibraryReady);
            shell.ShowLibraryCommand.Execute(null);
            library.SelectedBucket = Assert.Single(library.Buckets, bucket =>
                bucket.Key == (derelict ? LibraryBuckets.Derelict : LibraryBuckets.StaleButPatched));
            if (grid) library.ShowGridViewCommand.Execute(null);
            else library.ShowListViewCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, library.VisibleTiles.Count);
            return new(database, services, window, library);
        }

        public void Click(long id, bool control = false, bool right = false)
        {
            var target = Target(id);
            var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 3), window)!.Value;
            var button = right ? MouseButton.Right : MouseButton.Left;
            var modifiers = control ? RawInputModifiers.Control : RawInputModifiers.None;
            window.MouseMove(point, modifiers);
            window.MouseDown(point, button, modifiers);
            window.MouseUp(point, button, modifiers);
            Dispatcher.UIThread.RunJobs();
        }

        private Control Target(long id) => Library.IsGridView
            ? Assert.Single(window.GetVisualDescendants().OfType<GameTileView>(), control =>
                control.IsEffectivelyVisible && control.DataContext is GameTileViewModel tile && tile.Game.ResolvedWorkId == id)
            : Assert.Single(window.FindControl<ListBox>("ListRows")!.GetVisualDescendants().OfType<ListBoxItem>(), control =>
                control.IsEffectivelyVisible && control.DataContext is GameTileViewModel tile && tile.Game.ResolvedWorkId == id);

        public void AssertSelection(params long[] ids)
        {
            Assert.Equal(ids.Order(), Library.SelectedTiles.Select(tile => tile.Game.ResolvedWorkId).Order());
            Assert.Equal(ids.Length, Library.SelectedCount);
            foreach (var tile in Library.VisibleTiles)
            {
                var selected = ids.Contains(tile.Game.ResolvedWorkId);
                Assert.Equal(selected, tile.IsSelected);
                if (Library.IsGridView)
                    Assert.Equal(selected, ((GameTileView)Target(tile.Game.ResolvedWorkId))
                        .FindControl<Border>("InteractionRing")!.Classes.Contains("selected"));
                else Assert.Equal(selected, ((ListBoxItem)Target(tile.Game.ResolvedWorkId)).IsSelected);
            }
        }

        public async ValueTask DisposeAsync()
        {
            Menu.Close();
            window.Close();
            var feed = services.GetRequiredService<FeedViewModel>();
            await (feed.LoadCommand.ExecutionTask ?? Task.CompletedTask);
            await feed.Backfilling;
            await feed.AdditionalShelvesLoading;
            await services.DisposeAsync();
            database.Dispose();
        }
    }
}
