using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class LibraryHoverPreviewTests
{
    [AvaloniaFact]
    public void Hover_immediately_shows_shared_preview_with_compact_ratings_and_no_actions()
    {
        var requests = 0;
        using var fixture = new Fixture(_ =>
        {
            requests++;
            return Task.FromResult(Ratings());
        });
        Assert.Equal(0, requests);
        fixture.Hover();
        Assert.True(requests > 0);
        var bubble = Assert.IsType<GamePreviewBubble>(fixture.Flyout.Content);
        var ratings = bubble.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Name == "PreviewRatings");
        Assert.Equal(GameReceptionViewModel.From(Ratings())!.CompactText, ratings.Text);
        Assert.Contains("Steam: Very Positive", ratings.Text!);
        Assert.DoesNotContain("93%", ratings.Text!);
        Assert.DoesNotContain("1,500", ratings.Text!);
        Assert.Null(ToolTip.GetTip(ratings));
        Assert.Empty(bubble.GetVisualDescendants().OfType<Button>());
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exit_or_recycle_closes_preview_and_cancels_pending_metadata(bool recycle)
    {
        var pending = new TaskCompletionSource<IReadOnlyList<WorkRating>>();
        CancellationToken token = default;
        using var fixture = new Fixture(ct => { token = ct; return pending.Task; });
        fixture.Hover();
        if (recycle) fixture.View.DataContext = TileFixture.Tile(DateTime.UtcNow, title: "Replacement");
        else fixture.Window.MouseMove(new Point(880, 450));
        Pump();
        Assert.False(fixture.Flyout.IsOpen);
        Assert.True(token.IsCancellationRequested);
        pending.SetResult(Ratings());
        await Task.Delay(20);
        Pump();
        Assert.False(fixture.Flyout.IsOpen);
        Assert.Null(fixture.Flyout.Content);
    }

    [AvaloniaFact]
    public void Opening_feed_preview_closes_library_preview_even_without_a_pointer_exit()
    {
        using var fixture = new Fixture(_ => Task.FromResult(Ratings()));
        fixture.Hover();
        var feedAnchor = fixture.FeedView.FindControl<Button>("Card")!;
        var feedFlyout = Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(feedAnchor));
        feedFlyout.ShowAt(feedAnchor);
        Pump();
        Assert.True(feedFlyout.IsOpen);
        Assert.False(fixture.Flyout.IsOpen);
        Assert.Null(fixture.Flyout.Content);
        Assert.IsType<GamePreviewBubble>(feedFlyout.Content);
    }

    [AvaloniaFact]
    public void Detaching_library_tile_closes_its_preview()
    {
        using var fixture = new Fixture(_ => Task.FromResult(Ratings()));
        fixture.Hover();
        fixture.Canvas.Children.Remove(fixture.View);
        Pump();
        Assert.False(fixture.Flyout.IsOpen);
        Assert.Null(fixture.Flyout.Content);
    }

    private static IReadOnlyList<WorkRating> Ratings() =>
    [
        new() { WorkId = 1, Source = RatingSources.Steam, Score = 93, RatingCount = 1500,
            Label = "Very Positive", ObservedAt = DateTime.UtcNow },
        new() { WorkId = 1, Source = RatingSources.IgdbCritics, Score = 88, RatingCount = 12,
            ObservedAt = DateTime.UtcNow },
    ];

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly FeedCardViewModel _feedModel;
        public GameTileView View { get; }
        public FeedCardView FeedView { get; }
        public Window Window { get; }
        public Canvas Canvas { get; }
        public Flyout Flyout => Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(View.FindControl<Border>("Face")!));

        public Fixture(Func<CancellationToken, Task<IReadOnlyList<WorkRating>>> load)
        {
            var source = TileFixture.Tile(DateTime.UtcNow);
            var tile = new GameTileViewModel(source.Entries, source.Game, "Hades", DateTime.UtcNow)
            {
                LoadRatings = load,
            };
            View = new GameTileView { DataContext = tile, Width = 180, Height = 270 };
            _feedModel = new FeedCardViewModel(TileFixture.Tile(DateTime.UtcNow, title: "Feed game"), "Reason");
            FeedView = new FeedCardView { DataContext = _feedModel, Width = 180 };
            Avalonia.Controls.Canvas.SetLeft(View, 20);
            Avalonia.Controls.Canvas.SetTop(View, 20);
            Avalonia.Controls.Canvas.SetLeft(FeedView, 600);
            Avalonia.Controls.Canvas.SetTop(FeedView, 20);
            Canvas = new Canvas { Children = { View, FeedView } };
            Window = new Window { Width = 900, Height = 500, Content = Canvas };
            Window.Show();
            Pump();
        }

        public void Hover()
        {
            Window.MouseMove(View.TranslatePoint(new Point(40, 20), Window)!.Value);
            Pump();
            Assert.True(Flyout.IsOpen);
        }

        public void Dispose()
        {
            Window.Close();
            _feedModel.Dispose();
        }
    }
}

