using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Winnow.App.Services;

/// <summary>
/// The M2 snapshot scheduler (§5 "Core / Snapshot Scheduler", §8 M2). Re-runs
/// the local Steam scan-and-resolve on an interval for as long as the app is
/// running, so <c>playtime_snapshots</c> gains points while the user plays
/// rather than only at launch.
///
/// <para><b>Why this exists.</b> §1's premise is longitudinal history that the
/// storefronts discard. Until M2 the only writer of that history was Program's
/// one-shot startup sync, so a tray-resident app left open for a week recorded
/// exactly one point for that week — the delta from a Tuesday evening and a
/// Thursday evening collapsed into a single jump with no shape. The resolver
/// was already correct; nothing was calling it often enough.</para>
///
/// <para><b>Cheap by construction.</b> The tick is
/// <see cref="SteamSyncService"/> — the same filesystem-only path Program runs
/// at startup, no network anywhere in it. The resolver is idempotent by change
/// detection, so a tick over unchanged files opens one read transaction,
/// compares, and commits nothing. See <see cref="SnapshotSchedulerOptions"/>
/// for why the interval is not shorter.</para>
///
/// <para><b>No overlap, by structure rather than by lock.</b> This is one
/// sequential loop: the tick is awaited to completion before the next
/// <c>WaitForNextTickAsync</c> is even issued, so two scans can never be in
/// flight together. <see cref="PeriodicTimer"/> coalesces ticks that elapse
/// during a long scan into at most one pending tick, so a scan that overruns
/// its interval is followed by a single immediate catch-up tick, never a
/// backlog. This matters beyond CPU: SQLite has one writer, and
/// <see cref="Winnow.Data.SqliteConnectionFactory.Begin"/> throws outright if a
/// unit of work is already open on the same async flow.</para>
///
/// <para><b>Shutdown.</b> <c>stoppingToken</c> is honoured at every await and
/// checked before a tick starts, and cancellation ends the loop instead of
/// retrying — because Program's <c>finally</c> disposes the host, and the
/// SQLite connection factory with it, right after <c>StopAsync</c> returns.
/// <see cref="BackgroundService.StopAsync"/> awaits this loop, so a tick that
/// is mid-transaction when the window closes rolls back and unwinds before
/// anything it writes through can be disposed. A tick must therefore never
/// swallow cancellation and carry on.</para>
///
/// <para>§5.1: this composes ingest and resolve exactly as Program does and
/// touches no repository itself; the UI reads the database afterwards.</para>
/// </summary>
public sealed class SnapshotSchedulerService : BackgroundService
{
    private readonly ISteamSync _sync;
    private readonly SnapshotSchedulerOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SnapshotSchedulerService> _logger;

    /// <param name="timeProvider">
    /// Injected rather than read from <see cref="TimeProvider.System"/> so the
    /// tick schedule is drivable from a test without sleeping. Optional: the
    /// container falls back to this default when nothing is registered.
    /// </param>
    public SnapshotSchedulerService(
        ISteamSync sync,
        IOptions<SnapshotSchedulerOptions> options,
        ILogger<SnapshotSchedulerService> logger,
        TimeProvider? timeProvider = null)
    {
        _sync = sync;
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Snapshot scheduler disabled; playtime history will only advance at startup.");
            return;
        }

        var interval = _options.Interval;
        if (interval <= TimeSpan.Zero)
        {
            // A non-positive period would throw out of PeriodicTimer's
            // constructor and, unhandled in a BackgroundService, take the host
            // down with it. Misconfiguration should cost history, not the app.
            _logger.LogWarning(
                "Snapshot scheduler interval {Interval} is not positive; scheduler will not run.", interval);
            return;
        }

        _logger.LogInformation(
            "Snapshot scheduler started; scanning every {Interval}. First scan at {FirstScan:u}.",
            interval,
            _timeProvider.GetUtcNow().Add(_options.RunOnStartup ? TimeSpan.Zero : interval));

        using var timer = new PeriodicTimer(interval, _timeProvider);

        try
        {
            if (_options.RunOnStartup)
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }

            // WaitForNextTickAsync's first tick lands one full interval out.
            // That is deliberate and is the whole of point 3: Program already
            // scanned synchronously before the window opened, and a tick at T+0
            // would re-read byte-identical files seconds later.
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Window closed / host stopping. Ending the loop here is what keeps
            // the next tick from reaching a disposed connection factory.
        }

        _logger.LogDebug("Snapshot scheduler stopped.");
    }

    /// <summary>
    /// One scan-and-resolve pass. Swallows ordinary failures so a transient
    /// file lock (Steam rewriting localconfig.vdf under us is normal) costs one
    /// data point rather than the whole schedule; rethrows cancellation so the
    /// loop actually stops.
    /// </summary>
    private async Task TickAsync(CancellationToken ct)
    {
        // Re-checked here and not only at the loop head: cancellation can land
        // between a tick completing and the next one starting.
        if (ct.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var report = await _sync.SyncAsync(ct).ConfigureAwait(false);
            var result = report.Result;

            var changed = result is not null
                && (result.SnapshotsWritten > 0
                    || result.PlayRecordsWritten > 0
                    || result.CreatedReleases > 0
                    || result.NamesPromoted > 0);

            // A tray-resident app runs for days: at 15 minutes that is ~96 ticks
            // a day, and the overwhelming majority change nothing. Logging those
            // at Information buries the ones that matter, so the quiet case is
            // Debug and only a tick that actually moved the history speaks up.
            if (changed)
            {
                _logger.LogInformation(
                    "Snapshot tick: {Snapshots} snapshot(s), {PlayRecords} play record(s), "
                    + "{Created} new release(s), {Promoted} name(s) promoted from {Candidates} candidates "
                    + "in {Elapsed:n1}s.",
                    result!.SnapshotsWritten, result.PlayRecordsWritten, result.CreatedReleases,
                    result.NamesPromoted, report.Candidates, report.Elapsed.TotalSeconds);
            }
            else
            {
                _logger.LogDebug(
                    "Snapshot tick: no change across {Candidates} candidates in {Elapsed:n1}s.",
                    report.Candidates, report.Elapsed.TotalSeconds);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The resolver runs the whole pass in one transaction, so a failed
            // tick left nothing half-written; the next tick re-reads from disk.
            _logger.LogWarning(ex, "Snapshot tick failed; the series resumes at the next tick.");
        }
    }
}
