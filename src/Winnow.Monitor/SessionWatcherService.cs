using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Winnow.Monitor;

/// <summary>
/// Drives <see cref="SessionWatcher.TickAsync"/> on the poll interval for as
/// long as the app is running, and flushes in-flight sessions on shutdown.
/// Sequential loop with no overlap.
/// </summary>
public sealed class SessionWatcherService : BackgroundService
{
    private readonly SessionWatcher _watcher;
    private readonly SessionWatcherOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SessionWatcherService> _logger;

    public SessionWatcherService(
        SessionWatcher watcher,
        IOptions<SessionWatcherOptions> options,
        ILogger<SessionWatcherService> logger,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _watcher = watcher;
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "Session watcher disabled; play sessions will not be detected this run.");
            return;
        }

        var interval = _options.PollInterval;
        if (interval <= TimeSpan.Zero)
        {
            // A non-positive period throws out of PeriodicTimer's constructor
            // and, unhandled in a BackgroundService, takes the host down.
            // Misconfiguration should cost sessions, not the app.
            _logger.LogWarning(
                "Session watcher poll interval {Interval} is not positive; the watcher will not run.",
                interval);
            return;
        }

        _logger.LogInformation(
            "Session watcher started; polling every {Interval}, debounce {Debounce:n0}s, "
            + "relaunch grace {Grace:n0}s.",
            interval,
            _options.MinimumSessionDuration.TotalSeconds,
            _options.RelaunchGrace.TotalSeconds);

        using var timer = new PeriodicTimer(interval, _timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Host stopping. Fall through to the flush.
        }

        await FlushAsync().ConfigureAwait(false);
        _logger.LogDebug("Session watcher stopped.");
    }

    /// <summary>
    /// One poll. Swallows ordinary failures so a transient problem costs one
    /// pass rather than the rest of the run; rethrows cancellation.
    /// </summary>
    private async Task TickAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var tick = await _watcher.TickAsync(ct).ConfigureAwait(false);

            // At a five-second poll this runs seventeen thousand times a day, so
            // the quiet case says nothing at all and only a tick that changed
            // something speaks up. TickAsync already logs each recorded session
            // at Information in its own right.
            if (tick.Started > 0 || tick.Debounced > 0 || tick.Recorded > 0)
            {
                _logger.LogDebug(
                    "Session tick: {Started} process(es) attached, {Recorded} session(s) recorded, "
                    + "{Debounced} debounced, {Running} still running, {Queued} queued.",
                    tick.Started, tick.Recorded, tick.Debounced, tick.Running, tick.Queued);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Session watcher tick failed; detection resumes at the next poll.");
        }
    }

    private async Task FlushAsync()
    {
        try
        {
            // Bounded rather than unbounded: shutdown must not hang on a
            // database that will not answer, and the host is about to dispose
            // the connection factory regardless.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _watcher.FlushAsync(cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write in-flight sessions during shutdown.");
        }
    }
}
