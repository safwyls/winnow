using System.Net;
using Xunit;

namespace Winnow.Covers.Tests;

public sealed class SteamCoverRefreshTests
{
    [Fact]
    public async Task Disk_hit_returns_before_network_and_concurrent_hits_share_one_upgrade()
    {
        using var dir = new TempCoverDirectory();
        var options = dir.Options();
        var disk = new CoverDiskCache(options);
        var key = CoverKey.Steam("220");
        var old = TestArt.Capsule(120, 160);
        var replacement = TestArt.Capsule(120, 180);
        disk.WriteSource(key, old);
        using var cdn = new GatedCdn(replacement);
        using var pipeline = Pipeline(cdn, disk, options);

        using var cached = await pipeline.GetAsync(key, 60).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.NotNull(cached);
        Assert.Equal(80, cached.Vivid.Height);
        await cdn.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(pipeline.WaitForRefreshesAsync().IsCompleted);
        for (var i = 0; i < 10; i++)
        {
            using var again = await pipeline.GetAsync(key, 60);
            Assert.Equal(80, again!.Floor!.Height);
        }
        Assert.Equal(1, cdn.Calls);
        Assert.True(disk.HasFloor(key));

        cdn.Release.TrySetResult();
        await pipeline.WaitForRefreshesAsync();
        Assert.True(disk.TryReadSource(key, out var upgraded));
        Assert.Equal(replacement, upgraded);
        Assert.False(disk.HasFloor(key));
        using var fresh = await pipeline.GetAsync(key, 60);
        Assert.Equal(90, fresh!.Vivid.Height);
        Assert.Equal(90, fresh.Floor!.Height);
        Assert.True(disk.HasFloor(key));
        Assert.Equal(1, cdn.Calls);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("transport")]
    [InlineData("invalid")]
    [InlineData("truncated")]
    [InlineData("nonstandard")]
    public async Task Unsuccessful_upgrade_preserves_source_and_floor_and_cools_down_across_restart(string outcome)
    {
        using var dir = new TempCoverDirectory();
        var options = dir.Options();
        var clock = new MutableClock();
        var disk = new CoverDiskCache(options, clock);
        var key = CoverKey.Steam("220");
        var old = TestArt.Capsule(120, 160);
        disk.WriteSource(key, old);
        var bytes = outcome switch
        {
            "invalid" => new byte[] { 1, 2, 3 },
            "truncated" => TruncatedPortrait(),
            "nonstandard" => TestArt.Capsule(160, 90),
            _ => null,
        };
        using var cdn = new GatedCdn(bytes, outcome == "transport");
        byte[] floor;
        using (var pipeline = Pipeline(cdn, disk, options))
        {
            using var cached = await pipeline.GetAsync(key, 60);
            Assert.NotNull(cached);
            Assert.True(disk.TryReadFloor(key, out floor));
            cdn.Release.TrySetResult();
            await pipeline.WaitForRefreshesAsync();
        }
        Assert.True(disk.TryReadSource(key, out var retained));
        Assert.Equal(old, retained);
        Assert.True(disk.TryReadFloor(key, out var retainedFloor));
        Assert.Equal(floor, retainedFloor);
        Assert.False(File.Exists(disk.NegativePath(key)));
        var calls = cdn.Calls;
        using (var restarted = Pipeline(cdn, new CoverDiskCache(options, clock), options))
        {
            using var cached = await restarted.GetAsync(key, 60);
            await restarted.WaitForRefreshesAsync();
            Assert.Equal(calls, cdn.Calls);
        }
        clock.Now += outcome == "missing" ? TimeSpan.FromDays(8) : TimeSpan.FromHours(2);
        using var retried = Pipeline(cdn, disk, options);
        using var retry = await retried.GetAsync(key, 60);
        await retried.WaitForRefreshesAsync();
        Assert.True(cdn.Calls > calls);
    }

    private static byte[] TruncatedPortrait()
    {
        var complete = TestArt.Capsule(120, 180);
        return complete[..(complete.Length / 2)];
    }

    [Fact]
    public async Task Visible_hits_queue_a_bounded_number_of_keys_and_drain_one_at_a_time()
    {
        using var dir = new TempCoverDirectory();
        var options = dir.Options();
        var disk = new CoverDiskCache(options);
        var old = TestArt.Capsule(120, 160);
        using var cdn = new GatedCdn(TestArt.Capsule(120, 180));
        using var pipeline = Pipeline(cdn, disk, options);
        disk.WriteSource(CoverKey.Steam("1"), old);
        using var first = await pipeline.GetAsync(CoverKey.Steam("1"), 60);
        await cdn.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        for (var i = 2; i <= 25; i++)
        {
            var key = CoverKey.Steam(i.ToString());
            disk.WriteSource(key, old);
            using var cached = await pipeline.GetAsync(key, 60);
            Assert.NotNull(cached);
        }
        Assert.Equal(1, cdn.Calls);
        cdn.Release.TrySetResult();
        await pipeline.WaitForRefreshesAsync();
        Assert.Equal(17, cdn.Calls);
        Assert.True(disk.TryReadSource(CoverKey.Steam("25"), out var dropped));
        Assert.Equal(old, dropped);
    }

    [Fact]
    public async Task Disposal_cancels_the_tracked_upgrade_without_changing_cached_pixels()
    {
        using var dir = new TempCoverDirectory();
        var options = dir.Options();
        var disk = new CoverDiskCache(options);
        var key = CoverKey.Steam("220");
        var old = TestArt.Capsule(120, 160);
        disk.WriteSource(key, old);
        using var cdn = new GatedCdn(TestArt.Capsule(120, 180));
        var pipeline = Pipeline(cdn, disk, options);
        using var cached = await pipeline.GetAsync(key, 60);
        await cdn.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        pipeline.Dispose();
        await pipeline.WaitForRefreshesAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(disk.TryReadSource(key, out var retained));
        Assert.Equal(old, retained);
        Assert.True(disk.HasFloor(key));
    }

    [Fact]
    public async Task Only_nonstandard_steam_keys_with_published_lookup_are_eligible()
    {
        using var dir = new TempCoverDirectory();
        var options = dir.Options();
        var disk = new CoverDiskCache(options);
        using var cdn = new GatedCdn(TestArt.Capsule(120, 180));
        using var pipeline = Pipeline(cdn, disk, options);
        foreach (var key in new[] { CoverKey.User("custom"), CoverKey.Igdb("cover"), CoverKey.Steam("220") })
        {
            disk.WriteSource(key, TestArt.Capsule(120, key.Provider == CoverProviders.Steam ? 180 : 160));
            using var cached = await pipeline.GetAsync(key, 60);
            Assert.NotNull(cached);
        }
        await pipeline.WaitForRefreshesAsync();
        Assert.Equal(0, cdn.Calls);

        var legacy = CoverKey.Steam("221");
        disk.WriteSource(legacy, TestArt.Capsule(120, 160));
        using var legacyPipeline = new CoverPipeline([new SteamCapsuleSource(cdn, options)], disk, options);
        using var legacyCached = await legacyPipeline.GetAsync(legacy, 60);
        await legacyPipeline.WaitForRefreshesAsync();
        Assert.Equal(0, cdn.Calls);
    }

    private static CoverPipeline Pipeline(GatedCdn cdn, CoverDiskCache disk, CoverCacheOptions options) =>
        new([new SteamCapsuleSource(cdn, options, assets: new EmptyLookup())], disk, options);

    private sealed class EmptyLookup : ISteamLibraryAssetLookup
    {
        public Task<IReadOnlyList<string>?> GetPathsAsync(CoverKey key, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>?>(Array.Empty<string>());
    }

    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class GatedCdn(byte[]? bytes, bool fail = false) : HttpMessageHandler, IHttpClientFactory
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
            if (fail) throw new HttpRequestException("Temporary CDN failure");
            return bytes is null ? new(HttpStatusCode.NotFound) : new(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes),
            };
        }
    }
}
