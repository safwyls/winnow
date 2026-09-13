using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views.Fullscreen;
using Winnow.Data.Repositories;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenRowNavigationTests
{
    [AvaloniaFact]
    public async Task Fullscreen_shell_keeps_outgoing_cards_opaque_across_all_row_transitions()
    {
        using var fixture = new Fixture();
        await fixture.Library.LoadCommand.ExecuteAsync(null);
        for (var shelf = 0; shelf < 4; shelf++)
            fixture.Feed.Shelves.Add(new FeedShelfViewModel($"shelf-{shelf}", $"Shelf {shelf + 1}", "",
                fixture.Library.AllTiles.Skip(shelf * 6).Take(6).Select(tile => new FeedCardViewModel(tile, "Ready to play."))));
        using var television = new FullscreenView(fixture.Context);
        var window = new Window { Width = 1920, Height = 1080, Content = television };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            VerifyTransition("home", 1);
            television.Handle(GamepadButtons.Next); Dispatcher.UIThread.RunJobs();
            Assert.IsType<FullscreenBrowsePage>(television.CurrentPage);
            Assert.Equal("Library", television.CurrentPage.Title);
            VerifyTransition("library", 2);
            television.Handle(GamepadButtons.Search); Dispatcher.UIThread.RunJobs();
            Assert.IsType<FullscreenBrowseSearchPage>(television.CurrentPage);
            VerifyTransition("search", 2);
        }
        finally { window.Close(); }

        void VerifyTransition(string surface, int moves)
        {
            var viewport = Viewport(television.CurrentPage);
            var outgoing = viewport.GetRow(0);
            Assert.True(Cards(outgoing).First().Focus());
            for (var i = 0; i < moves; i++) television.Handle(GamepadButtons.Down);
            Assert.Equal(1, viewport.FirstRow);
            Assert.Same(outgoing, viewport.GetRow(0));
            Assert.True(viewport.IsAnimating);
            viewport.AdvanceAnimation(TimeSpan.FromMilliseconds(80));
            AssertNoFade(viewport);
            Assert.All(Cards(outgoing).SelectMany(card => card.GetVisualDescendants().OfType<ContentPresenter>()),
                presenter => Assert.Equal(1, presenter.Opacity));
            CaptureTransition(window, viewport, $"{surface}-shell");
            viewport.AdvanceAnimation(TimeSpan.FromMilliseconds(220));
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Immediate_navigation_after_content_expands_uses_current_rows_before_queued_rebuild(bool home)
    {
        using var fixture = new Fixture();
        await fixture.Library.LoadCommand.ExecuteAsync(null);
        if (home)
            fixture.Feed.Shelves.Add(new FeedShelfViewModel("initial", "Initial shelf", "",
                fixture.Library.AllTiles.Take(4).Select(tile => new FeedCardViewModel(tile, "Ready to play."))));
        else fixture.Library.SearchText = "Game 1";
        using var page = new FullscreenBrowsePage(fixture.Context, home);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); page.FocusInitial();
            Assert.Equal(0, Viewport(page).FirstRow);
            if (home)
            {
                for (var shelf = 1; shelf <= 2; shelf++)
                    fixture.Feed.Shelves.Add(new FeedShelfViewModel($"added-{shelf}", $"Added shelf {shelf}", "",
                        fixture.Library.AllTiles.Skip(shelf * 4).Take(4).Select(tile => new FeedCardViewModel(tile, "Ready to play."))));
            }
            else fixture.Library.SearchText = string.Empty;

            // Input can arrive before the coalesced content rebuild runs on the dispatcher.
            Assert.True(page.Handle(GamepadButtons.Down));
            Assert.True(page.Handle(GamepadButtons.Down));
            var viewport = Viewport(page);
            Assert.Equal(home ? 2 : 1, viewport.FirstRow);
            var selectedRow = viewport.GetRow(2);
            Assert.Contains(window.FocusManager!.GetFocusedElement(), Cards(selectedRow));
            var selectedName = AutomationProperties.GetName(Assert.IsAssignableFrom<Control>(window.FocusManager.GetFocusedElement()));
            Dispatcher.UIThread.RunJobs();
            page.FocusInitial();
            Assert.Equal(selectedName, AutomationProperties.GetName(Assert.IsAssignableFrom<Control>(window.FocusManager.GetFocusedElement())));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Search_returns_from_go_to_results_to_the_first_row_in_the_same_column()
    {
        using var fixture = new Fixture();
        await fixture.Library.LoadCommand.ExecuteAsync(null);
        using var page = new FullscreenBrowseSearchPage(fixture.Context);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var viewport = Viewport(page);
            var thirdCard = Cards(viewport.GetRow(0))[2];
            Assert.True(thirdCard.Focus());
            Assert.True(page.Handle(GamepadButtons.Up));
            var header = Assert.IsType<Button>(window.FocusManager!.GetFocusedElement());
            Assert.Equal("Go to results", AutomationProperties.GetName(header));
            Assert.True(page.Handle(GamepadButtons.Down));
            Assert.Same(thirdCard, window.FocusManager.GetFocusedElement());
            Assert.Equal(0, viewport.FirstRow);
            Assert.False(viewport.IsAnimating);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Home_moves_retained_shelves_and_reverses_without_replacing_their_cards()
    {
        using var fixture = new Fixture();
        await fixture.Library.LoadCommand.ExecuteAsync(null);
        for (var shelf = 0; shelf < 6; shelf++)
            fixture.Feed.Shelves.Add(new FeedShelfViewModel($"shelf-{shelf}", $"Shelf {shelf + 1}", "",
                fixture.Library.AllTiles.Skip(shelf * 6).Take(6).Select(tile => new FeedCardViewModel(tile, "Ready to play."))));
        using var page = new FullscreenBrowsePage(fixture.Context, true);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); page.FocusInitial();
            var viewport = Viewport(page);
            var first = viewport.GetRow(0);
            var next = viewport.GetRow(1);
            var firstCard = Cards(first).First();
            Assert.True(page.Handle(GamepadButtons.Down));
            Assert.Equal(1, viewport.FirstRow);
            Assert.True(viewport.IsAnimating);
            Assert.Same(first, viewport.GetRow(0));
            Assert.Same(next, viewport.GetRow(1));
            Assert.Contains(window.FocusManager!.GetFocusedElement(), Cards(next));
            viewport.AdvanceAnimation(TimeSpan.FromMilliseconds(110));
            Assert.NotEqual(0, Assert.IsType<TranslateTransform>(next.RenderTransform).Y);
            AssertNoFade(viewport);
            CaptureTransition(window, viewport, "home");

            Assert.True(page.Handle(GamepadButtons.Up));
            Assert.Equal(0, viewport.FirstRow);
            Assert.Same(first, viewport.GetRow(0));
            Assert.Same(firstCard, window.FocusManager.GetFocusedElement());
            viewport.AdvanceAnimation(TimeSpan.FromMilliseconds(220));
            Assert.False(viewport.IsAnimating);
            Assert.InRange(viewport.RealizedRows.Count, 1, 3);
            Assert.All(viewport.RealizedRows.Values, row => Assert.Equal(0, Assert.IsType<TranslateTransform>(row.RenderTransform).Y));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Grid_navigation_slides_one_row_preserves_overlap_and_bounds_realization(bool search)
    {
        using var fixture = new Fixture();
        await fixture.Library.LoadCommand.ExecuteAsync(null);
        using var page = CreateGrid(fixture.Context, search);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var viewport = Viewport(page);
            var shared = viewport.GetRow(1);
            var incoming = viewport.GetRow(2);
            Assert.True(Cards(viewport.GetRow(0)).First().Focus());
            Assert.True(page.Handle(GamepadButtons.Down));
            Assert.Equal(0, viewport.FirstRow);
            Assert.False(viewport.IsAnimating);
            Assert.True(page.Handle(GamepadButtons.Down));
            Assert.Equal(1, viewport.FirstRow);
            Assert.True(viewport.IsAnimating);
            Assert.Same(shared, viewport.GetRow(1));
            Assert.Same(incoming, viewport.GetRow(2));
            Assert.Contains(window.FocusManager!.GetFocusedElement(), Cards(incoming));
            viewport.AdvanceAnimation(TimeSpan.FromMilliseconds(110));
            AssertNoFade(viewport);
            CaptureTransition(window, viewport, search ? "search" : "library");
            viewport.AdvanceAnimation(TimeSpan.FromMilliseconds(220));
            Assert.InRange(viewport.RealizedRows.Count, 2, 4);

            for (var i = 0; i < 4; i++)
            {
                page.Handle(GamepadButtons.Down);
                viewport.AdvanceAnimation(TimeSpan.FromMilliseconds(220));
                Assert.InRange(viewport.RealizedRows.Count, 2, 4);
            }
            Assert.DoesNotContain(shared, viewport.RealizedRows.Values);
            Assert.All(viewport.RealizedRows.Where(row => row.Key < viewport.FirstRow || row.Key > viewport.FirstRow + 1),
                row => { Assert.False(row.Value.IsEnabled); Assert.False(row.Value.IsHitTestVisible); });
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mouse_wheel_moves_grid_rows_through_the_same_viewport(bool search)
    {
        using var fixture = new Fixture();
        await fixture.Library.LoadCommand.ExecuteAsync(null);
        using var page = CreateGrid(fixture.Context, search);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var viewport = Viewport(page);
            var card = Cards(viewport.GetRow(0)).First();
            Assert.True(card.Focus());
            var point = card.TranslatePoint(new Point(card.Bounds.Width / 2, card.Bounds.Height / 2), window)!.Value;
            window.MouseWheel(point, new Vector(0, -1));
            window.MouseWheel(point, new Vector(0, -1));
            Assert.Equal(1, viewport.FirstRow);
            Assert.True(viewport.IsAnimating);
            viewport.AdvanceAnimation(TimeSpan.FromMilliseconds(220));
            Assert.False(viewport.IsAnimating);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reduced_motion_and_returning_to_grid_restore_the_selected_game_without_travel(bool search)
    {
        using var fixture = new Fixture();
        await fixture.Library.LoadCommand.ExecuteAsync(null);
        fixture.Context.ReducedMotion = true;
        using var page = CreateGrid(fixture.Context, search);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var viewport = Viewport(page);
            Assert.True(Cards(viewport.GetRow(0)).First().Focus());
            page.Handle(GamepadButtons.Down);
            page.Handle(GamepadButtons.Down);
            Assert.Equal(1, viewport.FirstRow);
            Assert.False(viewport.IsAnimating);
            Assert.All(viewport.RealizedRows.Values, row => Assert.Equal(0, Assert.IsType<TranslateTransform>(row.RenderTransform).Y));
            var selectedName = AutomationProperties.GetName(Assert.IsAssignableFrom<Control>(window.FocusManager!.GetFocusedElement()));

            window.Content = new Border();
            Assert.Empty(viewport.RealizedRows);
            Assert.False(viewport.IsAnimating);
            window.Content = page;
            Dispatcher.UIThread.RunJobs(); page.FocusInitial();
            var restored = Viewport(page);
            Assert.Equal(1, restored.FirstRow);
            Assert.False(restored.IsAnimating);
            Assert.Equal(selectedName, AutomationProperties.GetName(Assert.IsAssignableFrom<Control>(window.FocusManager.GetFocusedElement())));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Narrowing_results_during_travel_releases_old_rows_and_selects_a_valid_game(bool search)
    {
        using var fixture = new Fixture();
        await fixture.Library.LoadCommand.ExecuteAsync(null);
        using var page = CreateGrid(fixture.Context, search);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var previous = Viewport(page);
            Assert.True(Cards(previous.GetRow(0)).First().Focus());
            page.Handle(GamepadButtons.Down);
            page.Handle(GamepadButtons.Down);
            Assert.True(previous.IsAnimating);

            if (search)
                Assert.Single(page.GetVisualDescendants().OfType<TextBox>()).Text = "Game 80";
            else fixture.Library.SearchText = "Game 80";
            Dispatcher.UIThread.RunJobs(); page.FocusInitial();
            var current = Viewport(page);
            Assert.Empty(previous.RealizedRows);
            Assert.False(previous.IsAnimating);
            Assert.Equal(0, current.FirstRow);
            Assert.False(current.IsAnimating);
            var game = Assert.Single(Cards(current.GetRow(0)));
            Assert.Contains("Game 80", AutomationProperties.GetName(game));
            Assert.Same(game, window.FocusManager!.GetFocusedElement());
            AssertNoFade(current);
        }
        finally { window.Close(); }
    }

    private static FullscreenPage CreateGrid(FullscreenContext context, bool search) => search
        ? new FullscreenBrowseSearchPage(context) : new FullscreenBrowsePage(context, false);

    private static FullscreenRowViewport Viewport(Control page) => Assert.Single(page.GetVisualDescendants().OfType<FullscreenRowViewport>());

    private static Button[] Cards(Control row) => row.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("tv-cover")).ToArray();

    private static void AssertNoFade(FullscreenRowViewport viewport)
    {
        Assert.True(viewport.ClipToBounds);
        foreach (var row in viewport.RealizedRows.Values)
        {
            Assert.Equal(1, row.Opacity);
            Assert.Null(row.OpacityMask);
            foreach (var card in Cards(row))
            {
                Assert.Equal(1, card.Opacity);
                Assert.Null(card.OpacityMask);
                Assert.All(card.GetVisualDescendants().OfType<FullscreenCover>(), cover => Assert.Null(cover.OpacityMask));
            }
        }
    }

    private static void CaptureTransition(Window window, FullscreenRowViewport viewport, string surface)
    {
        if (Environment.GetEnvironmentVariable("WINNOW_SCREENSHOT_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        window.UpdateLayout();
        viewport.AdvanceAnimation(TimeSpan.FromMilliseconds(80));
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, $"fullscreen-{surface}-row-transition.png"));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TempDatabase _database = new();
        public LibraryViewModel Library { get; }
        public FeedViewModel Feed { get; }
        public FullscreenContext Context { get; }

        public Fixture()
        {
            LibraryReadFixtures.Seed(_database, 80);
            Library = new LibraryViewModel(new LibraryQueryRepository(_database.Factory), new OwnershipRepository(_database.Factory),
                new ReleaseRepository(_database.Factory), new WorkRepository(_database.Factory), new UpdateEventRepository(_database.Factory));
            Feed = new FeedViewModel(new PreviewFeedService(), Library);
            Context = new FullscreenContext(Library, Feed, PreviewData.Shell);
        }

        public void Dispose()
        {
            Context.Dispose();
            _database.Dispose();
        }
    }
}
