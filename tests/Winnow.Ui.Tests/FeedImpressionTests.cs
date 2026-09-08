using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FeedImpressionTests
{
    [AvaloniaFact]
    public async Task Add_to_list_from_a_scrolled_card_focuses_the_picker_and_escape_returns_to_the_card()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Window.Width = 1024;
        fixture.Window.Show();
        await FlushAsync();
        fixture.Page.Offset = new Vector(0, fixture.Page.Extent.Height);
        await FlushAsync();
        var card = fixture.View.GetVisualDescendants().OfType<FeedCardView>().Last();
        var button = card.FindControl<Button>("AddToList")!;
        var actions = (Panel)button.Parent!;
        var visibleButtons = actions.Children.OfType<Button>().Where(b => b.IsVisible).ToArray();
        foreach (var first in visibleButtons)
        {
            Assert.True(first.Bounds.Right <= actions.Bounds.Width);
            foreach (var second in visibleButtons.Where(b => !ReferenceEquals(b, first)))
                Assert.False(first.Bounds.Intersects(second.Bounds));
        }
        Assert.True(button.Focus(NavigationMethod.Tab), $"visible={button.IsEffectivelyVisible} enabled={button.IsEffectivelyEnabled} focusable={button.Focusable} canAdd={((FeedCardViewModel)card.DataContext!).CanAddToList}");
        await FlushAsync();
        Assert.Same(button, fixture.Window.FocusManager!.GetFocusedElement());
        fixture.Window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        fixture.Window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        await FlushAsync();

        Assert.NotNull(fixture.Feed.ListPrompt);
        var input = Assert.IsType<TextBox>(fixture.Window.FocusManager!.GetFocusedElement());
        Assert.Equal("New list name", input.Watermark);
        var position = input.TranslatePoint(default, fixture.Window)!.Value;
        Assert.InRange(position.Y, 0, fixture.Window.Height - input.Bounds.Height);

        fixture.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        fixture.Window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await FlushAsync();
        Assert.Null(fixture.Feed.ListPrompt);
        Assert.Same(button, fixture.Window.FocusManager.GetFocusedElement());
    }

    [AvaloniaFact]
    public async Task Only_cards_intersecting_the_live_viewport_are_recorded_and_scroll_records_new_cards()
    {
        using var fixture = await Fixture.CreateAsync();
        Assert.Empty(fixture.Service.Seen);
        fixture.Window.Show();
        await FlushAsync();
        Assert.True(fixture.Window.IsActive);
        Assert.NotEmpty(fixture.Service.Seen);
        Assert.DoesNotContain(fixture.Service.Seen, item => item.Release == 12);
        var firstCount = fixture.Service.Seen.Count;
        fixture.Page.Offset = new Vector(0, fixture.Page.Extent.Height);
        await FlushAsync();
        Assert.Contains(fixture.Service.Seen, item => item.Release == 12);
        Assert.True(fixture.Service.Seen.Count > firstCount);
        fixture.Page.Offset = default;
        await FlushAsync();
        Assert.Equal(fixture.Service.Seen.Count, fixture.Service.Seen.Select(item => item.Release).Distinct().Count());
    }

    [AvaloniaFact]
    public async Task Hidden_feed_and_inactive_or_minimized_window_do_not_record_cards()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Container.IsVisible = false;
        fixture.Window.Show();
        await FlushAsync();
        Assert.Empty(fixture.Service.Seen);
        SetActive(fixture.Window, false);
        Assert.False(fixture.Window.IsActive);
        fixture.Container.IsVisible = true;
        await FlushAsync();
        Assert.Empty(fixture.Service.Seen);
        fixture.Window.WindowState = WindowState.Minimized;
        SetActive(fixture.Window, true);
        await FlushAsync();
        Assert.Empty(fixture.Service.Seen);
        fixture.Window.WindowState = WindowState.Normal;
        await FlushAsync();
        Assert.NotEmpty(fixture.Service.Seen);
    }

    [AvaloniaFact]
    public async Task Reloads_and_history_return_do_not_duplicate_today_and_new_day_records_again()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Window.Show();
        await FlushAsync();
        var initial = fixture.Service.Seen.ToArray();
        Assert.NotEmpty(initial);
        await fixture.Feed.LoadCommand.ExecuteAsync(null);
        await FlushAsync();
        Assert.Equal(initial, fixture.Service.Seen);
        fixture.Feed.IsHistoryOpen = true;
        await FlushAsync();
        fixture.Clock.Now = fixture.Clock.Now.AddDays(1);
        SetActive(fixture.Window, true);
        await FlushAsync();
        Assert.Equal(initial, fixture.Service.Seen);
        fixture.Feed.IsHistoryOpen = false;
        await FlushAsync();
        Assert.Equal(initial.Length * 2, fixture.Service.Seen.Count);
    }

    [AvaloniaFact]
    public async Task Hidden_window_can_load_and_scroll_without_creating_impressions()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Window.Show();
        await FlushAsync();
        fixture.Window.Hide();
        var count = fixture.Service.Seen.Count;
        fixture.Page.Offset = new Vector(0, fixture.Page.Extent.Height);
        await fixture.Feed.LoadCommand.ExecuteAsync(null);
        await FlushAsync();
        Assert.Equal(count, fixture.Service.Seen.Count);
        fixture.Window.Show();
        await FlushAsync();
        Assert.Contains(fixture.Service.Seen, item => item.Release == 12);
    }

    [AvaloniaFact]
    public async Task Offscreen_reserve_promotion_waits_for_viewport_entry()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Window.Show();
        await FlushAsync();
        var outgoing = fixture.Feed.Shelves[1].Cards[^1];
        await outgoing.NotInterestedCommand.ExecuteAsync(null);
        // Advance the receipt clock deterministically instead of sleeping through it.
        typeof(FeedViewModel).GetMethod("Tick", System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic)!.Invoke(fixture.Feed, [TimeSpan.FromSeconds(4)]);
        await FlushAsync();
        Assert.Equal(13, fixture.Feed.Shelves[1].Cards[^1].Tile.ReleaseId);
        Assert.DoesNotContain(fixture.Service.Seen, item => item.Release is 12 or 13);
        fixture.Page.Offset = new Vector(0, fixture.Page.Extent.Height);
        await FlushAsync();
        Assert.Contains(fixture.Service.Seen, item => item.Release == 13);
    }

    [AvaloniaFact]
    public async Task A_modal_covering_the_feed_suppresses_impressions_until_it_closes()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Window.Content = null;
        var overlay = new Border { Background = Avalonia.Media.Brushes.Black };
        fixture.Window.Content = new Panel { Children = { fixture.Container, overlay } };
        fixture.Window.Show();
        await FlushAsync();
        Assert.Empty(fixture.Service.Seen);
        overlay.IsVisible = false;
        await FlushAsync();
        Assert.NotEmpty(fixture.Service.Seen);
    }

    [AvaloniaFact]
    public async Task Moving_from_an_unshown_window_does_not_leave_observation_stalled()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Window.Content = null;
        var replacement = new Window { Width = 800, Height = 480, Content = fixture.Container };
        try
        {
            replacement.Show();
            await FlushAsync();
            Assert.NotEmpty(fixture.Service.Seen);
        }
        finally { replacement.Close(); }
    }

    // Feed the same notification as the platform backend: headless windows have
    // no desktop window manager to deactivate them on demand.
    private static void SetActive(Window window, bool active)
        => typeof(WindowBase).GetMethod(active ? "HandleActivated" : "HandleDeactivated",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, null);

    private static async Task FlushAsync()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(150);
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Fixture : IDisposable, IGameTileSource
    {
        public Window Window { get; } = new() { Width = 800, Height = 480 };
        public Border Container { get; } = new();
        public FeedView View { get; } = new();
        public FeedViewModel Feed { get; private set; } = null!;
        public Service Service { get; } = new();
        public Clock Clock { get; } = new();
        public ScrollViewer Page => View.FindControl<ScrollViewer>("Page")!;
        private readonly Dictionary<long, GameTileViewModel> _tiles = [];
        public event EventHandler? TilesChanged { add { } remove { } }
        public bool HasTiles => true;
        public GameTileViewModel? TileForOwnership(long id) => _tiles.GetValueOrDefault(id);
        public GameTileViewModel? TileForRelease(long id) => _tiles.GetValueOrDefault(id);

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            for (var i = 1; i <= 13; i++)
            {
                fixture._tiles[i] = TileFixture.Tile(fixture.Clock.Now.UtcDateTime, i, i, i, $"Fixture {i}");
                fixture._tiles[i].OpenDetailsCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });
            }
            fixture.Feed = new FeedViewModel(fixture.Service, fixture, fixture.Clock,
                lists: new Winnow.App.ViewModels.Lists.ListsViewModel());
            await fixture.Feed.LoadCommand.ExecuteAsync(null);
            fixture.View.DataContext = fixture.Feed;
            fixture.Container.Child = fixture.View;
            fixture.Window.Content = fixture.Container;
            return fixture;
        }

        public void Dispose()
        {
            Window.Close();
            foreach (var card in Feed.Shelves.SelectMany(s => s.Cards)) card.Dispose();
        }
    }

    private sealed class Service : IFeedService
    {
        public List<(long Release, string Shelf)> Seen { get; } = [];
        public Task<FeedSnapshot> GetShelvesAsync(CancellationToken ct = default)
            => Task.FromResult(new FeedSnapshot(Enumerable.Range(0, 2).Select(shelf =>
                new FeedShelf($"shelf-{shelf}", "A shelf", "Games to revisit", Enumerable.Range(shelf * 6 + 1, 6)
                    .Select(i => new FeedItem(i, i, $"Fixture {i}", "You last played this game a long time ago.")).ToArray())
                {
                    Reserve = shelf == 1 ? [new FeedItem(13, 13, "Fixture 13", "A new reason for a held card.")] : [],
                }).ToArray(),
                12, FeedConfidence.Established, false));
        public Task RecordSurfacedAsync(long releaseId, string shelfId, CancellationToken ct = default)
        {
            Seen.Add((releaseId, shelfId));
            return Task.CompletedTask;
        }
        public Task<FeedVerdictOutcome> RecordVerdictAsync(long releaseId, FeedVerdictKind kind, CancellationToken ct = default)
            => Task.FromResult(new FeedVerdictOutcome(true, null));
        public Task<bool> RevokeVerdictAsync(long releaseId, FeedVerdictKind kind, CancellationToken ct = default)
            => Task.FromResult(true);
        public Task<IReadOnlyList<FeedVerdictRecord>> GetHistoryAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FeedVerdictRecord>>([]);
    }
}
