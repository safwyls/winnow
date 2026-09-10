using System.Collections.Concurrent;
using Winnow.App.Services;
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
/// <para>The payload is a <see cref="CoverArt"/> carrying a vivid layer
/// only: no test in this project starts a rendering platform, so a real pair
/// cannot be built, and nothing here looks inside the layers. Each surface is driven through a queue standing in for the dispatcher,
/// so a result lands exactly when the test says it does and no Avalonia
/// dispatcher has to exist.</para>
/// </summary>
public sealed class CoverPresenterTests
{
    private static readonly CoverKey Half = CoverKey.Steam("620");
    private static readonly CoverKey Life = CoverKey.Steam("70");

    private static CoverArt Art() => new(null!, null);

    [Fact]
    public void Enabling_dimming_during_the_initial_decode_requests_the_floor_too()
    {
        var cache = new FakeCoverCache();
        var pool = new CoverLeasePool(cache);
        var ramp = new DormancyRamp { DimsDormantCovers = false };
        using var side = new MergeSideViewModel(1, "Fixture", coverKey: Half, covers: pool, ramp: ramp);
        side.RequestCover(MergeQueueViewModel.CoverWidth);
        ramp.DimsDormantCovers = true;
        Assert.Equal([CoverLayers.Vivid, CoverLayers.VividAndFloor], cache.Asked);
        side.Dispose();
        Assert.Equal(0, pool.LiveSlots);
    }

    [Fact]
    public void Merge_thumbnails_request_the_floor_only_when_dimming_is_enabled()
    {
        var cache = new FakeCoverCache();
        var pool = new CoverLeasePool(cache);
        var ramp = new DormancyRamp { DimsDormantCovers = false };
        var width = CoverImaging.SnapWidth(MergeQueueViewModel.CoverWidth);
        cache.Memory[new FakeCoverCache.Slot(Half, width, CoverLayers.Vivid)] = Art();
        using var side = new MergeSideViewModel(1, "Fixture", coverKey: Half, covers: pool, ramp: ramp);
        side.RequestCover(MergeQueueViewModel.CoverWidth);
        Assert.Empty(cache.Asked);
        Assert.Equal(1, pool.LiveSlots);

        ramp.DimsDormantCovers = true;
        Assert.Equal([CoverLayers.VividAndFloor], cache.Asked);
        side.Dispose();
        Assert.Equal(0, pool.LiveSlots);
    }

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
        cache.Memory[new FakeCoverCache.Slot(Half, 160, CoverLayers.VividAndFloor)] = art;
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

    // ── The floor layer is only decoded when it can be seen ──────────────────

    /// <summary>
    /// With the ramp dimming covers, a tile stacks the floor variant under its
    /// vivid art and needs both decoded — the shape the wall has always asked
    /// for, restated here because the next test turns it off.
    /// </summary>
    [Fact]
    public void A_dimming_wall_asks_for_both_layers()
    {
        var cache = new FakeCoverCache();
        var pool = new CoverLeasePool(cache);
        var wall = new Surface(Half, pool, new DormancyRamp { DimsDormantCovers = true });

        Assert.Equal(CoverLayers.VividAndFloor, wall.Presenter.Layers);

        wall.Presenter.Request(148);

        Assert.Equal([CoverLayers.VividAndFloor], cache.Asked);
    }

    /// <summary>
    /// With dimming off the vivid layer is drawn at full opacity, so the floor
    /// variant is a bitmap nobody can see. The wall then holds one decode per
    /// cover instead of two.
    /// </summary>
    [Fact]
    public void A_wall_with_dimming_off_asks_for_the_vivid_layer_only()
    {
        var cache = new FakeCoverCache();
        var pool = new CoverLeasePool(cache);
        var wall = new Surface(Half, pool, new DormancyRamp { DimsDormantCovers = false });

        Assert.Equal(CoverLayers.Vivid, wall.Presenter.Layers);

        wall.Presenter.Request(148);
        cache.Complete(Half, 160, Art(), CoverLayers.Vivid);
        wall.Settle();

        Assert.Equal([CoverLayers.Vivid], cache.Asked);
        Assert.NotNull(wall.Presenter.Art);
        Assert.Null(wall.Presenter.Floor);
    }

    /// <summary>
    /// Turning dimming back on makes the floor visible under art that was
    /// decoded without it. The pair is requested at the width already on
    /// screen, and the vivid art stays up while it arrives — the toggle is
    /// still a repaint, never a blank wall.
    /// </summary>
    [Fact]
    public void Turning_dimming_on_asks_for_the_floor_at_the_width_on_screen()
    {
        var cache = new FakeCoverCache();
        var pool = new CoverLeasePool(cache);
        var ramp = new DormancyRamp { DimsDormantCovers = false };
        var wall = new Surface(Half, pool, ramp);

        wall.Presenter.Request(148);
        var vividOnly = Art();
        cache.Complete(Half, 160, vividOnly, CoverLayers.Vivid);
        wall.Settle();

        ramp.DimsDormantCovers = true;

        Assert.Equal([CoverLayers.Vivid, CoverLayers.VividAndFloor], cache.Asked);
        Assert.Same(vividOnly, wall.Presenter.Art);

        var pair = Art();
        cache.Complete(Half, 160, pair, CoverLayers.VividAndFloor);
        wall.Settle();

        Assert.Same(pair, wall.Presenter.Art);
        Assert.Equal(160, wall.Presenter.PresentedWidth);
    }

    /// <summary>
    /// The other direction costs nothing: art that carries a floor still
    /// answers a vivid-only surface, so turning dimming off never re-decodes.
    /// </summary>
    [Fact]
    public void Turning_dimming_off_does_not_ask_again()
    {
        var cache = new FakeCoverCache();
        var pool = new CoverLeasePool(cache);
        var ramp = new DormancyRamp { DimsDormantCovers = true };
        var wall = new Surface(Half, pool, ramp);

        wall.Presenter.Request(148);
        cache.Complete(Half, 160, Art(), CoverLayers.VividAndFloor);
        wall.Settle();

        ramp.DimsDormantCovers = false;

        Assert.Equal([CoverLayers.VividAndFloor], cache.Asked);
    }

    /// <summary>One consumer: its presenter and the queue standing in for the dispatcher.</summary>
    private sealed class Surface
    {
        private readonly ConcurrentQueue<Action> _posted = new();

        public Surface(CoverKey key, ICoverLeases leases, DormancyRamp? ramp = null)
        {
            Presenter = new CoverPresenter(_posted.Enqueue);
            Presenter.Target(key, leases, ramp);
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
        private readonly Dictionary<Slot, TaskCompletionSource<CoverArt?>> _pending = [];

        public Dictionary<Slot, CoverArt> Memory { get; } = [];

        public int Requests { get; private set; }

        /// <summary>The layers each load asked for, in order.</summary>
        public List<CoverLayers> Asked { get; } = [];

        public bool TryGet(CoverKey key, double displayWidthPixels, CoverLayers layers, out CoverArt art)
        {
            if (Memory.TryGetValue(new Slot(key, CoverImaging.SnapWidth(displayWidthPixels), layers), out var hit))
            {
                art = hit;
                return true;
            }

            art = null!;
            return false;
        }

        public Task<CoverArt?> GetAsync(
            CoverKey key, double displayWidthPixels, CoverLayers layers, CancellationToken ct = default)
        {
            Requests++;
            Asked.Add(layers);
            var slot = new Slot(key, CoverImaging.SnapWidth(displayWidthPixels), layers);
            if (!_pending.TryGetValue(slot, out var source))
            {
                source = new TaskCompletionSource<CoverArt?>();
                _pending[slot] = source;
            }

            return source.Task;
        }

        public void Complete(
            CoverKey key, int width, CoverArt? art, CoverLayers layers = CoverLayers.VividAndFloor)
        {
            var slot = new Slot(key, width, layers);
            _pending[slot].SetResult(art);
            _pending.Remove(slot);
        }

        /// <summary>What the cache keys on: cover, width bucket and layers.</summary>
        internal readonly record struct Slot(CoverKey Key, int Width, CoverLayers Layers);
    }
}
