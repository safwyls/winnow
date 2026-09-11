using Xunit;

namespace Winnow.Covers.Tests;

public sealed class CoverRetryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Warm_memory_and_disk_negatives_expire_at_the_same_original_deadline(bool diskFirst)
    {
        using var directory = new TempCoverDirectory();
        var options = directory.Options();
        options.NegativeTtl = TimeSpan.FromHours(1);
        var clock = new Clock();
        var disk = new CoverDiskCache(options, clock);
        var source = new Source();
        var key = CoverKey.Steam("42");
        if (diskFirst) disk.MarkMissing(key, CoverSourceSet.Identity([source], key));
        using var pipeline = new CoverPipeline([source], disk, options);
        Assert.Null(await pipeline.GetAsync(key, 160));
        var calls = source.Calls;
        clock.Now += TimeSpan.FromMinutes(59);
        Assert.True(pipeline.IsKnownMissing(key));
        Assert.Null(await pipeline.GetAsync(key, 160));
        Assert.Equal(calls, source.Calls);
        source.Bytes = TestArt.Capsule(160, 240);
        clock.Now += TimeSpan.FromMinutes(1);
        Assert.False(pipeline.IsKnownMissing(key));
        Assert.False(disk.IsKnownMissing(key, pipeline.SourceSetIdFor(key)));
        using var art = await pipeline.GetAsync(key, 160);
        Assert.NotNull(art);
        Assert.Equal(calls + 1, source.Calls);
        Assert.False(pipeline.IsKnownMissing(key));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_retained_lease_retries_failure_without_duplicate_concurrent_loads(bool cancelled)
    {
        var cache = new RetryCache(cancelled);
        var pool = new CoverLeasePool(cache);
        using var first = pool.Acquire(CoverKey.Steam("42"), 160);
        using var second = pool.Acquire(CoverKey.Steam("42"), 160);
        if (cancelled) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.GetAsync());
        else Assert.Null(await first.GetAsync());
        var wanted = first.GetAsync();
        var alsoWanted = second.GetAsync();
        Assert.Equal(2, cache.Calls);
        var art = new CoverArt(null!, null);
        cache.Result.SetResult(art);
        Assert.Same(art, await wanted);
        Assert.Same(art, await alsoWanted);
        Assert.Equal(2, art.Holds);
        art.ReleaseHold();
        first.Dispose();
        Assert.Equal(1, art.Holds);
        second.Dispose();
        Assert.Equal(0, art.Holds);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Source : ICoverSource
    {
        public string Name => "fixture";
        public int Calls { get; private set; }
        public byte[]? Bytes { get; set; }
        public bool CanHandle(CoverKey key) => true;
        public Task<byte[]?> TryFetchAsync(CoverKey key, CancellationToken ct = default)
        { Calls++; return Task.FromResult(Bytes); }
    }

    private sealed class RetryCache(bool cancelled) : ICoverCache
    {
        public int Calls { get; private set; }
        public TaskCompletionSource<CoverArt?> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool TryGet(CoverKey key, double width, CoverLayers layers, out CoverArt art)
        { art = null!; return false; }
        public Task<CoverArt?> GetAsync(CoverKey key, double width, CoverLayers layers, CancellationToken ct = default)
            => ++Calls == 1 ? cancelled ? Task.FromCanceled<CoverArt?>(new CancellationToken(true)) : Task.FromResult<CoverArt?>(null) : Result.Task;
    }
}
