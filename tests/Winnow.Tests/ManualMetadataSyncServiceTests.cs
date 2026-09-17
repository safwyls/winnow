using Microsoft.Extensions.Logging.Abstractions;
using Winnow.App.Services;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Model;
using Xunit;

namespace Winnow.Tests;

public sealed class ManualMetadataSyncServiceTests
{
    [Fact]
    public async Task Missing_credentials_runs_no_steps_or_publication()
    {
        var calls = 0;
        var service = Service(false, [new("metadata", _ => { calls++; return Task.CompletedTask; }, IgdbRelevant: true)],
            _ => { calls++; return Task.CompletedTask; });
        Assert.Equal(MetadataSyncResult.MissingCredentials, await service.SyncAsync());
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Manual_sync_selects_only_metadata_and_reports_progress_before_publication()
    {
        var calls = new List<string>();
        var progress = new ImmediateProgress(value => calls.Add(value));
        var service = Service(true, [
            new("ownership", _ => { calls.Add("ownership"); return Task.CompletedTask; }),
            new("metadata", _ => { calls.Add("metadata-run"); return Task.CompletedTask; }, IgdbRelevant: true),
            new("plugins", _ => { calls.Add("plugins"); return Task.CompletedTask; }),
            new("facets", _ => { calls.Add("facets-run"); return Task.CompletedTask; }, IgdbRelevant: true)],
            _ => { calls.Add("publish"); return Task.CompletedTask; });

        Assert.Equal(MetadataSyncResult.Completed, await service.SyncAsync(progress));
        Assert.Equal(["Waiting for metadata sync…", "metadata", "metadata-run", "facets", "facets-run",
            "Updating library…", "publish"], calls);
    }

    [Fact]
    public async Task Failed_stage_continues_and_returns_partial_failure()
    {
        var laterRan = false;
        var published = false;
        var service = Service(true, [
            new("metadata", _ => throw new IOException(), IgdbRelevant: true),
            new("facets", _ => { laterRan = true; return Task.CompletedTask; }, IgdbRelevant: true)],
            _ => { published = true; return Task.CompletedTask; });
        Assert.Equal(MetadataSyncResult.PartialFailure, await service.SyncAsync());
        Assert.True(laterRan);
        Assert.True(published);
    }

    [Fact]
    public async Task Failed_publication_is_not_reported_as_success()
    {
        var service = Service(true, [new("metadata", _ => Task.CompletedTask, IgdbRelevant: true)],
            _ => throw new IOException());
        Assert.Equal(MetadataSyncResult.RefreshFailed, await service.SyncAsync());
    }

    [Fact]
    public async Task Manual_sync_waits_for_existing_pipeline_and_cancelled_waiter_never_runs()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var pipeline = Pipeline([new("metadata", async ct =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
        }, IgdbRelevant: true)], _ => Task.CompletedTask);
        var automatic = pipeline.RunAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var service = new ManualMetadataSyncService(new Igdb(true), pipeline);
        using var stop = new CancellationTokenSource();
        var cancelled = service.SyncAsync(new ImmediateProgress(_ => waiting.TrySetResult()), stop.Token);
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Equal(1, calls);
        var second = service.SyncAsync();
        release.SetResult();
        await automatic.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(MetadataSyncResult.Completed, await second.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Cancelled_stage_does_not_publish_and_releases_gate()
    {
        using var stop = new CancellationTokenSource();
        var published = 0;
        var calls = 0;
        var service = Service(true, [new("metadata", ct =>
        {
            if (++calls == 1) stop.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }, IgdbRelevant: true)], _ => { published++; return Task.CompletedTask; });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SyncAsync(ct: stop.Token));
        Assert.Equal(0, published);
        Assert.Equal(MetadataSyncResult.Completed, await service.SyncAsync());
        Assert.Equal(1, published);
    }

    private static LibraryRefreshPipeline Pipeline(IReadOnlyList<LibraryRefreshStep> steps, Func<CancellationToken, Task> publish)
        => new(steps, publish, NullLogger<LibraryRefreshPipeline>.Instance);

    private static ManualMetadataSyncService Service(bool configured, IReadOnlyList<LibraryRefreshStep> steps,
        Func<CancellationToken, Task> publish) => new(new Igdb(configured), Pipeline(steps, publish));

    private sealed class ImmediateProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }

    private sealed class Igdb(bool configured) : IIgdbClient
    {
        public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default) => ValueTask.FromResult(configured);
        public Task<IReadOnlyList<IgdbGame>> GetGamesAsync(IEnumerable<long> igdbIds, TimeSpan? cacheTtl = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, IgdbExternalMatch>> ResolveBySteamAppIdsAsync(IEnumerable<string> appIds, TimeSpan? cacheTtl = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, IgdbExternalMatch>> ResolveByExternalIdsAsync(int externalGameSourceId, IEnumerable<string> uids, TimeSpan? cacheTtl = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<long, IgdbAgeRatings>> GetAgeRatingsAsync(IEnumerable<long> igdbIds, TimeSpan? cacheTtl = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IgdbSearchResult>> SearchGamesAsync(string title, int limit = 0, TimeSpan? cacheTtl = null, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
