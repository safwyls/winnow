using SkiaSharp;
using Winnow.Covers.Igdb;
using Xunit;

namespace Winnow.Covers.Tests;

public sealed class IgdbCoverCancellationTests
{
    [Fact]
    public async Task Six_cancelled_fallbacks_release_fetch_permits_before_shared_prewarm_completes()
    {
        using var directory = new TempCoverDirectory();
        using var cdn = new FakeCoverCdn();
        using var cancelled = new CancellationTokenSource();
        using var decodeGate = new SemaphoreSlim(1);
        var options = directory.Options();
        options.MaxConcurrentFetches = 6;
        var disk = new CoverDiskCache(options);
        var batch = new HeldBatch();
        var igdb = new FakeIgdbClient { BeforeResolve = batch.WaitAsync };
        var jpeg = TestArt.Capsule(160, 240);
        var keys = Enumerable.Range(9000, 6).Select(id => CoverKey.Steam(id.ToString())).ToArray();
        var survivorKey = CoverKey.Steam("9999");
        foreach (var key in keys.Append(survivorKey))
        {
            // Old Steam-only misses seed IGDB's shared prewarm on the next run.
            disk.MarkMissing(key, "steam-capsule");
            igdb.AddCover(key.Id, "co" + key.Id);
            cdn.AddIgdbCover("co" + key.Id, jpeg);
        }
        cdn.AddIgdbCover("fresh123", jpeg);
        var source = new ObservedSource(new IgdbCoverSource(igdb, cdn, options, new IgdbCoverOptions()), 6);
        using var pipeline = directory.Pipeline(options, new SteamCapsuleSource(cdn, options), source);
        var requests = keys.Select(key => pipeline.GetPngAsync(key, 160, decodeGate, cancelled.Token)).ToList();
        try
        {
            await source.AllEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await batch.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.All(requests, request => Assert.False(request.IsCompleted));
            Assert.False(batch.Token.CanBeCanceled);

            cancelled.Cancel();
            var direct = pipeline.GetPngAsync(CoverKey.Igdb("fresh123"), 160, decodeGate);
            requests.Add(direct);
            var png = await direct.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.NotNull(png);
            using var decoded = SKBitmap.Decode(png);
            Assert.Equal((160, 240), (decoded.Width, decoded.Height));
            Assert.False(batch.Release.Task.IsCompleted);
            Assert.All(await Task.WhenAll(requests.Take(6)).WaitAsync(TimeSpan.FromSeconds(2)), Assert.Null);

            var survivor = pipeline.GetPngAsync(survivorKey, 160, decodeGate);
            requests.Add(survivor);
            Assert.False(survivor.IsCompleted);
            batch.Release.TrySetResult();
            Assert.NotNull(await survivor.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.Equal(1, igdb.BatchCount);
        }
        finally
        {
            cancelled.Cancel();
            batch.Release.TrySetResult();
            await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Caller_filling_batch_cancels_promptly_without_cancelling_remaining_waiter()
    {
        using var cancelled = new CancellationTokenSource();
        var batch = new HeldBatch();
        var igdb = new FakeIgdbClient { BeforeResolve = batch.WaitAsync };
        igdb.AddCover("9000", "co9000");
        igdb.AddCover("9001", "co9001");
        var lookup = new IgdbSteamCoverLookup(igdb, new IgdbCoverOptions
        {
            MaxBatchSize = 2,
            BatchLinger = TimeSpan.FromSeconds(30),
            PrewarmFromNegativeMarkers = false,
        });
        var survivor = lookup.GetCoverUrlAsync("9000", CancellationToken.None);
        var triggeringCaller = lookup.GetCoverUrlAsync("9001", cancelled.Token);
        try
        {
            await batch.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                triggeringCaller.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.False(survivor.IsCompleted);
            Assert.False(batch.Token.CanBeCanceled);
            Assert.False(batch.Release.Task.IsCompleted);

            batch.Release.TrySetResult();
            Assert.EndsWith("/co9000.jpg", await survivor.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.EndsWith("/co9001.jpg", await lookup.GetCoverUrlAsync("9001", CancellationToken.None));
            Assert.Equal(1, igdb.BatchCount);
        }
        finally
        {
            batch.Release.TrySetResult();
            try { await Task.WhenAll(survivor, triggeringCaller).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
        }
    }

    private sealed class HeldBatch
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }

        public async Task WaitAsync(CancellationToken ct)
        {
            Token = ct;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
        }
    }

    private sealed class ObservedSource(ICoverSource source, int expected) : ICoverSource
    {
        private int _entered;
        public TaskCompletionSource AllEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Name => source.Name;
        public string SourceSetId => source.SourceSetId;
        public bool CanHandle(CoverKey key) => source.CanHandle(key);
        public ValueTask RefreshCapabilityAsync(CoverKey key, CancellationToken ct = default)
            => source.RefreshCapabilityAsync(key, ct);

        public Task<byte[]?> TryFetchAsync(CoverKey key, CancellationToken ct = default)
        {
            if (key.Provider == CoverProviders.Steam && Interlocked.Increment(ref _entered) == expected)
            {
                AllEntered.TrySetResult();
            }
            return source.TryFetchAsync(key, ct);
        }
    }
}
