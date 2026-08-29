using Xunit;

namespace Winnow.Covers.Tests;

/// <summary>
/// Reference counting under <see cref="CoverLeasePool"/>: one load per
/// (cover, width bucket) however many surfaces want it, per-consumer
/// cancellation that does not cancel the shared load, and nothing held once
/// the last lease is released.
///
/// <para>The payload is a <see cref="CoverArt"/> with null layers: these tests
/// start no rendering platform (see <c>DecodedLruTests</c>), and the pool
/// never looks inside the value it hands out.</para>
/// </summary>
public class CoverLeasePoolTests
{
    private static readonly CoverKey Key = CoverKey.Steam("620");

    private static CoverArt Art() => new(null!, null!);

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
        cache.Memory[(Key, 160)] = art;

        var pool = new CoverLeasePool(cache);
        using var lease = pool.Acquire(Key, 160);

        Assert.True(lease.TryGetArt(out var hit));
        Assert.Same(art, hit);
        Assert.Equal(0, cache.Requests);
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
