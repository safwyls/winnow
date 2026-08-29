using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Winnow.App.Services;

/// <summary>
/// Re-runs the local scan-and-resolve on an interval so
/// <c>playtime_snapshots</c> gains points while the user plays rather than
/// only at launch. Sequential loop with no overlap.
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
