using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views.Fullscreen;
using Winnow.Tests;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Queries;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenDetailsTests
{
    [AvaloniaFact]
    public void Single_copy_achievements_remain_visible_without_an_identity_link()
    {
        var now = DateTime.UtcNow;
        var entry = new CoverageEntry { OwnershipId = 1, ReleaseId = 1, WorkId = 1, Title = "One copy", Store = "steam", PlaytimeMinutes = 60 };
        var coverage = new GameCoverageViewModel(IdentityCoverage.For(1, SameGameResolution.Empty, [entry]),
            new Dictionary<long, string> { [1] = "One copy" }, new Dictionary<long, ReleaseAchievementSummary>
            { [1] = new() { ReleaseId = 1, Total = 20, Unlocked = 5 } });
        using var details = new GameDetailsViewModel(TileFixture.Tile(now, title: "One copy"), "Started", [], now, coverage: coverage);
        Assert.False(coverage.HasCoverage);
        var page = new FullscreenDetailsPage(null!, details, 3);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "Achievements: 5/20 · 25%");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Per_game_history_switches_from_lifetime_to_observed_sessions_with_controller()
    {
        var now = DateTime.UtcNow;
        var tracker = new ActivityTrackerViewModel([], [new Session { OwnershipId = 1, StartedAt = now.AddHours(-2),
            EndedAt = now.AddHours(-1), DurationSeconds = 3600, DetectionMethod = "process" }], null, now.AddHours(-1), 60, now);
        var page = new FullscreenDetailsHistoryPage(null!, tracker);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            page.FocusInitial();
            page.Handle(GamepadButtons.Right);
            page.Handle(GamepadButtons.Accept);
            Dispatcher.UIThread.RunJobs();
            Assert.True(tracker.IsTrackedSessions);
            Assert.Single(tracker.Series.Bars);
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "Only sessions observed by Winnow are shown.");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Long_game_title_expands_the_hero_without_overlapping_section_navigation()
    {
        var now = DateTime.UtcNow;
        using var details = new GameDetailsViewModel(TileFixture.Tile(now, title: string.Join(" ", Enumerable.Repeat("A very long game title", 10))), "Never played", [], now);
        var page = new FullscreenDetailsPage(null!, details);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var layout = Assert.IsType<Grid>(page.Content);
            var hero = layout.Children[0];
            var sections = layout.Children[1];
            Assert.True(hero.Bounds.Height > 320);
            Assert.True(hero.Bounds.Bottom <= sections.Bounds.Top);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Version_chooser_does_not_launch_until_selected_and_uses_the_selected_copy_uri()
    {
        var dispatcher = new RecordingDispatcher();
        var library = new LibraryViewModel(new PreviewLibraryQueryRepository(), new PreviewOwnershipRepository(),
            new PreviewReleaseRepository(), new PreviewWorkRepository(), new PreviewUpdateEventRepository(),
            launcher: new GameLaunchService(dispatcher));
        using var context = new FullscreenContext(library, new FeedViewModel(new PreviewFeedService(), library), PreviewData.Shell);
        var now = DateTime.UtcNow;
        var first = TileFixture.Tile(now).Primary with { OwnershipId = 1, SteamAppId = "10", Installed = true };
        var second = first with { OwnershipId = 2, SteamAppId = "20" };
        var tile = new GameTileViewModel([first, second], TileFixture.Tile(now).Game, "Two copies", now);
        FullscreenPage? choice = null;
        context.PageRequested += page => choice = page;
        context.ChooseVersion(tile);
        Assert.Empty(dispatcher.Uris);
        Assert.NotNull(choice);
        var window = new Window { Width = 1920, Height = 1080, Content = choice };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            choice.FocusInitial();
            choice.Handle(GamepadButtons.Down);
            choice.Handle(GamepadButtons.Accept);
            Assert.Contains("20", Assert.Single(dispatcher.Uris).AbsoluteUri);
            context.Dispose();
            context.Play(tile);
            Assert.Single(dispatcher.Uris);
        }
        finally { window.Close(); }
    }

    private sealed class RecordingDispatcher : IUriDispatcher
    {
        public List<Uri> Uris { get; } = [];
        public Task<bool> OpenAsync(Uri uri) { Uris.Add(uri); return Task.FromResult(true); }
    }

    [AvaloniaFact]
    public void Reload_replaces_the_underlying_detail_and_preserves_local_section_and_open_editor()
    {
        var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        using var view = new FullscreenView(context);
        var window = new Window { Width = 1920, Height = 1080, Content = view };
        var now = DateTime.UtcNow;
        context.Library.Details = new GameDetailsViewModel(TileFixture.Tile(now, title: "Before"), "Started", [], now);
        window.Show();
        try
        {
            context.Push(new FullscreenDetailsPage(context, context.Library.Details, 2));
            var editor = new FullscreenDetailsReadingPage(context, "Open draft", "Keep this page in place.");
            context.Push(editor);
            context.Library.Details = new GameDetailsViewModel(TileFixture.Tile(now, title: "After"), "Started", [], now);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(editor, view.CurrentPage);
            view.Back();
            var refreshed = Assert.IsType<FullscreenDetailsPage>(view.CurrentPage);
            Assert.Equal("After", refreshed.Title);
            Assert.Equal(2, refreshed.SelectedSection);
            view.Back();
            Assert.IsType<FullscreenBrowsePage>(view.CurrentPage);
            Assert.Null(context.Library.Details);
        }
        finally { window.Close(); context.Library.CloseDetailsCommand.Execute(null); }
    }

    [AvaloniaFact]
    public void Pending_session_prompt_opens_on_attach_and_dismisses_through_the_shared_model()
    {
        var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        using var view = new FullscreenView(context);
        var prompt = context.Shared.Library.Journal;
        prompt.DismissCommand.Execute(null);
        prompt.Open(new EndedSession(781, 1, 600), "A finished game");
        Assert.IsType<FullscreenBrowsePage>(view.CurrentPage);
        var window = new Window { Width = 1920, Height = 1080, Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.IsType<FullscreenSessionJournalPage>(view.CurrentPage);
            view.Handle(GamepadButtons.Back);
            Assert.False(prompt.IsOpen);
            Assert.IsType<FullscreenBrowsePage>(view.CurrentPage);
        }
        finally { window.Close(); prompt.DismissCommand.Execute(null); }
    }

    [AvaloniaFact]
    public async Task Ungroup_from_the_expansion_end_requires_confirmation_and_uses_child_identity()
    {
        var changed = new List<long>();
        var row = new ExpansionRowViewModel(10, 99, "Base game", ["STEAM"], "Steam", 30, null);
        var expansions = new GameExpansionsViewModel([], row, child => { changed.Add(child); return Task.CompletedTask; });
        var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        FullscreenPage? confirmation = null;
        context.PageRequested += page => confirmation = page;
        var now = DateTime.UtcNow;
        using var details = new GameDetailsViewModel(TileFixture.Tile(now, title: "Expansion"), "Started", [], now, expansions: expansions);
        var page = new FullscreenDetailsPage(context, details);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            page.Handle(GamepadButtons.Previous);
            Dispatcher.UIThread.RunJobs();
            var ungroup = Assert.Single(page.GetVisualDescendants().OfType<Button>(), button =>
                Avalonia.Automation.AutomationProperties.GetName(button) == row.UngroupAutomationName);
            ungroup.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(changed);
            Assert.NotNull(confirmation);
            window.Content = confirmation;
            Dispatcher.UIThread.RunJobs();
            confirmation.FocusInitial();
            confirmation.Handle(GamepadButtons.Down);
            confirmation.Handle(GamepadButtons.Accept);
            if (expansions.UngroupCommand.ExecutionTask is { } task) await task;
            Assert.Equal([99L], changed);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Opening_details_focuses_play_without_launching_and_bumpers_own_local_sections()
    {
        var now = DateTime.UtcNow;
        using var details = new GameDetailsViewModel(TileFixture.Tile(now, title: "Across the room"), "Never played", [], now);
        var page = new FullscreenDetailsPage(null!, details);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            page.FocusInitial();
            Assert.Equal(0, page.SelectedSection);
            Assert.IsType<Button>(window.FocusManager!.GetFocusedElement());
            var first = (Button)window.FocusManager.GetFocusedElement()!;
            Assert.Equal(details.PrimaryAction?.Label ?? "More", first.Content);
            Assert.True(page.Handle(GamepadButtons.Next));
            Assert.Equal(1, page.SelectedSection);
            Assert.Equal(0, details.SelectedTabIndex);
            Assert.True(page.Handle(GamepadButtons.Previous));
            Assert.Equal(0, page.SelectedSection);
            Assert.True(page.Handle(GamepadButtons.Previous));
            Assert.Equal(3, page.SelectedSection);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Long_description_has_an_explicit_controller_reading_region()
    {
        var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        var page = new FullscreenDetailsReadingPage(context, "Long description", string.Join("\n", Enumerable.Repeat("A readable line", 100)));
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var scroll = Assert.Single(page.GetVisualDescendants().OfType<ScrollViewer>());
            Assert.True(page.Handle(GamepadButtons.Down));
            Assert.True(scroll.Offset.Y > 0);
            Assert.True(page.Handle(GamepadButtons.Up));
            Assert.Equal(0, scroll.Offset.Y);
        }
        finally { window.Close(); }
    }
}
