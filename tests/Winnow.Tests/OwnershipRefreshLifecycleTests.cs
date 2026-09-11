using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Winnow.App.Services;
using Xunit;

namespace Winnow.Tests;

public sealed class OwnershipRefreshLifecycleTests
{
    [Fact]
    public async Task Account_changes_coalesce_behind_startup_and_keep_one_followup_during_the_full_operation()
    {
        using var stop = new CancellationTokenSource();
        var startup = Signal();
        var metadataEntered = Signal();
        var releaseMetadata = Signal();
        var finished = Signal();
        var accounts = new ConcurrentQueue<string>();
        var currentAccount = "account-a";
        var metadataCalls = 0;
        var completed = 0;
        var coordinator = Coordinator(new Remote(_ =>
        {
            accounts.Enqueue(Volatile.Read(ref currentAccount));
            return Task.FromResult(Report);
        }), Pipeline([new("metadata", async ct =>
        {
            if (Interlocked.Increment(ref metadataCalls) != 1) return;
            metadataEntered.TrySetResult();
            await releaseMetadata.Task.WaitAsync(ct);
        })]));
        var queue = new CredentialMetadataRefresh(startup.Task, async ct =>
        {
            await coordinator.SyncAsync(ct);
            if (Interlocked.Increment(ref completed) == 2) finished.TrySetResult();
        }, ex => finished.TrySetException(ex), stop.Token);
        var requests = new OwnershipRefreshRequests();
        requests.Requested += queue.Request;
        try
        {
            for (var i = 0; i < 20; i++) requests.Request();
            Assert.Empty(accounts);
            startup.TrySetResult();
            await metadataEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Volatile.Write(ref currentAccount, "account-b");
            for (var i = 0; i < 20; i++) requests.Request();
            Assert.Equal(["account-a"], accounts);
            releaseMetadata.TrySetResult();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await stop.CancelAsync();
            releaseMetadata.TrySetResult();
            await queue.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(["account-a", "account-b"], accounts);
        Assert.Equal(2, metadataCalls);
        Assert.Equal(2, completed);
    }

    [Fact]
    public async Task Shutdown_cancels_active_metadata_discards_queued_account_changes_and_releases_both_gates()
    {
        using var stop = new CancellationTokenSource();
        var metadataEntered = Signal();
        var ownershipCalls = 0;
        var metadataCalls = 0;
        var publications = 0;
        var coordinator = Coordinator(new Remote(_ =>
        {
            Interlocked.Increment(ref ownershipCalls);
            return Task.FromResult(Report);
        }), Pipeline([new("metadata", async ct =>
        {
            if (Interlocked.Increment(ref metadataCalls) != 1) return;
            metadataEntered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        })], _ => { Interlocked.Increment(ref publications); return Task.CompletedTask; }));
        var queue = new CredentialMetadataRefresh(Task.CompletedTask,
            async ct => { await coordinator.SyncAsync(ct); }, _ => Assert.Fail("Shutdown must be cancellation"), stop.Token);
        queue.Request();
        await metadataEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        queue.Request();
        await stop.CancelAsync();
        await queue.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, ownershipCalls);
        Assert.Equal(1, metadataCalls);
        Assert.Equal(1, publications);

        await coordinator.SyncAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, ownershipCalls);
        Assert.Equal(2, metadataCalls);
        Assert.Equal(3, publications);
    }

    [Fact]
    public async Task Igdb_and_ownership_refresh_share_one_downstream_pipeline_without_delaying_committed_ownership_publication()
    {
        var metadataEntered = Signal();
        var releaseMetadata = Signal();
        var ownershipPublished = Signal();
        var calls = new ConcurrentQueue<string>();
        var metadataCalls = 0;
        var pipeline = Pipeline([
            new("history", _ => { calls.Enqueue("history"); return Task.CompletedTask; }),
            new("metadata", async ct =>
            {
                var first = Interlocked.Increment(ref metadataCalls) == 1;
                calls.Enqueue(first ? "igdb metadata:start" : "ownership metadata:start");
                if (first)
                {
                    metadataEntered.TrySetResult();
                    await releaseMetadata.Task.WaitAsync(ct);
                }
                calls.Enqueue(first ? "igdb metadata:end" : "ownership metadata:end");
            }, IgdbRelevant: true)], _ =>
            {
                calls.Enqueue("publish");
                ownershipPublished.TrySetResult();
                return Task.CompletedTask;
            });
        var coordinator = Coordinator(new Remote(_ =>
        {
            calls.Enqueue("ownership");
            return Task.FromResult(Report);
        }), pipeline);
        var igdb = pipeline.RunAsync(igdbOnly: true);
        await metadataEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var ownership = coordinator.SyncAsync();
        try
        {
            await ownershipPublished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(["igdb metadata:start", "ownership", "publish"], calls);
        }
        finally { releaseMetadata.TrySetResult(); }
        await Task.WhenAll(igdb, ownership).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal([
            "igdb metadata:start", "ownership", "publish", "igdb metadata:end", "publish",
            "history", "ownership metadata:start", "ownership metadata:end", "publish"], calls);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static readonly LibrarySyncReport Report = new(1, null, TimeSpan.Zero);
    private static LibraryRefreshPipeline Pipeline(IReadOnlyList<LibraryRefreshStep> steps,
        Func<CancellationToken, Task>? publish = null)
        => new(steps, publish ?? (_ => Task.CompletedTask), NullLogger<LibraryRefreshPipeline>.Instance);
    private static OwnershipRefreshCoordinator Coordinator(IRemoteOwnershipSync remote, LibraryRefreshPipeline pipeline)
        => new(remote, pipeline, NullLogger<OwnershipRefreshCoordinator>.Instance);
    private sealed class Remote(Func<CancellationToken, Task<LibrarySyncReport>> run) : IRemoteOwnershipSync
    {
        public Task<LibrarySyncReport> SyncAsync(CancellationToken ct = default) => run(ct);
        public Task<LibrarySyncReport> SyncAsync(LocalLibraryScan scan, CancellationToken ct = default) => run(ct);
    }
}
