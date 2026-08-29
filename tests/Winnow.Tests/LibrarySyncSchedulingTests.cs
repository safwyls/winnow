using System.Text.RegularExpressions;
using Winnow.App.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// F04. The window must open on whatever the database already holds, the
/// fifteen-minute timer must drive the local job and nothing else, and the
/// entitlement backfill must be a separate, cancellable schedule of its own.
///
/// <para>Nothing here sleeps: time only moves when a test moves it, and every
/// <see cref="TimeSpan"/> that appears is a failure bound reached only when a
/// tick that should happen never does.</para>
/// </summary>
public sealed class LibrarySyncSchedulingTests
{
    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RemoteInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan FailureBound = TimeSpan.FromSeconds(20);

    /// <summary>
    /// The whole of F04's second half. The scheduler is typed to
    /// <see cref="ILocalLibrarySync"/>, so a backfill cannot ride the snapshot
    /// cadence even if someone registers one — asserted behaviourally so a
    /// future scheduler that grows a second dependency is caught too.
    /// </summary>
    [Fact]
    public async Task The_snapshot_scheduler_ticks_the_local_job_and_never_the_remote_one()
    {
        var clock = new SchedulerClock();
        var local = new FakeLocalLibrarySync();
        var remote = new FakeRemoteOwnershipSync();

        using var scheduler = new SnapshotSchedulerService(
            local,
            Options.Create(new SnapshotSchedulerOptions { Interval = SnapshotInterval }),
            NullLogger<SnapshotSchedulerService>.Instance,
            clock);

        await scheduler.StartAsync(CancellationToken.None);
        await clock.TimerCreated.WaitAsync(FailureBound);

        clock.Advance(SnapshotInterval);
        Assert.Equal(1, await local.NextCompletionAsync());
        clock.Advance(SnapshotInterval);
        Assert.Equal(2, await local.NextCompletionAsync());

        await scheduler.StopAsync(CancellationToken.None).WaitAsync(FailureBound);

        Assert.Equal(2, local.Calls);
        Assert.Equal(0, remote.Calls);
    }

    /// <summary>
    /// Starting the host must not cost a round trip. The backfill's first tick
    /// is a full interval out, so <c>host.Start()</c> returns having awaited no
    /// network at all — which is what lets the window open while the machine is
    /// offline, rate-limited or refreshing an Epic token.
    /// </summary>
    [Fact]
    public async Task Starting_the_remote_scheduler_awaits_no_backfill()
    {
        var clock = new SchedulerClock();
        var remote = new FakeRemoteOwnershipSync();

        using var scheduler = RemoteScheduler(remote, clock);

        await scheduler.StartAsync(CancellationToken.None).WaitAsync(FailureBound);
        await clock.TimerCreated.WaitAsync(FailureBound);

        // The timer is armed and the clock has not moved: nothing but the clock
        // can produce a backfill from here, so zero rules out a startup call
        // rather than merely failing to observe one.
        Assert.Equal(0, remote.Calls);

        clock.Advance(RemoteInterval - TimeSpan.FromSeconds(1));
        Assert.Equal(0, remote.Calls);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, await remote.NextCompletionAsync());

        await scheduler.StopAsync(CancellationToken.None).WaitAsync(FailureBound);
    }

    /// <summary>Mirrors <c>--no-sync</c> / <c>--seed-sample</c>.</summary>
    [Fact]
    public async Task A_disabled_remote_scheduler_never_backfills()
    {
        var clock = new SchedulerClock();
        var remote = new FakeRemoteOwnershipSync();

        using var scheduler = RemoteScheduler(remote, clock, enabled: false);

        await scheduler.StartAsync(CancellationToken.None);
        clock.Advance(RemoteInterval * 4);
        await scheduler.StopAsync(CancellationToken.None).WaitAsync(FailureBound);

        Assert.Equal(0, remote.Calls);
    }

    /// <summary>
    /// Shutdown. The window closing cancels a backfill that is mid-flight, and
    /// the loop has to unwind rather than throw out of a
    /// <c>BackgroundService</c> — the connection factory is disposed moments
    /// later, and <c>SessionJournalService</c> drains its pending writes in that
    /// same disposal.
    /// </summary>
    [Fact]
    public async Task Stopping_mid_backfill_unwinds_cleanly()
    {
        var clock = new SchedulerClock();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var remote = new FakeRemoteOwnershipSync(async (_, ct) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return FakeLocalLibrarySync.NoChangeReport;
        });

        using var scheduler = RemoteScheduler(remote, clock);

        await scheduler.StartAsync(CancellationToken.None);
        await clock.TimerCreated.WaitAsync(FailureBound);
        clock.Advance(RemoteInterval);
        await entered.Task.WaitAsync(FailureBound);

        await scheduler.StopAsync(CancellationToken.None).WaitAsync(FailureBound);

        Assert.NotNull(scheduler.ExecuteTask);
        Assert.True(scheduler.ExecuteTask.IsCompletedSuccessfully);
    }

    /// <summary>
    /// The gate exists because the resolver runs a whole pass in one SQLite
    /// transaction and there are now three callers on independent schedules.
    /// </summary>
    [Fact]
    public async Task The_sync_gate_admits_one_pass_at_a_time()
    {
        var gate = new LibrarySyncGate();

        using var first = await gate.EnterAsync(CancellationToken.None);

        var second = gate.EnterAsync(CancellationToken.None);
        Assert.False(second.IsCompleted);

        first.Dispose();
        using var admitted = await second.WaitAsync(FailureBound);
        Assert.NotNull(admitted);
    }

    /// <summary>
    /// Shutdown again, one layer down: a job cancelled while queued behind
    /// another must not leave the gate held, or the next launch's first pass
    /// would wait on a semaphore nothing will ever release.
    /// </summary>
    [Fact]
    public async Task A_cancelled_wait_on_the_gate_leaves_it_free()
    {
        var gate = new LibrarySyncGate();
        using var cts = new CancellationTokenSource();

        var held = await gate.EnterAsync(CancellationToken.None);
        var queued = gate.EnterAsync(cts.Token);

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);

        held.Dispose();
        using var next = await gate.EnterAsync(CancellationToken.None).WaitAsync(FailureBound);
        Assert.NotNull(next);
    }

    /// <summary>
    /// F04's first half, guarded where it actually lives. Program composes the
    /// startup pipeline, and the regression is one line: a blocking wait on a
    /// sync job before <c>StartWithClassicDesktopLifetime</c>. Asserted against
    /// the source because <see cref="Program.Main"/> starts a window and cannot
    /// be called from a test.
    /// </summary>
    [Fact]
    public void Program_never_blocks_on_a_sync_job()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Winnow.App", "Program.cs"));

        Assert.Empty(Regex.Matches(
            source,
            @"SyncAsync\s*\([^)]*\)\s*(\.ConfigureAwait\([^)]*\)\s*)?\.(GetAwaiter\(\)\s*\.GetResult|Wait|Result)",
            RegexOptions.Singleline));
    }

    private static RemoteOwnershipSchedulerService RemoteScheduler(
        IRemoteOwnershipSync sync, TimeProvider time, bool enabled = true)
        => new(
            sync,
            Options.Create(new RemoteOwnershipSchedulerOptions
            {
                Interval = RemoteInterval,
                Enabled = enabled,
            }),
            NullLogger<RemoteOwnershipSchedulerService>.Instance,
            time);

    /// <summary>Walks up from the test binary to the checkout that contains it.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "Winnow.App", "Program.cs")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from the test binary.");
    }
}
