using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dapper;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.ViewModels.Lists;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class ListWriteParityTests
{
    [AvaloniaTheory]
    [InlineData(false, "latest")]
    [InlineData(true, "latest")]
    [InlineData(false, "failure")]
    [InlineData(true, "failure")]
    [InlineData(false, "refresh")]
    [InlineData(true, "refresh")]
    [InlineData(false, "compensation_failure")]
    [InlineData(true, "compensation_failure")]
    public async Task Pending_membership_preserves_latest_intent_and_reports_the_committed_result(bool fullscreen, string scenario)
    {
        using var fixture = new Fixture();
        await fixture.Library.LoadCommand.ExecuteAsync(null);
        await fixture.Library.OpenDetailsCommand.ExecuteAsync(fixture.Library.AllTiles.Single(tile => tile.ReleaseId == 2));
        var details = fixture.Library.Details!;
        details.SelectedTabIndex = 4;
        var row = Assert.Single(details.Lists!.Rows);
        using var context = new FullscreenContext(fixture.Library, PreviewData.Feed, PreviewData.Shell);
        using var page = new FullscreenDetailsPage(context, details, selectedSection: 3);
        var window = new Window { Width = 1920, Height = 1080, Content = fullscreen ? page : new GameDetailsView { DataContext = details } };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            fixture.Repository.HoldNextAppend();
            if (scenario == "failure") fixture.Sql("CREATE TRIGGER fail_list BEFORE INSERT ON list_items BEGIN SELECT RAISE(ABORT, 'injected'); END;");
            if (scenario == "compensation_failure") fixture.Sql("CREATE TRIGGER fail_list BEFORE DELETE ON list_items BEGIN SELECT RAISE(ABORT, 'injected'); END;");
            Activate();
            await fixture.Repository.Entered.Task;
            Dispatcher.UIThread.RunJobs();
            Assert.True(row.IsBusy);
            AssertText(GameListsCopy.Saving);
            Assert.DoesNotContain(2L, (await fixture.Repository.Inner.GetItemsAsync(1)).Select(item => item.ReleaseId));
            Task? refresh = null;
            if (scenario == "refresh")
            {
                refresh = fixture.Library.LoadCommand.ExecuteAsync(null);
                Assert.False(refresh.IsCompleted);
            }
            if (scenario is "latest" or "compensation_failure") Activate();
            fixture.Repository.Release();
            await row.Pending;
            if (refresh is not null) await refresh;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(row, Assert.Single(details.Lists.Rows));
            Assert.False(row.IsBusy);
            var committed = scenario is "refresh" or "compensation_failure";
            Assert.Equal(committed, row.IsMember);
            Assert.Equal(committed, (await fixture.Repository.Inner.GetItemsAsync(1)).Any(item => item.ReleaseId == 2));
            Assert.Equal(committed, row.List.ReleaseIds.Contains(2));
            if (scenario is "failure" or "compensation_failure")
            {
                AssertText(GameListsCopy.SaveFailed);
                fixture.Sql("DROP TRIGGER fail_list;");
                Activate();
                await row.Pending;
                Dispatcher.UIThread.RunJobs();
                Assert.Null(row.Problem);
                Assert.Equal(!committed, row.IsMember);
                Assert.Equal(!committed, (await fixture.Repository.Inner.GetItemsAsync(1)).Any(item => item.ReleaseId == 2));
            }
            else Assert.Null(row.Problem);
        }
        finally { fixture.Repository.Release(); window.Close(); }

        void Activate()
        {
            var control = Assert.Single(window.GetVisualDescendants().OfType<Control>(), item =>
                item.IsEffectivelyVisible && (fullscreen ? item is Button : item is CheckBox) && AutomationProperties.GetName(item) == row.AutomationName);
            Assert.True(control.Focus());
            if (fullscreen) page.Handle(GamepadButtons.Accept);
            else { window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None); }
            Dispatcher.UIThread.RunJobs();
        }
        void AssertText(string text) => Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), item => item.IsEffectivelyVisible && item.Text == text);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_failed_list_action_keeps_the_open_list_and_shows_a_retry_message(bool fullscreen)
    {
        using var fixture = new Fixture();
        await fixture.Library.LoadCommand.ExecuteAsync(null);
        var list = Assert.Single(fixture.Library.Lists.Lists);
        fixture.Library.OpenListCommand.Execute(list);
        fixture.Library.SelectedTiles = [fixture.Library.VisibleTiles.First()];
        using var context = new FullscreenContext(fixture.Library, PreviewData.Feed, PreviewData.Shell);
        using var page = new FullscreenBrowsePage(context, feed: false);
        var window = new Window { Width = 1920, Height = 1080, Content = fullscreen ? page : new ActionBarView { DataContext = fixture.Library } };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            fixture.Sql("CREATE TRIGGER fail_list BEFORE DELETE ON list_items BEGIN SELECT RAISE(ABORT, 'injected'); END;");
            await fixture.Library.RemoveFromOpenListCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(list, fixture.Library.Lists.Open);
            Assert.Equal(2, list.ReleaseIds.Count);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), item => item.IsEffectivelyVisible && item.Text == GameListsCopy.SaveFailed);
        }
        finally { window.Close(); }
    }

    private sealed class Fixture : IDisposable
    {
        public TempDatabase Database { get; } = new();
        public DelayedLists Repository { get; }
        public LibraryViewModel Library { get; }
        public Fixture()
        {
            LibraryReadFixtures.Seed(Database, 3);
            Repository = new(new GameListRepository(Database.Factory));
            Library = new(new LibraryQueryRepository(Database.Factory), new OwnershipRepository(Database.Factory), new ReleaseRepository(Database.Factory),
                new WorkRepository(Database.Factory), new UpdateEventRepository(Database.Factory), lists: Repository);
        }
        public void Sql(string sql) { using var connection = Database.Factory.Open(); connection.Execute(sql); }
        public void Dispose() { Library.Dispose(); Database.Dispose(); }
    }

    private sealed class DelayedLists(IGameListRepository inner) : IGameListRepository
    {
        private TaskCompletionSource? _release;
        public IGameListRepository Inner => inner;
        public TaskCompletionSource Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void HoldNextAppend() { Entered = new(TaskCreationOptions.RunContinuationsAsynchronously); _release = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        public void Release() => _release?.TrySetResult();
        public async Task<IReadOnlyList<long>> AppendItemsAsync(long id, IReadOnlyList<long> items, CancellationToken ct = default)
        {
            if (_release is { } gate) { Entered.TrySetResult(); await gate.Task; _release = null; }
            return await inner.AppendItemsAsync(id, items, ct);
        }
        public Task<long> CreateManualAsync(string name, IReadOnlyList<long> items, CancellationToken ct = default) => inner.CreateManualAsync(name, items, ct);
        public Task<IReadOnlyList<long>> RemoveItemsAsync(long id, IReadOnlyList<long> items, CancellationToken ct = default) => inner.RemoveItemsAsync(id, items, ct);
        public Task<long> InsertAsync(GameList list, CancellationToken ct = default) => inner.InsertAsync(list, ct);
        public Task<GameList?> GetAsync(long id, CancellationToken ct = default) => inner.GetAsync(id, ct);
        public Task<IReadOnlyList<GameList>> GetAllAsync(CancellationToken ct = default) => inner.GetAllAsync(ct);
        public Task<IReadOnlyList<ListItem>> GetAllItemsAsync(CancellationToken ct = default) => inner.GetAllItemsAsync(ct);
        public Task<bool> RenameAsync(long id, string name, string? description, CancellationToken ct = default) => inner.RenameAsync(id, name, description, ct);
        public Task<bool> SetFilterAsync(long id, LibraryFilter filter, CancellationToken ct = default) => inner.SetFilterAsync(id, filter, ct);
        public Task<bool> DeleteAsync(long id, CancellationToken ct = default) => inner.DeleteAsync(id, ct);
        public Task AddItemAsync(ListItem item, CancellationToken ct = default) => inner.AddItemAsync(item, ct);
        public Task<int> AppendItemAsync(long id, long release, CancellationToken ct = default) => inner.AppendItemAsync(id, release, ct);
        public Task<IReadOnlyList<ListItem>> GetItemsAsync(long id, CancellationToken ct = default) => inner.GetItemsAsync(id, ct);
        public Task RemoveItemAsync(long id, long release, CancellationToken ct = default) => inner.RemoveItemAsync(id, release, ct);
        public Task<IReadOnlyList<long>> ReorderAsync(long id, IReadOnlyList<long> order, CancellationToken ct = default) => inner.ReorderAsync(id, order, ct);
        public Task<IReadOnlyList<GameListMembership>> GetMembershipForGameAsync(long id, CancellationToken ct = default) => inner.GetMembershipForGameAsync(id, ct);
        public Task<IReadOnlyList<long>> GetMemberWorkIdsAsync(long id, CancellationToken ct = default) => inner.GetMemberWorkIdsAsync(id, ct);
    }
}
