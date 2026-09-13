using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class SupplementalFeedTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recently_played_hides_verdict_actions_and_does_not_record_impressions(bool fullscreen)
    {
        var tiles = new Tiles();
        FeedItem item = new(1, 1, "First game", "Last played today.");
        var service = new Service(new([
            new("recently_played", "Recently played", "Your latest games", [item]) { SupportsFeedback = false },
            new("recommended", "Recommended", "", [item])], 1, FeedConfidence.Established, false));
        using var feed = new FeedViewModel(service, tiles, includeReserve: fullscreen);
        await feed.LoadCommand.ExecuteAsync(null);
        using var context = new FullscreenContext(PreviewData.Library, feed, PreviewData.Shell);
        var page = fullscreen ? new FullscreenBrowsePage(context, feed: true) : null;
        var window = new Window { Width = 1920, Height = 1080,
            Content = page as Control ?? new FeedView { DataContext = feed } };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            await feed.RecordViewportEntryAsync(feed.Shelves[0].Cards[0]);
            if (fullscreen)
            {
                Assert.Empty(service.Surfaced);
                FullscreenPage? actions = null;
                context.PageRequested += value => actions = value;
                page!.FocusInitial();
                Assert.True(page.Handle(GamepadButtons.Keyboard));
                window.Content = actions;
                Dispatcher.UIThread.RunJobs();
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Not now") || Equals(b.Content, "Not interested"));
                Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Open game"));
                window.Content = page;
                page.Handle(GamepadButtons.Down);
                page.Handle(GamepadButtons.Keyboard);
                window.Content = actions;
                Dispatcher.UIThread.RunJobs();
                Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Not interested"));
            }
            else
            {
                var cards = window.GetVisualDescendants().OfType<FeedCardView>().ToArray();
                var recent = Assert.Single(cards, c => ReferenceEquals(c.DataContext, feed.Shelves[0].Cards[0]));
                Assert.False(recent.FindControl<Button>("NotNow")!.IsEffectivelyVisible);
                Assert.False(recent.FindControl<Button>("NotInterested")!.IsEffectivelyVisible);
                var open = Assert.Single(recent.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("feedcard"));
                Assert.True(open.Focus());
                Assert.True(open.Command!.CanExecute(open.CommandParameter));
                var recommended = Assert.Single(cards, c => ReferenceEquals(c.DataContext, feed.Shelves[1].Cards[0]));
                Assert.True(recommended.FindControl<Button>("NotInterested")!.IsEffectivelyVisible);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Arriving_optional_shelves_preserve_the_focused_game_and_existing_card(bool fullscreen)
    {
        var tiles = new Tiles();
        var pending = new TaskCompletionSource<FeedSupplement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new Service(new([new("builtin", "Built in", "", [new(1, 1, "First game", "Baseline reason")])],
            2, FeedConfidence.EarlyDays, false) { AdditionalShelves = pending.Task });
        using var feed = new FeedViewModel(service, tiles, includeReserve: fullscreen);
        await feed.LoadCommand.ExecuteAsync(null);
        var card = feed.Shelves[0].Cards[0];
        using var context = new FullscreenContext(PreviewData.Library, feed, PreviewData.Shell);
        var window = new Window { Width = 1920, Height = 1080, Content = fullscreen
            ? new FullscreenBrowsePage(context, feed: true)
            : new FeedView { DataContext = feed } };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var focused = Assert.Single(window.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible
                && button.Classes.Contains(fullscreen ? "tv-cover" : "feedcard"));
            Assert.True(focused.Focus());
            var name = AutomationProperties.GetName(focused);
            await feed.RecordViewportEntryAsync(card);
            pending.SetResult(new([new("plugin:extra", "Optional", "", [new(2, 2, "Second game", "Optional reason")])], 2));
            await feed.AdditionalShelvesLoading;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, feed.Shelves.Count);
            Assert.Same(card, feed.Shelves[0].Cards[0]);
            var after = Assert.IsAssignableFrom<Control>(window.FocusManager!.GetFocusedElement());
            Assert.Equal(name, AutomationProperties.GetName(after));
            if (!fullscreen) Assert.Same(focused, after);
            await feed.RecordViewportEntryAsync(card);
            Assert.Single(service.Surfaced, id => id == 1);
        }
        finally { window.Close(); pending.TrySetResult(new([], 0)); }
    }

    private sealed class Tiles : IGameTileSource
    {
        private readonly GameTileViewModel[] _tiles = [
            TileFixture.Tile(DateTime.UtcNow, title: "First game"),
            TileFixture.Tile(DateTime.UtcNow, ownershipId: 2, releaseId: 2, workId: 2, title: "Second game"),
        ];
        public Tiles()
        {
            foreach (var tile in _tiles)
                tile.OpenDetailsCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });
        }
        public event EventHandler? TilesChanged { add { } remove { } }
        public bool HasTiles => true;
        public GameTileViewModel? TileForOwnership(long id) => _tiles.FirstOrDefault(tile => tile.OwnershipId == id);
        public GameTileViewModel? TileForRelease(long id) => _tiles.FirstOrDefault(tile => tile.ReleaseId == id);
    }

    private sealed class Service(FeedSnapshot snapshot) : IFeedService
    {
        public List<long> Surfaced { get; } = [];
        public Task<FeedSnapshot> GetShelvesAsync(CancellationToken ct = default) => Task.FromResult(snapshot);
        public Task RecordSurfacedAsync(long id, string shelf, CancellationToken ct = default)
        { Surfaced.Add(id); return Task.CompletedTask; }
        public Task<FeedVerdictOutcome> RecordVerdictAsync(long id, FeedVerdictKind kind, CancellationToken ct = default)
            => Task.FromResult(FeedVerdictOutcome.NotSaved);
        public Task<bool> RevokeVerdictAsync(long id, FeedVerdictKind kind, CancellationToken ct = default) => Task.FromResult(false);
        public Task<IReadOnlyList<FeedVerdictRecord>> GetHistoryAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FeedVerdictRecord>>([]);
    }
}
