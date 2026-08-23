using Xunit;

namespace Hoard.Covers.Tests;

public class CoverPipelineTests
{
    [Fact]
    public async Task Second_request_is_served_from_disk_without_refetching()
    {
        using var dir = new TempCoverDirectory();
        var cdn = new FakeCoverCdn();
        cdn.AddCapsule("220", TestArt.Capsule());

        using (var pipeline = dir.Pipeline(cdn))
        {
            using var first = await pipeline.GetAsync(CoverKey.Steam("220"), 320);
            Assert.NotNull(first);
        }

        Assert.Equal(1, cdn.RequestCount);

        // A fresh pipeline is the real question: does the *next launch* re-fetch?
        using (var pipeline = dir.Pipeline(cdn))
        {
            using var second = await pipeline.GetAsync(CoverKey.Steam("220"), 320);
            Assert.NotNull(second);
        }

        Assert.Equal(1, cdn.RequestCount);
    }

    [Fact]
    public async Task Both_layers_are_cached_on_disk_beside_the_original()
    {
        using var dir = new TempCoverDirectory();
        var cdn = new FakeCoverCdn();
        cdn.AddCapsule("220", TestArt.Capsule());

        using var pipeline = dir.Pipeline(cdn);
        using var art = await pipeline.GetAsync(CoverKey.Steam("220"), 320);
        Assert.NotNull(art);

        var disk = new CoverDiskCache(dir.Options());
        Assert.True(File.Exists(disk.SourcePath(CoverKey.Steam("220"))));
        Assert.True(File.Exists(disk.FloorPath(CoverKey.Steam("220"))));
    }

    [Fact]
    public async Task Missing_capsule_is_remembered_and_never_refetched()
    {
        using var dir = new TempCoverDirectory();
        var cdn = new FakeCoverCdn();

        // 228980 is Steamworks Common Redistributables — a real appid with no
        // capsule. Both the 2x and 1x paths 404, then we stop asking.
        var key = CoverKey.Steam("228980");

        using (var pipeline = dir.Pipeline(cdn))
        {
            Assert.Null(await pipeline.GetAsync(key, 320));
            var afterFirst = cdn.RequestCount;
            Assert.Equal(2, afterFirst); // _2x then the 1x fallback, once each.

            Assert.Null(await pipeline.GetAsync(key, 320));
            Assert.Equal(afterFirst, cdn.RequestCount);
            Assert.True(pipeline.IsKnownMissing(key));
        }

        Assert.True(File.Exists(new CoverDiskCache(dir.Options()).NegativePath(key)));

        // And the negative result survives the process, not just the instance.
        using (var pipeline = dir.Pipeline(cdn))
        {
            Assert.True(pipeline.IsKnownMissing(key));
            Assert.Null(await pipeline.GetAsync(key, 320));
            Assert.Equal(2, cdn.RequestCount);
        }
    }

    [Fact]
    public async Task Expired_negative_marker_is_retried()
    {
        using var dir = new TempCoverDirectory();
        var cdn = new FakeCoverCdn();
        var key = CoverKey.Steam("228980");
        var options = dir.Options();
        options.NegativeTtl = TimeSpan.FromDays(30);

        using (var pipeline = dir.Pipeline(cdn, options))
        {
            Assert.Null(await pipeline.GetAsync(key, 320));
        }

        var marker = new CoverDiskCache(options).NegativePath(key);
        File.SetLastWriteTimeUtc(marker, DateTime.UtcNow - TimeSpan.FromDays(45));

        // A capsule that appears later (a store page finally getting art) must
        // not be locked out forever by one 404.
        cdn.AddCapsule("228980", TestArt.Capsule());
        using (var pipeline = dir.Pipeline(cdn, options))
        {
            using var art = await pipeline.GetAsync(key, 320);
            Assert.NotNull(art);
        }
    }

    [Fact]
    public async Task Concurrent_requests_stay_within_the_fetch_bound()
    {
        using var dir = new TempCoverDirectory();
        var options = dir.Options();
        options.MaxConcurrentFetches = 3;

        var cdn = new ConcurrencyProbe();
        for (var i = 0; i < 40; i++)
        {
            cdn.AddCapsule(i.ToString(), TestArt.Capsule(300, 450));
        }

        using var pipeline = dir.Pipeline(cdn, options);
        var loads = Enumerable.Range(0, 40)
            .Select(async i =>
            {
                using var art = await pipeline.GetAsync(CoverKey.Steam(i.ToString()), 160);
                Assert.NotNull(art);
            });

        await Task.WhenAll(loads);
        Assert.True(cdn.PeakConcurrency <= 3, $"peak concurrency was {cdn.PeakConcurrency}");
    }

    private sealed class ConcurrencyProbe : FakeCoverCdn
    {
        private int _current;
        private int _peak;

        public int PeakConcurrency => Volatile.Read(ref _peak);

        protected override async Task<HttpResponseMessage> OnSendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var now = Interlocked.Increment(ref _current);
            int peak;
            while (now > (peak = Volatile.Read(ref _peak)))
            {
                Interlocked.CompareExchange(ref _peak, now, peak);
            }

            try
            {
                await Task.Delay(15, ct);
                return await base.OnSendAsync(request, ct);
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
        }
    }
}
