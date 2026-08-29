using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Winnow.App.Services;

/// <summary>
/// Knobs for <see cref="RemoteOwnershipSchedulerService"/>. Defaults are the
/// shipped values; <c>--no-sync</c> and <c>--seed-sample</c> flip
/// <see cref="Enabled"/> off.
/// </summary>
public sealed class RemoteOwnershipSchedulerOptions
{
    /// <summary>
    /// How often the remote entitlement backfill re-runs. Six hours because
    /// entitlement lists change only when the user buys a game, both endpoints
    /// are rate-limited, and the startup pass already covers what changed since
    /// the last run. Non-positive disables the scheduler.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// Whether the scheduler runs at all. Off mirrors <c>--no-sync</c>: UI work
    /// against a fixed database must not have rows appearing underneath it.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether to run a backfill immediately on start. False by default because
    /// the background startup pipeline already includes a remote sync, and an
    /// immediate tick would duplicate that work.
    /// </summary>
    public bool RunOnStartup { get; set; }
}

/// <summary>
/// Runs <see cref="IRemoteOwnershipSync"/> on the interval configured in
/// <see cref="RemoteOwnershipSchedulerOptions"/>. Sequential loop with no
/// overlap. Disabled alongside <see cref="SnapshotSchedulerService"/> by
/// <c>--no-sync</c> and <c>--seed-sample</c>.
/// </summary>
public sealed class RemoteOwnershipSchedulerService : BackgroundService
{
    private readonly IRemoteOwnershipSync _sync;
    private readonly RemoteOwnershipSchedulerOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RemoteOwnershipSchedulerService> _logger;

    public RemoteOwnershipSchedulerService(
        IRemoteOwnershipSync sync,
        IOptions<RemoteOwnershipSchedulerOptions> options,
        ILogger<RemoteOwnershipSchedulerService> logger,
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
            _logger.LogInformation("Remote ownership scheduler disabled.");
            return;
        }

        var interval = _options.Interval;
        if (interval <= TimeSpan.Zero)
        {
            // A non-positive period throws out of PeriodicTimer's constructor
            // and, unhandled in a BackgroundService, takes the host with it.
            _logger.LogWarning(
                "Remote ownership interval {Interval} is not positive; scheduler will not run.", interval);
            return;
        }

        _logger.LogInformation(
            "Remote ownership scheduler started; backfilling every {Interval}. First run at {First:u}.",
            interval,
            _timeProvider.GetUtcNow().Add(_options.RunOnStartup ? TimeSpan.Zero : interval));

        using var timer = new PeriodicTimer(interval, _timeProvider);

        try
        {
            if (_options.RunOnStartup)
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }

            // The first tick is one full interval out: the background startup
            // pipeline already ran a backfill as soon as the window was up.
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

        _logger.LogDebug("Remote ownership scheduler stopped.");
    }

    private async Task TickAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var report = await _sync.SyncAsync(ct).ConfigureAwait(false);
            _logger.LogDebug(
                "Remote ownership tick: {Candidates} candidates in {Elapsed:n1}s.",
                report.Candidates, report.Elapsed.TotalSeconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Offline, rate-limited or a token that would not refresh. The
            // resolver runs the whole pass in one transaction, so a failed tick
            // left nothing half-written.
            _logger.LogWarning(ex, "Remote ownership backfill failed; it resumes at the next tick.");
        }
    }
}
