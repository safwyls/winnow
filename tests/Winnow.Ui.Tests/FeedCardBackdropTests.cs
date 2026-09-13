using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Covers;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FeedCardBackdropTests
{
    [AvaloniaFact]
    public async Task Released_card_ignores_late_pixels_and_reacquires_on_attach()
    {
        var leases = new Leases();
        using var card = new FeedCardViewModel(TileFixture.Tile(DateTime.UtcNow,
            steamAppId: "123", covers: leases), "Recently played");
        using var pixels = new WriteableBitmap(new PixelSize(8, 8), new Vector(96, 96));
        card.RequestBackdrop(440, 190);
        var old = Assert.Single(leases.All);
        Assert.Equal(CoverKey.SteamHero("123"), old.Key);
        card.ReleaseBackdrop();
        Assert.True(old.Disposed);
        old.Complete(pixels);
        await Task.Delay(20);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(card.Backdrop);

        card.RequestBackdrop(440, 190);
        var current = leases.All[^1];
        Assert.NotSame(old, current);
        current.Complete(pixels);
        await WaitUntil(() => card.Backdrop is not null);
        Assert.Same(pixels, card.Backdrop);
        card.Dispose();
        Assert.True(current.Disposed);
        Assert.Null(card.Backdrop);
    }

    [AvaloniaFact]
    public async Task Detached_metadata_read_cannot_replace_reattached_cards_candidates()
    {
        var leases = new Leases();
        var requests = new List<TaskCompletionSource<IReadOnlyList<WorkImages>>>();
        var cancellation = new List<CancellationToken>();
        var source = TileFixture.Tile(DateTime.UtcNow);
        var tile = new GameTileViewModel(source.Entries, source.Game, source.Title, DateTime.UtcNow,
            covers: leases)
        {
            LoadBackdropImages = token =>
            {
                cancellation.Add(token);
                var result = new TaskCompletionSource<IReadOnlyList<WorkImages>>();
                requests.Add(result);
                return result.Task;
            },
        };
        using var card = new FeedCardViewModel(tile, "Never played");
        card.RequestBackdrop(440, 190);
        card.ReleaseBackdrop();
        Assert.True(cancellation[0].IsCancellationRequested);
        card.RequestBackdrop(440, 190);
        requests[0].SetResult(Images("staleart"));
        await Task.Delay(20);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(leases.All);
        requests[1].SetResult(Images("currentart"));
        await WaitUntil(() => leases.All.Count > 0);
        Assert.Equal(CoverKey.IgdbBackdrop("currentart"), Assert.Single(leases.All).Key);
    }

    private static IReadOnlyList<WorkImages> Images(string id) =>
        [new() { WorkId = 1, Source = "igdb", Kind = "artwork", ImageIds = id, ObservedAt = DateTime.UtcNow }];

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(condition(), "Backdrop completion did not reach the UI thread.");
    }

    private sealed class Leases : ICoverLeases
    {
        public List<Lease> All { get; } = [];
        public ICoverLease Acquire(CoverKey key, double width, CoverLayers layers = CoverLayers.VividAndFloor)
        {
            var lease = new Lease(key, CoverImaging.SnapWidth(width), layers);
            All.Add(lease);
            return lease;
        }
        public sealed class Lease(CoverKey key, int width, CoverLayers layers) : ICoverLease
        {
            private readonly TaskCompletionSource<CoverArt?> _result = new();
            public CoverKey Key => key;
            public int Width => width;
            public CoverLayers Layers => layers;
            public bool Disposed { get; private set; }
            public bool TryGetArt(out CoverArt art) { art = null!; return false; }
            public Task<CoverArt?> GetAsync(CancellationToken ct = default) => _result.Task;
            public void Complete(Bitmap bitmap) => _result.SetResult(new CoverArt(bitmap, bitmap));
            public void Dispose() => Disposed = true;
        }
    }
}
