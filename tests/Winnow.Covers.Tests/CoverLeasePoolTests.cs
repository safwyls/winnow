using Xunit;

namespace Winnow.Covers.Tests;

/// <summary>
/// Reference counting under <see cref="CoverLeasePool"/>: one load per
/// (cover, width bucket, layers) however many surfaces want it, per-consumer
/// cancellation that does not cancel the shared load, nothing held once the
/// last lease is released — and, since TASK-152.2, the hold on the decoded
/// pixels that stops the memory cache freeing art a surface is still drawing.
///
/// <para>The payload is a <see cref="CoverArt"/> with null layers: these tests
/// start no rendering platform (see <c>DecodedLruTests</c>), and the pool
/// never looks inside the value it hands out.</para>
/// </summary>
public class CoverLeasePoolTests
{
    private static readonly CoverKey Key = CoverKey.Steam("620");

    private static CoverArt Art() => new(null!, null);

    [Fact]
    public async Task Two_surfaces_wanting_the_same_cover_at_the_same_size_share_one_load()
    {
        var cache = new FakeCoverCache();
        var pool = new CoverLeasePool(cache);

        using var wall = pool.Acquire(Key, 148);
        using var feed = pool.Acquire(Key, 108);

        var first = wall.GetAsync();
        var second = feed.GetAsync();

        Assert.Equal(160, wall.Width);
        Assert.Equal(160, feed.Width);
        Assert.Equal(1, cache.Requests);

        var art = Art();
        cache.Complete(Key, 160, art);

        Assert.Same(art, await first);
        Assert.Same(art, await second);
    }

    [Fact]
    public void Different_size_buckets_are_different_slots()
    {
        var cache = new FakeCoverCache();
        var pool = new CoverLeasePool(cache);

        using var small = pool.Acquire(Key, 160);
        using var large = pool.Acquire(Key, 300);

        _ = small.GetAsync();
        _ = large.GetAsync();

        Assert.Equal(320, large.Width);
        Assert.Equal(2, cache.Requests);
    }

    [Fact]
    public async Task The_slot_lives_until_the_last_lease_is_released_and_then_holds_nothing()
    {
        var cache = new FakeCoverCache();
        var pool = new CoverLeasePool(cache);

        var wall = pool.Acquire(Key, 160);
        var feed = pool.Acquire(Key, 160);
        var load = wall.GetAsync();
        cache.Complete(Key, 160, Art());
        await load;

        Assert.Equal(1, pool.LiveSlots);

        wall.Dispose();
        Assert.Equal(1, pool.LiveSlots);

        // Held art is a sync hit for anyone still leasing the slot.
        Assert.True(feed.TryGetArt(out _));

        feed.Dispose();
        Assert.Equal(0, pool.LiveSlots);

        // Nothing was kept: the memory cache is the only owner again, so the
        // next surface to want this cover asks it rather than the pool.
        using var later = pool.Acquire(Key, 160);
        Assert.False(later.TryGetArt(out _));
        _ = later.GetAsync();
        Assert.Equal(2, cache.Requests);
    }

    [Fact]
    public async Task One_surface_walking_away_does_not_cancel_the_load_for_the_others()
    {
        var cache = new FakeCoverCache();
        var pool = new CoverLeasePool(cache);

        using var leaving = pool.Acquire(Key, 160);
        using var staying = pool.Acquire(Key, 160);

        using var cancelled = new CancellationTokenSource();
        var abandoned = leaving.GetAsync(cancelled.Token);
        var wanted = staying.GetAsync();

        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
        Assert.False(wanted.IsCompleted);

        var art = Art();
        cache.Complete(Key, 160, art);
        Assert.Same(art, await wanted);
    }

    [Fact]
    public void A_memory_hit_is_answered_without_a_load()
    {
        var cache = new FakeCoverCache();
        var art = Art();
        cache.Memory[new FakeCoverCache.Slot(Key, 160, CoverLayers.VividAndFloor)] = art;

        var pool = new CoverLeasePool(cache);
        using var lease = pool.Acquire(Key, 160);

        Assert.True(lease.TryGetArt(out var hit));
        Assert.Same(art, hit);
        Assert.Equal(0, cache.Requests);
    }

    // ── Layers are part of the slot ──────────────────────────────────────────

    /// <summary>
    /// A wall tile needs the floor variant under its vivid art; the detail
    /// modal draws the same cover at full saturation. Sharing one slot would
    /// mean whichever asked first decided what the other got, so the layers are
    /// part of the key — and a two-layer entry still answers a vivid-only
    /// request, because extra pixels are not missing pixels.
    /// </summary>
    [Fact]
    public void A_vivid_only_lease_and_a_two_layer_lease_are_different_slots()
    {
        var cache = new FakeCoverCache();
        var pool = new CoverLeasePool(cache);

        using var tile = pool.Acquire(Key, 160, CoverLayers.VividAndFloor);
        using var modal = pool.Acquire(Key, 160, CoverLayers.Vivid);

        Assert.Equal(CoverLayers.VividAndFloor, tile.Layers);
        Assert.Equal(CoverLayers.Vivid, modal.Layers);
        Assert.Equal(2, pool.LiveSlots);

        _ = tile.GetAsync();
        _ = modal.GetAsync();

        Assert.Equal(2, cache.Requests);
    }

    [Fact]
    public void A_two_layer_memory_entry_answers_a_vivid_only_lease()
    {
        var cache = new FakeCoverCache();
        var art = Art();
        cache.Memory[new FakeCoverCache.Slot(Key, 160, CoverLayers.Vivid)] = art;

        var pool = new CoverLeasePool(cache);
        using var lease = pool.Acquire(Key, 160, CoverLayers.Vivid);

        Assert.True(lease.TryGetArt(out var hit));
        Assert.Same(art, hit);
        Assert.Equal(0, cache.Requests);
    }

    // ── Leases decide when pixels are freed ──────────────────────────────────

    /// <summary>
    /// The invariant the whole disposal rule rests on: the memory cache may
    /// evict art a tile is drawing, and the lease keeps the pixels valid until
    /// the tile lets go. Eviction is modelled here the way the cache does it —
    /// by releasing the LRU's own hold.
    /// </summary>
    [Fact]
    public void A_lease_keeps_evicted_art_alive_until_it_is_disposed()
    {
        var frees = 0;
        var cache = new FakeCoverCache();
        var art = new CoverArt(null!, null, _ => frees++);
        cache.Memory[new FakeCoverCache.Slot(Key, 160, CoverLayers.VividAndFloor)] = art;

        var pool = new CoverLeasePool(cache);
        var lease = pool.Acquire(Key, 160);
        Assert.True(lease.TryGetArt(out _));

        // The cache evicts under the tile.
        art.ReleaseHold();

        Assert.Equal(0, frees);
        Assert.True(lease.TryGetArt(out var still));
        Assert.Same(art, still);

        lease.Dispose();

        Assert.Equal(1, frees);
    }

    /// <summary>
    /// The other side of that race: art whose last hold has already gone must
    /// not be handed to a surface at all. A miss re-decodes; a disposed bitmap
    /// on the render thread does not fail politely.
    /// </summary>
    [Fact]
    public void Art_the_cache_has_already_freed_is_not_handed_out()
    {
        var cache = new FakeCoverCache();
        var art = new CoverArt(null!, null, _ => { });
        cache.Memory[new FakeCoverCache.Slot(Key, 160, CoverLayers.VividAndFloor)] = art;
        art.ReleaseHold();

        var pool = new CoverLeasePool(cache);
        using var lease = pool.Acquire(Key, 160);

        Assert.False(lease.TryGetArt(out _));
    }

    /// <summary>Hands out one <see cref="TaskCompletionSource{TResult}"/> per slot, completed by the test.</summary>
    private sealed class FakeCoverCache : ICoverCache
    {
        private readonly Dictionary<Slot, TaskCompletionSource<CoverArt?>> _pending = [];

        public Dictionary<Slot, CoverArt> Memory { get; } = [];

        public int Requests { get; private set; }

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
