using System.Collections.Concurrent;
using Winnow.App.ViewModels;
using Winnow.Covers;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Cover state is per surface. The wall and the feed show the same game from the
/// same tile at the same time, and these are the three failure modes that a
/// single shared mutable copy produced: a recycled wall container blanking a
/// visible feed card, a small feed request overwriting larger wall art, and a
/// load landing on a container that has since been recycled onto another game.
///
/// <para>The payload is a <see cref="CoverArt"/> with null layers: no test in
/// this project starts a rendering platform, and nothing here looks inside the
/// pair. Each surface is driven through a queue standing in for the dispatcher,
/// so a result lands exactly when the test says it does and no Avalonia
/// dispatcher has to exist.</para>
/// </summary>
public sealed class CoverPresenterTests
{
    private static readonly CoverKey Half = CoverKey.Steam("620");
    private static readonly CoverKey Life = CoverKey.Steam("70");

    private static CoverArt Art() => new(null!, null!);

    [Fact]
    public void Recycling_the_wall_tile_leaves_the_feed_cards_art_alone()
    {
        var cache = new FakeCoverCache();
        var pool = new CoverLeasePool(cache);

        var wall = new Surface(Half, pool);
        var feed = new Surface(Half, pool);
        wall.Presenter.Request(148);
        feed.Presenter.Request(108);

        var art = Art();
        cache.Complete(Half, 160, art);
        wall.Settle();
        feed.Settle();

        Assert.Same(art, wall.Presenter.Art);
        Assert.Same(art, feed.Presenter.Art);

        // The wall scrolls; the container is recycled onto another game.
        wall.Presenter.Release();

        Assert.Null(wall.Presenter.Art);
        Assert.True(wall.Presenter.ShowPlaceholder);
        Assert.Same(art, feed.Presenter.Art);
        Assert.False(feed.Presenter.ShowPlaceholder);
        Assert.True(feed.Presenter.HasCover);
    }

    [Fact]
    public void A_smaller_request_from_another_surface_does_not_shrink_the_walls_art()
    {
        var cache = new FakeCoverCache();
        var pool = new CoverLeasePool(cache);

        var wall = new Surface(Half, pool);
        wall.Presenter.Request(300);
        var large = Art();
        cache.Complete(Half, 320, large);
        wall.Settle();

        var feed = new Surface(Half, pool);
        feed.Presenter.Request(108);
        var small = Art();
        cache.Complete(Half, 160, small);
        feed.Settle();

        Assert.Same(large, wall.Presenter.Art);
        Assert.Equal(320, wall.Presenter.PresentedWidth);
        Assert.Same(small, feed.Presenter.Art);
        Assert.Equal(160, feed.Presenter.PresentedWidth);
    }

    [Fact]
    public void A_small_result_arriving_after_a_large_one_never_downgrades_the_surface()
    {
        var cache = new FakeCoverCache();
        var pool = new CoverLeasePool(cache);

        var wall = new Surface(Half, pool);

        // Both in flight at once: the tile grew under a density change before
        // the first decode came back.
        wall.Presenter.Request(148);
        wall.Presenter.Request(300);

        var large = Art();
        cache.Complete(Half, 320, large);
        wall.Settle();
        Assert.Same(large, wall.Presenter.Art);

        var small = Art();
        cache.Complete(Half, 160, small);
        wall.Settle();

        Assert.Same(large, wall.Presenter.Art);
        Assert.Equal(320, wall.Presenter.PresentedWidth);
    }

    [Fact]
    public void A_load_that_lands_after_recycling_cannot_paint_the_next_game()
    {
        var cache = new FakeCoverCache();
        var pool = new CoverLeasePool(cache);

        var container = new Surface(Half, pool);
        container.Presenter.Request(148);

        // Recycled onto another game before the first decode came back.
        container.Presenter.Target(Life, pool);
        container.Presenter.Request(148);

        cache.Complete(Half, 160, Art());
        container.Settle();
        Assert.Null(container.Presenter.Art);
        Assert.True(container.Presenter.ShowPlaceholder);

        var right = Art();
        cache.Complete(Life, 160, right);
        container.Settle();
        Assert.Same(right, container.Presenter.Art);
    }

    [Fact]
    public void A_memory_hit_applies_without_waiting_and_is_not_asked_for_twice()
    {
        var cache = new FakeCoverCache();
        var art = Art();
        cache.Memory[(Half, 160)] = art;
        var pool = new CoverLeasePool(cache);

        var wall = new Surface(Half, pool);
        wall.Presenter.Request(148);

        Assert.Same(art, wall.Presenter.Art);
        Assert.Equal(0, cache.Requests);

        // Re-realized at the same bucket: nothing to fetch, nothing to repaint.
        wall.Presenter.Request(148);
        Assert.Equal(0, cache.Requests);
    }

    [Fact]
    public void Releasing_a_surface_drops_its_lease_so_the_memory_cache_is_the_only_owner()
    {
        var cache = new FakeCoverCache();
        var pool = new CoverLeasePool(cache);

        var wall = new Surface(Half, pool);
        var feed = new Surface(Half, pool);
        wall.Presenter.Request(148);
        feed.Presenter.Request(108);
        cache.Complete(Half, 160, Art());
        wall.Settle();
        feed.Settle();

        Assert.Equal(1, pool.LiveSlots);

        wall.Presenter.Release();
        Assert.Equal(1, pool.LiveSlots);

        feed.Presenter.Dispose();
        Assert.Equal(0, pool.LiveSlots);
    }

    /// <summary>One consumer: its presenter and the queue standing in for the dispatcher.</summary>
    private sealed class Surface
    {
        private readonly ConcurrentQueue<Action> _posted = new();

        public Surface(CoverKey key, ICoverLeases leases)
        {
            Presenter = new CoverPresenter(_posted.Enqueue);
            Presenter.Target(key, leases);
        }

        public CoverPresenter Presenter { get; }

        /// <summary>Runs what the load posted back, so results land in a stated order.</summary>
        public void Settle()
        {
            SpinWait.SpinUntil(() => !_posted.IsEmpty, TimeSpan.FromSeconds(5));
            while (_posted.TryDequeue(out var action))
            {
                action();
            }
        }
    }

    /// <summary>Hands out one <see cref="TaskCompletionSource{TResult}"/> per slot, completed by the test.</summary>
    private sealed class FakeCoverCache : ICoverCache
    {
        private readonly Dictionary<(CoverKey Key, int Width), TaskCompletionSource<CoverArt?>> _pending = [];

        public Dictionary<(CoverKey Key, int Width), CoverArt> Memory { get; } = [];

        public int Requests { get; private set; }

        public bool TryGet(CoverKey key, double displayWidthPixels, out CoverArt art)
        {
            if (Memory.TryGetValue((key, CoverImaging.SnapWidth(displayWidthPixels)), out var hit))
            {
                art = hit;
                return true;
            }

            art = null!;
            return false;
        }

        public Task<CoverArt?> GetAsync(CoverKey key, double displayWidthPixels, CancellationToken ct = default)
        {
            Requests++;
            var slot = (key, CoverImaging.SnapWidth(displayWidthPixels));
            if (!_pending.TryGetValue(slot, out var source))
            {
                source = new TaskCompletionSource<CoverArt?>();
                _pending[slot] = source;
            }

            return source.Task;
        }

        public void Complete(CoverKey key, int width, CoverArt? art)
        {
            _pending[(key, width)].SetResult(art);
            _pending.Remove((key, width));
        }
    }
}
