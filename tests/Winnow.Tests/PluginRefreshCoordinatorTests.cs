using Winnow.App.Services;
using Xunit;

namespace Winnow.Tests;

public sealed class PluginRefreshCoordinatorTests
{
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task Imported_rows_publish_before_enrichment_and_account_changes_do_not_wait_for_the_sweep()
    {
        using var stop = new CancellationTokenSource();
        var discovery = Signal();
        var enrichmentStarted = Signal();
        var secondPublication = Signal();
        var imports = 0;
        var publications = 0;
        var failures = new List<Exception>();
        var refresh = new PluginRefreshCoordinator(discovery.Task,
            _ => { Interlocked.Increment(ref imports); return Task.CompletedTask; },
            async ct =>
            {
                Assert.True(Volatile.Read(ref publications) >= 1);
                enrichmentStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            },
            _ =>
            {
                if (Interlocked.Increment(ref publications) == 2) secondPublication.TrySetResult();
                return Task.CompletedTask;
            }, failures.Add, stop.Token);
        try
        {
            refresh.Request();
            Assert.Equal(0, Volatile.Read(ref imports));
            discovery.SetResult();
            await enrichmentStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, Volatile.Read(ref imports));
            refresh.Request();
            await secondPublication.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, Volatile.Read(ref imports));
            Assert.Empty(failures);
        }
        finally
        {
            await stop.CancelAsync();
            await refresh.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Partial_import_failure_still_publishes_and_a_later_request_recovers()
    {
        using var stop = new CancellationTokenSource();
        var failed = Signal();
        var recovered = Signal();
        var calls = 0;
        var publications = 0;
        var refresh = new PluginRefreshCoordinator(Task.CompletedTask,
            _ => Interlocked.Increment(ref calls) == 1
                ? Task.FromException(new InvalidOperationException("Synthetic import failure")) : Task.CompletedTask,
            _ => Task.CompletedTask,
            _ =>
            {
                Interlocked.Increment(ref publications);
                if (Volatile.Read(ref calls) > 1) recovered.TrySetResult();
                return Task.CompletedTask;
            }, _ => failed.TrySetResult(), stop.Token);
        try
        {
            refresh.Request();
            await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(Volatile.Read(ref publications) >= 1);
            refresh.Request();
            await recovered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, Volatile.Read(ref calls));
        }
        finally
        {
            await stop.CancelAsync();
            await refresh.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Later_builtin_startup_enriches_its_new_works_without_delaying_the_initial_plugin_import()
    {
        using var stop = new CancellationTokenSource();
        var startup = Signal();
        var firstEnrichment = Signal();
        var laterEnrichment = Signal();
        var imports = 0;
        var sweeps = 0;
        var refresh = new PluginRefreshCoordinator(Task.CompletedTask,
            _ => { Interlocked.Increment(ref imports); return Task.CompletedTask; },
            _ =>
            {
                if (Interlocked.Increment(ref sweeps) == 1) firstEnrichment.TrySetResult();
                else laterEnrichment.TrySetResult();
                return Task.CompletedTask;
            }, _ => Task.CompletedTask, _ => Assert.Fail("Unexpected failure"), stop.Token, libraryStartup: startup.Task);
        try
        {
            refresh.Request();
            await firstEnrichment.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(startup.Task.IsCompleted);
            Assert.Equal(1, Volatile.Read(ref imports));
            startup.SetResult();
            await laterEnrichment.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, Volatile.Read(ref imports));
        }
        finally
        {
            await stop.CancelAsync();
            await refresh.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Shutdown_cancels_both_queues_while_discovery_is_pending()
    {
        using var stop = new CancellationTokenSource();
        var discovery = Signal();
        var refresh = new PluginRefreshCoordinator(discovery.Task,
            _ => throw new InvalidOperationException("Import must wait for discovery"),
            _ => throw new InvalidOperationException("Enrichment must wait for discovery"),
            _ => throw new InvalidOperationException("Nothing has been committed"),
            _ => Assert.Fail("No work should run"), stop.Token);
        refresh.Request();
        await stop.CancelAsync();
        await refresh.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(discovery.Task.IsCompleted);
    }
}
