using Xunit;

namespace Winnow.Covers.Tests;

public sealed class CoverLeaseInterleavingTests
{
    [Fact]
    public async Task A_late_waiter_returns_the_replacement_held_by_its_slot_after_eviction()
    {
        var cache = new ReplacingCache();
        var pool = new CoverLeasePool(cache);
        using var first = pool.Acquire(CoverKey.Steam("42"), 160);
        using var second = pool.Acquire(CoverKey.Steam("42"), 160);
        var firstLoad = first.GetAsync();
        var secondLoad = second.GetAsync();
        cache.Evicted.ReleaseHold();
        Assert.Equal(0, cache.Evicted.Holds);

        // Inline continuations deliberately order the first waiter's retry and
        // hold before the second waiter receives the original, evicted result.
        cache.Original.SetResult(cache.Evicted);
        var results = await Task.WhenAll(firstLoad, secondLoad);
        Assert.Equal(2, cache.Requests);
        Assert.All(results, result => Assert.Same(cache.Replacement, result));
        Assert.All(results, result => Assert.True(result!.Holds > 0));
        Assert.True(second.TryGetArt(out var held));
        Assert.Same(held, results[1]);
    }

    private sealed class ReplacingCache : ICoverCache
    {
        public CoverArt Evicted { get; } = new(null!, null);
        public CoverArt Replacement { get; } = new(null!, null);
        public TaskCompletionSource<CoverArt?> Original { get; } = new();
        public int Requests;
        public bool TryGet(CoverKey key, double displayWidthPixels, CoverLayers layers, out CoverArt art)
        {
            art = null!;
            return false;
        }
        public Task<CoverArt?> GetAsync(CoverKey key, double displayWidthPixels,
            CoverLayers layers, CancellationToken ct = default)
            => ++Requests == 1 ? Original.Task : Task.FromResult<CoverArt?>(Replacement);
    }
}
