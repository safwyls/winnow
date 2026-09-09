using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
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
using Winnow.Core.Repositories;
using Winnow.Covers;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenDetailsTests
{
    [AvaloniaTheory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Cinematic_details_lease_saved_landscape_across_the_canvas_and_release_on_close(bool userBackground, bool longTitle)
    {
        // An original geometric landscape exercises real bitmap decoding/display without live library art.
        using var pixels = new RenderTargetBitmap(new PixelSize(1600, 900));
        using (var draw = pixels.CreateDrawingContext())
        {
            draw.DrawRectangle(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = [new GradientStop(Color.Parse("#203E60"), 0), new GradientStop(Color.Parse("#D7A37C"), .55),
                    new GradientStop(Color.Parse("#17434B"), 1)]
            }, null, new Rect(0, 0, 1600, 900));
            draw.DrawEllipse(Brushes.Wheat, null, new Point(1200, 210), 70, 70);
            draw.DrawGeometry(new SolidColorBrush(Color.Parse("#244954")), null,
                Geometry.Parse("M 0,650 L 350,290 L 610,570 L 990,180 L 1420,560 L 1600,390 L 1600,900 L 0,900 Z"));
            draw.DrawGeometry(new SolidColorBrush(Color.Parse("#102D32")), null,
                Geometry.Parse("M 0,760 L 260,550 L 600,760 L 1050,510 L 1330,700 L 1600,580 L 1600,900 L 0,900 Z"));
        }
        var leases = new DetailLeases(new CoverArt(pixels, pixels));
        var work = new Work { Id = 1, Name = longTitle ? "A distant shore: the journey beyond the mountains and the forgotten coast" : "A distant shore",
            Summary = string.Join(" ", Enumerable.Repeat("Explore the mountain coast and find your way home.", 12)),
            BackgroundUrl = userBackground ? UserArtRef.Format("landscape") : null };
        var images = new DetailImages();
        using var services = new ServiceCollection().AddSingleton<ICoverLeases>(leases)
            .AddSingleton<IWorkRepository>(new DetailWorks(work)).AddSingleton<IWorkImageRepository>(images).BuildServiceProvider();
        var library = new LibraryViewModel(new PreviewLibraryQueryRepository(), new PreviewOwnershipRepository(),
            new PreviewReleaseRepository(), new PreviewWorkRepository(), new PreviewUpdateEventRepository());
        using var context = new FullscreenContext(library, new FeedViewModel(new PreviewFeedService(), library), PreviewData.Shell, services);
        using var details = new GameDetailsViewModel(TileFixture.Tile(DateTime.UtcNow, title: work.Name, work: work, steamAppId: "42",
            ownership: new Ownership { ReleaseId = 1, Store = "steam", Installed = true }),
            "Never played", [], DateTime.UtcNow, covers: leases, images: images.Rows);
        using var view = new FullscreenView(context);
        context.Push(new FullscreenDetailsPage(context, details));
        var window = new Window { Width = 1920, Height = 1080, Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var backdrop = Assert.IsType<FullscreenBackdrop>(view.CurrentPage.Backdrop);
            Assert.Equal(1920, backdrop.Bounds.Width);
            Assert.Equal(1080, backdrop.Bounds.Height);
            Assert.Same(pixels, Assert.Single(backdrop.Children.OfType<Image>()).Source);
            Assert.Contains(userBackground ? CoverKey.User("landscape") : CoverKey.IgdbBackdrop("detailshot"), leases.Keys);
            Assert.Equal(userBackground ? 0 : 1, images.Reads);
            var hero = Assert.IsType<Grid>(Assert.IsType<Grid>(view.CurrentPage.Content).Children[0]);
            Assert.Empty(hero.GetVisualDescendants().OfType<Image>());
            if (userBackground && Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is { } directory)
            {
                Directory.CreateDirectory(directory);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(directory, longTitle ? "fullscreen-details-long-title.png" : "fullscreen-details-landscape.png"));
            }
            window.Width = 3840;
            window.Height = 2160;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(leases.Requests, request => request.Key == (userBackground ? CoverKey.User("landscape") : CoverKey.IgdbBackdrop("detailshot")) && request.Width >= 3840);
            context.TextScale = 1.4;
            window.Width = 1280;
            window.Height = 720;
            Dispatcher.UIThread.RunJobs();
            var layout = Assert.IsType<Grid>(view.CurrentPage.Content);
            Assert.True(hero.Bounds.Bottom <= layout.Children[1].Bounds.Top);
            Assert.True(layout.Children[2].Bounds.Height > 100);
            Assert.Empty(view.CurrentPage.GetVisualDescendants().OfType<ScrollViewer>());
            var strip = Assert.Single(view.CurrentPage.GetVisualDescendants().OfType<Grid>(), grid => grid.Name == "FullscreenOverviewScreenshots");
            Assert.Equal(2, strip.Children.Count);
            foreach (var button in strip.Children.OfType<Button>())
            {
                var position = button.TranslatePoint(default, layout)!.Value;
                Assert.True(button.Bounds.Height >= 150);
                Assert.True(position.Y + button.Bounds.Height <= layout.Bounds.Height + 1);
                button.Focus();
                Assert.Same(button, window.FocusManager!.GetFocusedElement());
            }
            if (longTitle && Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is { } scaledDirectory)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(scaledDirectory, "fullscreen-details-long-title-140.png"));
            }
            var about = Assert.Single(view.CurrentPage.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "About game"));
            about.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.IsType<FullscreenDetailsReadingPage>(view.CurrentPage);
            Assert.Contains(view.CurrentPage.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == work.Summary);
            view.Back();

            window.Content = null;
            details.Dispose();
            Assert.Equal(0, leases.Active);
            Assert.Null(Assert.Single(backdrop.Children.OfType<Image>()).Source);
        }
        finally { window.Close(); }
    }

    private sealed class DetailImages : IWorkImageRepository
    {
        public int Reads { get; private set; }
        public IReadOnlyList<WorkImages> Rows { get; } = [new() { WorkId = 1, Source = ImageSources.Igdb,
            Kind = ImageKinds.Screenshot, ImageIds = "detailshot,detailshot2", ObservedAt = DateTime.UtcNow }];
        public Task<IReadOnlyList<WorkImages>> GetForWorkAsync(long workId, CancellationToken ct = default) { Reads++; return Task.FromResult(Rows); }
        public Task UpsertAsync(WorkImages images, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(long workId, string source, string kind, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class DetailWorks(Work work) : IWorkRepository
    {
        public Task<Work?> GetAsync(long id, CancellationToken ct = default) => Task.FromResult<Work?>(work);
        public Task<long> InsertAsync(Work value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateNameAsync(long id, string name, bool provisional, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Work?> GetByIgdbIdAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Work>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProvisionalNameTarget>> GetProvisionalNameTargetsAsync(string provider, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<EnrichmentTarget>> GetEnrichmentTargetsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> ApplyEnrichmentAsync(WorkEnrichment enrichment, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class DetailLeases(CoverArt art) : ICoverLeases
    {
        public int Active { get; private set; }
        public List<CoverKey> Keys { get; } = [];
        public List<(CoverKey Key, double Width)> Requests { get; } = [];
        public ICoverLease Acquire(CoverKey key, double width, CoverLayers layers = CoverLayers.VividAndFloor)
        { Active++; Keys.Add(key); Requests.Add((key, width)); return new DetailLease(this, art, key, CoverImaging.SnapWidth(width), layers); }
        private sealed class DetailLease(DetailLeases owner, CoverArt art, CoverKey key, int width, CoverLayers layers) : ICoverLease
        {
            private bool _disposed;
            public CoverKey Key => key;
            public int Width => width;
            public CoverLayers Layers => layers;
            public bool TryGetArt(out CoverArt value) { value = art; return true; }
            public Task<CoverArt?> GetAsync(CancellationToken ct = default) => Task.FromResult<CoverArt?>(art);
            public void Dispose() { if (_disposed) return; _disposed = true; owner.Active--; }
        }
    }

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
            page.Handle(GamepadButtons.PagePrevious);
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
    public void Opening_details_focuses_play_without_launching_and_triggers_own_local_sections()
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
            Assert.True(page.Handle(GamepadButtons.PageNext));
            Assert.Equal(1, page.SelectedSection);
            Assert.Equal(0, details.SelectedTabIndex);
            Assert.True(page.Handle(GamepadButtons.PagePrevious));
            Assert.Equal(0, page.SelectedSection);
            Assert.True(page.Handle(GamepadButtons.PagePrevious));
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
