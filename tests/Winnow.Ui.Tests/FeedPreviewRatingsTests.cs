using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FeedPreviewRatingsTests
{
    [AvaloniaFact]
    public void Hover_displays_each_available_population_with_its_score_and_count()
    {
        var requests = 0;
        using var fixture = new PreviewFixture(_ =>
        {
            requests++;
            return Task.FromResult(Ratings());
        });
        Assert.Equal(0, requests);
        fixture.Hover();
        Assert.Equal(1, requests);
        var ratings = fixture.RatingsControl;
        Assert.True(ratings.IsEffectivelyVisible);
        var lines = ratings.GetVisualDescendants().OfType<TextBlock>().ToArray();
        var expected = GameReceptionViewModel.From(Ratings())!.Figures;
        Assert.Equal(3, lines.Length);
        for (var i = 0; i < lines.Length; i++)
        {
            Assert.Equal($"{expected[i].Source}  {expected[i].Value} · {expected[i].Count}", lines[i].Text);
            Assert.Equal(expected[i].AutomationName, AutomationProperties.GetName(lines[i]));
        }
        fixture.Window.MouseMove(new Point(880, 380));
        Pump();
        Assert.False(fixture.Flyout.IsOpen);
        Assert.Null(fixture.Model.Reception);
    }

    [AvaloniaFact]
    public async Task Missing_ratings_do_not_reserve_preview_space()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<WorkRating>>();
        using var fixture = new PreviewFixture(_ => pending.Task);
        fixture.Hover();
        var size = fixture.Bubble.Bounds.Size;
        Assert.False(fixture.RatingsControl.IsVisible);
        pending.SetResult([]);
        await DrainCompletion();
        Assert.Null(fixture.Model.Reception);
        Assert.False(fixture.RatingsControl.IsVisible);
        Assert.Equal(size, fixture.Bubble.Bounds.Size);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Closed_or_disposed_preview_cancels_pending_ratings_and_ignores_late_result(bool dispose)
    {
        var pending = new TaskCompletionSource<IReadOnlyList<WorkRating>>();
        CancellationToken cancellation = default;
        using var fixture = new PreviewFixture(token =>
        {
            cancellation = token;
            return pending.Task;
        });
        fixture.Hover();
        if (dispose) fixture.Model.Dispose();
        else fixture.Window.MouseMove(new Point(880, 380));
        Pump();
        Assert.True(cancellation.IsCancellationRequested);
        pending.SetResult(Ratings());
        await DrainCompletion();
        Assert.Null(fixture.Model.Reception);
    }

    [AvaloniaFact]
    public async Task Ratings_arriving_near_bottom_reposition_the_growing_preview_inside_window()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<WorkRating>>();
        using var fixture = new PreviewFixture(_ => pending.Task, 340);
        fixture.Hover();
        var height = fixture.Bubble.Bounds.Height;
        pending.SetResult(Ratings());
        for (var i = 0; i < 100 && fixture.Model.Reception is null; i++)
            await DrainCompletion();
        Assert.NotNull(fixture.Model.Reception);
        Pump();
        Assert.True(fixture.Bubble.Bounds.Height > height);
        var origin = fixture.Window.PointToClient(fixture.Bubble.PointToScreen(default));
        Assert.InRange(origin.X, 0, fixture.Window.Bounds.Width - fixture.Bubble.Bounds.Width);
        Assert.InRange(origin.Y, 0, fixture.Window.Bounds.Height - fixture.Bubble.Bounds.Height);
    }

    private static IReadOnlyList<WorkRating> Ratings() =>
    [
        Rating(RatingSources.Steam, 93, 1500),
        Rating(RatingSources.IgdbCritics, 88, 12),
        Rating(RatingSources.IgdbUsers, 81, 100),
    ];

    private static WorkRating Rating(string source, double score, int count) => new()
    {
        WorkId = 1, Source = source, Score = score, RatingCount = count,
        ObservedAt = DateTime.UtcNow,
    };

    private static async Task DrainCompletion()
    {
        await Task.Delay(10);
        Pump();
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class PreviewFixture : IDisposable
    {
        public FeedCardViewModel Model { get; }
        public Window Window { get; }
        public FeedCardView View { get; }
        public Flyout Flyout => Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(View.FindControl<Button>("Card")!));
        public FeedPreviewBubble Bubble => Assert.IsType<FeedPreviewBubble>(Flyout.Content);
        public ItemsControl RatingsControl => Bubble.GetVisualDescendants().OfType<ItemsControl>()
            .Single(control => control.Name == "PreviewRatings");

        public PreviewFixture(Func<CancellationToken, Task<IReadOnlyList<WorkRating>>> load, double y = 20)
        {
            var source = TileFixture.Tile(DateTime.UtcNow);
            var tile = new GameTileViewModel(source.Entries, source.Game, "Hades", DateTime.UtcNow)
            {
                LoadRatings = load,
            };
            Model = new FeedCardViewModel(tile, "Reason", new FeedbackService());
            View = new FeedCardView { DataContext = Model, Width = 220 };
            Canvas.SetLeft(View, 20);
            Canvas.SetTop(View, y);
            Window = new Window { Width = 900, Height = 400, Content = new Canvas { Children = { View } } };
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
            Model.Dispose();
        }
    }

    private sealed class FeedbackService : IFeedService
    {
        public Task<FeedSnapshot> GetShelvesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task RecordSurfacedAsync(long releaseId, string shelfId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<FeedVerdictOutcome> RecordVerdictAsync(long releaseId, FeedVerdictKind kind, CancellationToken ct = default)
            => Task.FromResult(new FeedVerdictOutcome(true, null));
        public Task<bool> RevokeVerdictAsync(long releaseId, FeedVerdictKind kind, CancellationToken ct = default)
            => Task.FromResult(true);
        public Task<IReadOnlyList<FeedVerdictRecord>> GetHistoryAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FeedVerdictRecord>>([]);
    }
}
