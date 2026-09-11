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
using Winnow.Data.Repositories;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class ListPromptParityTests
{
    [AvaloniaTheory]
    [InlineData(false, "library")]
    [InlineData(false, "details")]
    [InlineData(false, "feed")]
    [InlineData(true, "library")]
    [InlineData(true, "details")]
    [InlineData(true, "feed")]
    public async Task Existing_choices_and_new_list_action_remain_available_after_a_failed_save(bool fullscreen, string origin)
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        var lists = new GameListRepository(db.Factory);
        var library = new LibraryViewModel(new LibraryQueryRepository(db.Factory), new OwnershipRepository(db.Factory),
            new ReleaseRepository(db.Factory), new WorkRepository(db.Factory), new UpdateEventRepository(db.Factory), lists: lists);
        await library.LoadCommand.ExecuteAsync(null);
        var tile = library.AllTiles[0];
        using var feed = new FeedViewModel(new SingleGameFeed(tile), library, lists: library.Lists);
        await feed.LoadCommand.ExecuteAsync(null);
        using var context = new FullscreenContext(library, feed, PreviewData.Shell);
        FullscreenPage? requested = null;
        context.PageRequested += page => requested = page;
        var backs = 0;
        context.BackRequested += () => backs++;
        if (origin == "feed") feed.Shelves[0].Cards[0].AddToListCommand.Execute(null);
        else if (origin == "details")
        {
            await library.OpenDetailsCommand.ExecuteAsync(tile);
            library.Details!.AddToListCommand!.Execute(null);
        }
        else { library.SelectedTiles = [tile]; library.BeginAddToListCommand.Execute(null); }
        var prompt = (origin == "feed" ? feed.ListPrompt : library.Prompt)!;
        Assert.Single(prompt.Choices);
        var page = Assert.IsType<FullscreenPromptPage>(requested);
        var window = new Window { Width = 1920, Height = 1080, Content = fullscreen
            ? page : new FeedListPromptView { DataContext = prompt } };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var input = Assert.Single(window.GetVisualDescendants().OfType<TextBox>());
            Assert.Equal("New list name", AutomationProperties.GetName(input));
            input.Text = "Second list";
            Dispatcher.UIThread.RunJobs();
            using (var connection = db.Factory.Open())
                connection.Execute("CREATE TRIGGER reject_list BEFORE INSERT ON lists BEGIN SELECT RAISE(ABORT, 'fixture failure'); END;");
            Activate(window, page, fullscreen, "New list");
            await prompt.ConfirmCommand.ExecutionTask!;
            Dispatcher.UIThread.RunJobs();
            Assert.False(prompt.IsCompleted);
            Assert.True(prompt.HasProblem);
            Assert.Equal("Second list", prompt.Text);
            Assert.Equal(0, backs);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == prompt.Problem && text.IsEffectivelyVisible);
            using (var connection = db.Factory.Open()) connection.Execute("DROP TRIGGER reject_list;");
            Activate(window, page, fullscreen, "New list");
            await prompt.ConfirmCommand.ExecutionTask!;
            Dispatcher.UIThread.RunJobs();
            Assert.True(prompt.IsCompleted);
            Assert.Null(prompt.Problem);
            Assert.Null(origin == "feed" ? feed.ListPrompt : library.Prompt);
            Assert.Equal(fullscreen ? 1 : 0, backs);
            var created = Assert.Single(await lists.GetAllAsync(), list => list.Name == "Second list");
            Assert.Equal(tile.ReleaseId, Assert.Single(await lists.GetItemsAsync(created.Id)).ReleaseId);
        }
        finally { window.Close(); page.Dispose(); library.CloseDetailsCommand.Execute(null); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pending_prompt_disables_conflicting_actions_and_disposed_pages_do_not_navigate(bool disposeBeforeCompletion)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        var prompt = new ActionPromptViewModel("Name the list", "Save", async _ => { writes++; await gate.Task; }, () => { },
            inputWatermark: "List name", initialText: "Later");
        using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        using var page = new FullscreenPromptPage(context, prompt);
        var backs = 0;
        context.BackRequested += () => backs++;
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Activate(window, page, true, "Save");
            Assert.True(prompt.IsBusy);
            Assert.All(window.GetVisualDescendants().OfType<Button>(), button => Assert.False(button.IsEnabled));
            Assert.True(page.Handle(GamepadButtons.Back));
            await prompt.ConfirmCommand.ExecuteAsync(null);
            Assert.Equal(1, writes);
            if (disposeBeforeCompletion) page.Dispose();
            gate.SetResult();
            // The second guarded invocation must not replace the operation being awaited.
            for (var i = 0; i < 100 && prompt.IsBusy; i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.False(prompt.IsBusy);
            Assert.True(prompt.IsCompleted);
            Assert.Equal(disposeBeforeCompletion ? 0 : 1, backs);
        }
        finally { gate.TrySetResult(); window.Close(); }
    }

    private static void Activate(Window window, FullscreenPage page, bool fullscreen, string label)
    {
        var button = Assert.Single(window.GetVisualDescendants().OfType<Button>(), button =>
            button.IsEffectivelyVisible && (AutomationProperties.GetName(button) == label || Equals(button.Content, label)));
        Assert.True(button.IsEnabled);
        Assert.True(button.Focus());
        if (fullscreen) page.Handle(GamepadButtons.Accept);
        else
        {
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        }
    }

    private sealed class SingleGameFeed(GameTileViewModel tile) : IFeedService
    {
        public Task<FeedSnapshot> GetShelvesAsync(CancellationToken ct = default) => Task.FromResult(new FeedSnapshot(
            [new("ready", "Ready", "Ready to play", [new(tile.OwnershipId, tile.ReleaseId, tile.Title, "Ready to play")])],
            1, FeedConfidence.Established, false));
        public Task RecordSurfacedAsync(long releaseId, string shelfId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<FeedVerdictOutcome> RecordVerdictAsync(long releaseId, FeedVerdictKind kind, CancellationToken ct = default) => Task.FromResult(FeedVerdictOutcome.NotSaved);
        public Task<bool> RevokeVerdictAsync(long releaseId, FeedVerdictKind kind, CancellationToken ct = default) => Task.FromResult(false);
        public Task<IReadOnlyList<FeedVerdictRecord>> GetHistoryAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<FeedVerdictRecord>>([]);
    }
}
