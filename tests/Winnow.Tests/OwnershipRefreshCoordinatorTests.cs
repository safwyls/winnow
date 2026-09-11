using Microsoft.Extensions.Logging.Abstractions;
using Winnow.App.Services;
using Xunit;

namespace Winnow.Tests;

public sealed class OwnershipRefreshCoordinatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Committed_ownership_is_published_before_metadata_even_when_a_later_ownership_operation_failed(bool fail)
    {
        var calls = new List<string>();
        var failure = new IOException("Synthetic failure after commit");
        var remote = new Remote(_ =>
        {
            calls.Add("ownership");
            return fail ? Task.FromException<LibrarySyncReport>(failure) : Task.FromResult(Report);
        });
        var pipeline = Pipeline([new("metadata", _ => { calls.Add("metadata"); return Task.CompletedTask; })],
            _ => { calls.Add("publish"); return Task.CompletedTask; });
        var coordinator = Coordinator(remote, pipeline);
        if (fail) Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => coordinator.SyncAsync()));
        else Assert.Same(Report, await coordinator.SyncAsync());
        Assert.Equal(["ownership", "publish", "metadata", "publish"], calls);
    }

    [Fact]
    public async Task Independent_phase_failure_and_failed_publication_do_not_hide_later_commits()
    {
        var calls = new List<string>();
        var publishes = 0;
        var pipeline = Pipeline([
            new("unavailable", _ => throw new IOException(), PublishAfter: true),
            new("updates", _ => { calls.Add("updates"); return Task.CompletedTask; })], _ =>
            {
                if (++publishes == 1) throw new IOException();
                calls.Add("published"); return Task.CompletedTask;
            });
        await pipeline.RunAsync();
        Assert.Equal(["updates", "published"], calls);
        Assert.Equal(2, publishes);
    }

    [Fact]
    public async Task Credential_refresh_selects_the_same_IGDB_steps_without_running_unrelated_services()
    {
        var calls = new List<string>();
        var pipeline = Pipeline([
            new("history", _ => throw new InvalidOperationException("Must not run")),
            new("facets", _ => { calls.Add("facets"); return Task.CompletedTask; }, IgdbRelevant: true),
            new("lifecycle", _ => { calls.Add("lifecycle"); return Task.CompletedTask; }, IgdbRelevant: true)],
            _ => { calls.Add("publish"); return Task.CompletedTask; });
        await pipeline.RunAsync(igdbOnly: true);
        Assert.Equal(["facets", "lifecycle", "publish"], calls);
    }

    [Fact]
    public async Task Concurrent_entry_points_serialize_the_entire_pass_and_cancelled_waiters_do_not_start()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ownershipCalls = 0;
        var coordinator = Coordinator(new Remote(_ => { ownershipCalls++; return Task.FromResult(Report); }),
            Pipeline([new("metadata", async ct => { entered.TrySetResult(); await release.Task.WaitAsync(ct); })], _ => Task.CompletedTask));
        var first = coordinator.SyncAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var stop = new CancellationTokenSource();
        var cancelled = coordinator.SyncAsync(stop.Token);
        var second = coordinator.SyncAsync();
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Equal(1, ownershipCalls);
        release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, ownershipCalls);
    }

    [Fact]
    public async Task Cancellation_after_ownership_prevents_any_publication_or_metadata()
    {
        using var stop = new CancellationTokenSource();
        var coordinator = Coordinator(new Remote(_ => { stop.Cancel(); return Task.FromResult(Report); }),
            Pipeline([new("metadata", _ => throw new InvalidOperationException("Must not run"))],
                _ => throw new InvalidOperationException("Must not publish")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.SyncAsync(stop.Token));
    }

    private static readonly LibrarySyncReport Report = new(1, null, TimeSpan.Zero);
    private static LibraryRefreshPipeline Pipeline(IReadOnlyList<LibraryRefreshStep> steps, Func<CancellationToken, Task> publish)
        => new(steps, publish, NullLogger<LibraryRefreshPipeline>.Instance);
    private static OwnershipRefreshCoordinator Coordinator(IRemoteOwnershipSync remote, LibraryRefreshPipeline pipeline)
        => new(remote, pipeline, NullLogger<OwnershipRefreshCoordinator>.Instance);
    private sealed class Remote(Func<CancellationToken, Task<LibrarySyncReport>> run) : IRemoteOwnershipSync
    {
        public Task<LibrarySyncReport> SyncAsync(CancellationToken ct = default) => run(ct);
        public Task<LibrarySyncReport> SyncAsync(LocalLibraryScan scan, CancellationToken ct = default) => run(ct);
    }
}
