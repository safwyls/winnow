using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.ViewModels.Lists;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenAccessibilityTests
{
    [AvaloniaTheory]
    [InlineData("Appearance")]
    [InlineData("Controller")]
    [InlineData("Library")]
    [InlineData("Platforms")]
    [InlineData("Metadata & artwork")]
    [InlineData("Plugins")]
    [InlineData("Application")]
    [InlineData("Steam key")]
    [InlineData("Search")]
    [InlineData("Filters")]
    public async Task Rendered_settings_and_filter_controls_expose_names_and_controller_routes(string screen)
    {
        using var fixture = new Fixture();
        var context = fixture.Context;
        await context.Library.LoadCommand.ExecuteAsync(null);
        using FullscreenPage page = screen switch
        {
            "Steam key" => new FullscreenSteamApiKeyPage(context),
            "Search" => new FullscreenBrowseSearchPage(context),
            "Filters" => new FullscreenBrowseFiltersPage(context),
            _ => new FullscreenSettingsPage(context, screen)
        };
        var window = Show(page);
        try
        {
            if (page is FullscreenSettingsPage settings)
            {
                await settings.PendingLibraryRefresh;
                await settings.PendingPlatformRefresh;
                await settings.PendingPluginRefresh;
                Dispatcher.UIThread.RunJobs();
            }
            AssertNamesAndStates(page);
            AssertControllerReachability(window, page);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task A_combined_prompt_keeps_names_and_focus_routes_through_empty_busy_and_error_states()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var prompt = new ActionPromptViewModel("Add to list", "Create list", async _ => { await gate.Task; throw new IOException("injected"); }, () => { },
            inputWatermark: "List name", choices: [new(GameList.Manual("Existing list") with { Id = 1 })], choose: _ => Task.CompletedTask);
        using var fixture = new Fixture();
        var context = fixture.Context;
        using var page = new FullscreenPromptPage(context, prompt);
        var window = Show(page);
        try
        {
            AssertNamesAndStates(page);
            AssertControllerReachability(window, page);
            var confirm = ButtonNamed(page, "Create list");
            Assert.False(ControlAutomationPeer.CreatePeerForElement(confirm)!.IsEnabled());
            Assert.Single(page.GetVisualDescendants().OfType<TextBox>()).Text = "New list";
            Dispatcher.UIThread.RunJobs();
            AssertControllerReachability(window, page);
            Assert.True(confirm.Focus()); page.Handle(GamepadButtons.Accept);
            Assert.True(prompt.IsBusy);
            AssertNamesAndStates(page);
            Assert.All(Interactive(page), control => Assert.False(ControlAutomationPeer.CreatePeerForElement(control)!.IsEnabled()));
            Assert.True(page.Handle(GamepadButtons.Back));
            gate.TrySetResult();
            await prompt.ConfirmCommand.ExecutionTask!;
            Dispatcher.UIThread.RunJobs();
            Assert.True(prompt.HasProblem);
            AssertNamesAndStates(page);
            AssertControllerReachability(window, page);
            Assert.Contains(Peers(page), peer => peer.GetName() == prompt.Problem);
        }
        finally { gate.TrySetResult(); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Journal_editors_name_the_field_expose_validation_and_reach_the_keyboard_action(bool details)
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        var repository = new SessionRepository(db.Factory);
        var id = await repository.InsertAsync(new Session { OwnershipId = 1, StartedAt = DateTime.UtcNow, DetectionMethod = "manual" });
        using var services = new ServiceCollection().AddSingleton<ISessionRepository>(repository).BuildServiceProvider();
        using var fixture = new Fixture(services);
        var context = fixture.Context;
        var entry = new JournalEntryViewModel(id, null, repository);
        using FullscreenPage page = details ? new FullscreenDetailsJournalPage(context, entry)
            : new FullscreenSessionNotePage(context, id, "A session", null, _ => { });
        var window = Show(page);
        try
        {
            AssertNamesAndStates(page);
            AssertControllerReachability(window, page);
            var field = Assert.Single(page.GetVisualDescendants().OfType<TextBox>());
            Assert.Equal("Journal note", ControlAutomationPeer.CreatePeerForElement(field)!.GetName());
            TextBox? keyboardTarget = null;
            context.TextRequested += target => keyboardTarget = target;
            Assert.True(ButtonNamed(page, "Edit note").Focus());
            page.Handle(GamepadButtons.Accept);
            Assert.Same(field, keyboardTarget);
            Assert.True(ButtonNamed(page, "Save").Focus());
            page.Handle(GamepadButtons.Accept);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(Peers(page), peer => peer.GetName() == GameDetailsCopy.JournalEmptyEditProblem);
            AssertNamesAndStates(page);
            AssertControllerReachability(window, page);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Back_from_a_dynamic_modal_restores_the_invoking_control_in_the_real_shell()
    {
        using var fixture = new Fixture();
        var context = fixture.Context;
        using var shell = new FullscreenView(context);
        var window = Show(shell);
        try
        {
            var settings = new FullscreenSettingsPage(context, "Controller");
            context.Push(settings); Dispatcher.UIThread.RunJobs();
            settings.FocusInitial(); settings.Handle(GamepadButtons.Down);
            var invoking = Assert.IsAssignableFrom<Control>(window.FocusManager!.GetFocusedElement());
            context.ShowActions("Choose an action", [new("Unavailable", () => { }, false), new("Cancel", () => { })]);
            Dispatcher.UIThread.RunJobs();
            Assert.NotSame(settings, shell.CurrentPage);
            AssertNamesAndStates(shell.CurrentPage);
            AssertControllerReachability(window, shell.CurrentPage);
            Assert.Equal("Cancel", ControlAutomationPeer.CreatePeerForElement(Assert.IsAssignableFrom<Control>(window.FocusManager.GetFocusedElement()))!.GetName());
            shell.Handle(GamepadButtons.Back); Dispatcher.UIThread.RunJobs();
            Assert.Same(settings, shell.CurrentPage);
            Assert.Same(invoking, window.FocusManager.GetFocusedElement());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Controller_keyboard_exposes_its_keys_and_returns_focus_to_the_original_field()
    {
        using var fixture = new Fixture();
        var context = fixture.Context;
        using var shell = new FullscreenView(context);
        var window = Show(shell);
        try
        {
            var search = new FullscreenBrowseSearchPage(context);
            context.Push(search); Dispatcher.UIThread.RunJobs();
            var field = Assert.Single(search.GetVisualDescendants().OfType<TextBox>());
            Assert.True(ButtonNamed(search, "Enter search").Focus());
            shell.Handle(GamepadButtons.Accept); Dispatcher.UIThread.RunJobs();
            var keyboard = Assert.Single(shell.GetVisualDescendants().OfType<GamepadKeyboardView>());
            var exposed = Peers(keyboard).ToHashSet();
            foreach (var key in keyboard.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible))
            {
                var peer = ControlAutomationPeer.CreatePeerForElement(key)!;
                Assert.False(string.IsNullOrWhiteSpace(peer.GetName()));
                Assert.Contains(peer, exposed);
            }
            shell.Handle(GamepadButtons.Accept); // The initial character key types into the original field.
            Assert.False(string.IsNullOrEmpty(field.Text));
            shell.Handle(GamepadButtons.Back); Dispatcher.UIThread.RunJobs();
            Assert.Empty(shell.GetVisualDescendants().OfType<GamepadKeyboardView>());
            Assert.Same(search, shell.CurrentPage);
            Assert.Same(field, window.FocusManager!.GetFocusedElement());
        }
        finally { window.Close(); }
    }

    private static Window Show(Control content)
    {
        var window = new Window { Width = 1920, Height = 1080, Content = content };
        window.Show(); Dispatcher.UIThread.RunJobs();
        return window;
    }

    private sealed class Fixture : IDisposable
    {
        public FullscreenContext Context { get; }
        public Fixture(IServiceProvider? services = null)
        {
            var releases = new PreviewReleaseRepository();
            var links = new PreviewIdentityLinkRepository();
            var refusals = new PreviewExpansionRefusalRepository();
            var library = new LibraryViewModel(new PreviewLibraryQueryRepository(), new PreviewOwnershipRepository(), releases,
                new PreviewWorkRepository(), new PreviewUpdateEventRepository());
            var feed = new FeedViewModel(new PreviewFeedService(), library);
            var merges = new MergeQueueViewModel(new PreviewMergeCandidateRepository(), releases, new PreviewWorkRepository(),
                links, new PreviewOwnershipRepository(), new LibraryExpansionScan(releases, links, refusals), refusals, new PreviewLibraryQueryRepository());
            var shell = new MainWindowViewModel(library, merges, new StoresViewModel(new PreviewStoreConnections()),
                new AppearanceViewModel(new ThemeService()), feed, new AccountStatsViewModel(new PreviewAccountStatsRepository()), new LibrarySettingsViewModel());
            Context = new(library, feed, shell, services);
        }
        public void Dispose() { Context.Dispose(); Context.Shared.MergeQueue.Dispose(); Context.Feed.Dispose(); Context.Library.Dispose(); }
    }

    private static Button ButtonNamed(Control page, string name) => Assert.Single(page.GetVisualDescendants().OfType<Button>(),
        button => button.IsEffectivelyVisible && ControlAutomationPeer.CreatePeerForElement(button)!.GetName() == name);

    private static Control[] Interactive(Control page) => page.GetVisualDescendants().OfType<Control>()
        .Where(control => control.IsEffectivelyVisible && control.Focusable && control is Button or TextBox or ComboBox or Slider).ToArray();

    private static void AssertNamesAndStates(Control page)
    {
        var controls = Interactive(page);
        Assert.NotEmpty(controls);
        var exposed = Peers(page).ToHashSet();
        foreach (var control in controls)
        {
            var peer = ControlAutomationPeer.CreatePeerForElement(control)!;
            Assert.True(!string.IsNullOrWhiteSpace(peer.GetName()), $"{page.GetType().Name}: unnamed {control.GetType().Name} ({control.Name ?? "no id"})");
            Assert.Contains(peer, exposed);
            Assert.Equal(control.IsEffectivelyEnabled, peer.IsEnabled());
        }
    }

    private static IEnumerable<AutomationPeer> Peers(Control page)
    {
        var queue = new Queue<AutomationPeer>();
        queue.Enqueue(ControlAutomationPeer.CreatePeerForElement(page)!);
        var visited = new HashSet<AutomationPeer>();
        while (queue.TryDequeue(out var peer))
        {
            if (!visited.Add(peer)) continue;
            yield return peer;
            foreach (var child in peer.GetChildren()) queue.Enqueue(child);
        }
    }

    private static void AssertControllerReachability(Window window, FullscreenPage page)
    {
        var buttons = Interactive(page).OfType<Button>().Where(button => button.IsEffectivelyEnabled).ToArray();
        if (buttons.Length == 0) return;
        page.FocusInitial(); Dispatcher.UIThread.RunJobs();
        var first = Assert.IsAssignableFrom<Control>(window.FocusManager!.GetFocusedElement());
        var visited = new HashSet<Control> { first };
        var queue = new Queue<Control>(); queue.Enqueue(first);
        while (queue.TryDequeue(out var origin))
        {
            foreach (var direction in new[] { GamepadButtons.Up, GamepadButtons.Down, GamepadButtons.Left, GamepadButtons.Right })
            {
                Assert.True(origin.Focus());
                page.Handle(direction); Dispatcher.UIThread.RunJobs();
                var next = Assert.IsAssignableFrom<Control>(window.FocusManager.GetFocusedElement());
                Assert.True(next.IsEffectivelyEnabled);
                if (visited.Add(next)) queue.Enqueue(next);
            }
        }
        Assert.All(buttons, button => Assert.True(visited.Contains(button),
            $"{page.GetType().Name}: controller cannot reach {ControlAutomationPeer.CreatePeerForElement(button)!.GetName()}"));
    }
}
