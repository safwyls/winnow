using System.Collections.Concurrent;
using Winnow.App.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Scheduling behaviour of the M2 snapshot scheduler, driven entirely by a fake
/// clock: the tick cadence, the deliberate full-interval gap before the first
/// tick, non-overlap when a scan overruns, and clean shutdown mid-scan.
///
/// <para>Nothing here sleeps. Time only moves when a test moves it, so a suite
/// asserting about 15-minute intervals runs in milliseconds; the timeouts that
/// appear are failure bounds, reached only when a tick that should happen
/// never does.</para>
/// </summary>
public sealed class SnapshotSchedulerServiceTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    /// <summary>Only reached when the scheduler misbehaves.</summary>
    private static readonly TimeSpan FailureBound = TimeSpan.FromSeconds(20);

    private static SnapshotSchedulerService Scheduler(
        ISteamSync sync,
        TimeProvider time,
        TimeSpan? interval = null,
        bool runOnStartup = false,
        bool enabled = true)
        => new(
            sync,
            Options.Create(new SnapshotSchedulerOptions
            {
                Interval = interval ?? Interval,
                RunOnStartup = runOnStartup,
                Enabled = enabled,
            }),
            NullLogger<SnapshotSchedulerService>.Instance,
            time);

    /// <summary>
    /// Point 3. Program syncs once, synchronously, before the window opens; a
    /// scheduler tick at T+0 would re-resolve byte-identical files seconds
    /// later. Once the timer is armed the clock is the only thing that can
    /// produce a scan, so a zero call count here rules out a startup double-scan
    /// rather than merely failing to observe one.
    /// </summary>
    [Fact]
    public async Task First_tick_lands_a_full_interval_out_so_it_never_doubles_the_startup_sync()
    {
        var clock = new SchedulerClock();
        var startedAt = clock.GetUtcNow();
        var observedAt = new ConcurrentQueue<DateTimeOffset>();
        var sync = new FakeSteamSync((_, _) =>
        {
            observedAt.Enqueue(clock.GetUtcNow());
            return Task.FromResult(FakeSteamSync.NoChangeReport);
        });

        using var service = Scheduler(sync, clock);
        await service.StartAsync(CancellationToken.None);
        await clock.TimerCreated.WaitAsync(FailureBound);

        Assert.Equal(0, sync.Calls);

        // One second short of the interval: still nothing.
        clock.Advance(Interval - TimeSpan.FromSeconds(1));
        Assert.Equal(0, sync.Calls);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, await sync.NextCompletionAsync());

        // The only scan that ever ran, ran a full interval after startup.
        Assert.Equal(new[] { startedAt + Interval }, observedAt);

        await service.StopAsync(CancellationToken.None).WaitAsync(FailureBound);
    }

    /// <summary>
    /// The mechanism can put a scan at T+0 — it is the default configuration
    /// that declines to, which is what makes the test above meaningful rather
    /// than a scheduler that simply never fires early.
    /// </summary>
    [Fact]
    public async Task Run_on_startup_scans_immediately_when_the_scheduler_owns_the_startup_path()
    {
        var clock = new SchedulerClock();
        var startedAt = clock.GetUtcNow();
        var observedAt = new ConcurrentQueue<DateTimeOffset>();
        var sync = new FakeSteamSync((_, _) =>
        {
            observedAt.Enqueue(clock.GetUtcNow());
            return Task.FromResult(FakeSteamSync.NoChangeReport);
        });

        using var service = Scheduler(sync, clock, runOnStartup: true);
        await service.StartAsync(CancellationToken.None);

        Assert.Equal(1, await sync.NextCompletionAsync());
        Assert.Equal(new[] { startedAt }, observedAt);

        await service.StopAsync(CancellationToken.None).WaitAsync(FailureBound);
    }

    /// <summary>
    /// The point of M2: history keeps accruing for as long as the app is
    /// resident, one scan per interval, on the interval.
    /// </summary>
    [Fact]
    public async Task Ticks_repeat_on_the_interval_for_as_long_as_the_app_runs()
    {
        var clock = new SchedulerClock();
        var startedAt = clock.GetUtcNow();
        var observedAt = new ConcurrentQueue<DateTimeOffset>();
        var sync = new FakeSteamSync((_, _) =>
        {
            observedAt.Enqueue(clock.GetUtcNow());
            return Task.FromResult(FakeSteamSync.NoChangeReport);
        });

        using var service = Scheduler(sync, clock);
        await service.StartAsync(CancellationToken.None);
        await clock.TimerCreated.WaitAsync(FailureBound);

        for (var tick = 1; tick <= 4; tick++)
        {
            clock.Advance(Interval);
            Assert.Equal(tick, await sync.NextCompletionAsync());
        }

        Assert.Equal(
            new[]
            {
                startedAt + Interval,
                startedAt + (2 * Interval),
                startedAt + (3 * Interval),
                startedAt + (4 * Interval),
            },
            observedAt);
        Assert.Equal(1, sync.MaxInFlight);

        await service.StopAsync(CancellationToken.None).WaitAsync(FailureBound);
    }

    /// <summary>
    /// Point 4. A scan held open past several of its own intervals must not
    /// have a second scan started underneath it — SQLite has one writer and the
    /// connection factory throws outright on a nested unit of work. The missed
    /// ticks collapse into exactly one catch-up scan, not a backlog of them.
    /// </summary>
    [Fact]
    public async Task Ticks_never_overlap_when_a_scan_overruns_its_interval()
    {
        var clock = new SchedulerClock();
        var firstScanGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sync = new FakeSteamSync(async (ordinal, ct) =>
        {
            if (ordinal == 1)
            {
                await firstScanGate.Task.WaitAsync(ct);
            }

            return FakeSteamSync.NoChangeReport;
        });

        using var service = Scheduler(sync, clock);
        await service.StartAsync(CancellationToken.None);
        await clock.TimerCreated.WaitAsync(FailureBound);

        clock.Advance(Interval);
        Assert.Equal(1, await sync.NextStartAsync());

        // Three intervals go by while scan 1 is still running. A scheduler that
        // launched ticks instead of awaiting them would have three more scans
        // sitting on this same gate by now.
        clock.Advance(Interval);
        clock.Advance(Interval);
        clock.Advance(Interval);

        Assert.Equal(1, sync.Calls);
        Assert.False(sync.Overlapped.IsCompleted);

        firstScanGate.SetResult();
        Assert.Equal(1, await sync.NextCompletionAsync());

        // One coalesced catch-up scan, and then quiet: three missed ticks do
        // not become three scans.
        Assert.Equal(2, await sync.NextCompletionAsync());
        Assert.Equal(2, sync.Calls);
        Assert.Equal(1, sync.MaxInFlight);
        Assert.False(sync.Overlapped.IsCompleted);

        await service.StopAsync(CancellationToken.None).WaitAsync(FailureBound);
    }

    /// <summary>
    /// Point 4's other half. Program's <c>finally</c> disposes the host — and
    /// the SQLite connection factory with it — the moment <c>StopAsync</c>
    /// returns, so a scan in flight must unwind before that, and no tick may
    /// start afterwards however far the clock moves.
    /// </summary>
    [Fact]
    public async Task Stopping_cancels_an_in_flight_scan_and_no_tick_runs_afterwards()
    {
        var clock = new SchedulerClock();
        var sync = new FakeSteamSync(async (_, ct) =>
        {
            // Cancelled by the stopping token, never by elapsed time.
            await Task.Delay(Timeout.Infinite, ct);
            return FakeSteamSync.NoChangeReport;
        });

        using var service = Scheduler(sync, clock);
        await service.StartAsync(CancellationToken.None);
        await clock.TimerCreated.WaitAsync(FailureBound);

        clock.Advance(Interval);
        Assert.Equal(1, await sync.NextStartAsync());

        await service.StopAsync(CancellationToken.None).WaitAsync(FailureBound);

        // The scan unwound rather than being abandoned still holding a write.
        Assert.Equal(1, await sync.NextCompletionAsync());

        // Cancellation ends the loop; it is not an error the host has to absorb.
        Assert.NotNull(service.ExecuteTask);
        Assert.True(service.ExecuteTask.IsCompletedSuccessfully);

        clock.Advance(5 * Interval);
        Assert.Equal(1, sync.Calls);
    }

    /// <summary>
    /// Steam rewriting <c>localconfig.vdf</c> mid-read is normal, and the
    /// resolver's single transaction means a failed pass left nothing behind.
    /// One lost data point is the correct cost; an unhandled throw out of a
    /// BackgroundService would instead take the whole host down.
    /// </summary>
    [Fact]
    public async Task A_failing_tick_costs_one_data_point_not_the_schedule()
    {
        var clock = new SchedulerClock();
        var sync = new FakeSteamSync((ordinal, _) => ordinal == 1
            ? Task.FromException<SteamSyncReport>(new IOException("localconfig.vdf is locked"))
            : Task.FromResult(FakeSteamSync.NoChangeReport));

        using var service = Scheduler(sync, clock);
        await service.StartAsync(CancellationToken.None);
        await clock.TimerCreated.WaitAsync(FailureBound);

        clock.Advance(Interval);
        Assert.Equal(1, await sync.NextCompletionAsync());

        clock.Advance(Interval);
        Assert.Equal(2, await sync.NextCompletionAsync());

        Assert.NotNull(service.ExecuteTask);
        Assert.False(service.ExecuteTask.IsCompleted);

        await service.StopAsync(CancellationToken.None).WaitAsync(FailureBound);
    }

    /// <summary>Mirrors <c>--no-sync</c>: rows must not appear under fixed-database UI work.</summary>
    [Fact]
    public async Task Disabled_scheduler_never_scans()
    {
        var clock = new SchedulerClock();
        var sync = new FakeSteamSync();

        using var service = Scheduler(sync, clock, enabled: false);
        await service.StartAsync(CancellationToken.None);

        Assert.NotNull(service.ExecuteTask);
        await service.ExecuteTask.WaitAsync(FailureBound);
        Assert.True(service.ExecuteTask.IsCompletedSuccessfully);

        clock.Advance(100 * Interval);
        Assert.Equal(0, sync.Calls);

        await service.StopAsync(CancellationToken.None).WaitAsync(FailureBound);
    }

    /// <summary>
    /// A non-positive interval throws out of PeriodicTimer's constructor, and an
    /// unhandled throw in a BackgroundService stops the host by default —
    /// misconfiguration would cost the user the app, not just the history.
    /// </summary>
    [Fact]
    public async Task A_non_positive_interval_stands_the_scheduler_down_instead_of_taking_the_host_with_it()
    {
        var clock = new SchedulerClock();
        var sync = new FakeSteamSync();

        using var service = Scheduler(sync, clock, interval: TimeSpan.Zero);
        await service.StartAsync(CancellationToken.None);

        Assert.NotNull(service.ExecuteTask);
        await service.ExecuteTask.WaitAsync(FailureBound);
        Assert.True(service.ExecuteTask.IsCompletedSuccessfully);

        clock.Advance(100 * Interval);
        Assert.Equal(0, sync.Calls);

        await service.StopAsync(CancellationToken.None).WaitAsync(FailureBound);
    }
}
