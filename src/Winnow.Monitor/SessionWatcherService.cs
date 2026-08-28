using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Winnow.Monitor;

/// <summary>
/// Drives <see cref="SessionWatcher.TickAsync"/> on the §5.2 poll interval for
/// as long as the app is running, and closes the watcher's books on shutdown.
///
/// <para><b>Nothing here is on a user-facing path (§5.1, pitfall 3).</b> The
/// first tick lands one interval after start rather than at T+0 — a deliberate
/// copy of <c>SnapshotSchedulerService</c>'s reasoning: the first tick builds the
/// executable index, which walks install directories, and the window opening is
/// not the moment to do that. Five seconds later nobody is watching. A game
/// launched in that first five seconds is still recorded correctly, because the
/// recorded start time comes from the OS, not from us.</para>
///
/// <para><b>No overlap, by structure rather than by lock</b> — the same shape as
/// the snapshot scheduler, for the same reasons. One sequential loop, each tick
/// awaited to completion before the next is requested, and
/// <see cref="PeriodicTimer"/> coalescing anything that elapses during a long
/// tick into at most one catch-up. SQLite has a single writer and
/// <c>SqliteConnectionFactory.Begin</c> throws if a unit of work is already open
/// on the same flow, so "two ticks in flight" is not a performance concern here,
/// it is an exception.</para>
///
/// <para><b>Shutdown.</b> <see cref="BackgroundService.StopAsync"/> awaits this
/// loop, and the loop's last act is <see cref="SessionWatcher.FlushAsync"/> —
/// which writes, so it must happen before the host disposes the connection
/// factory. It runs on a token deliberately separate from
/// <c>stoppingToken</c>: that token is already cancelled by the time the flush
/// is reached, and passing it would cancel the very write the flush exists to
/// perform.</para>
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
    /// One poll. Swallows ordinary failures so a transient problem — an install
    /// directory that vanished mid-scan, a failed index rebuild — costs one pass
    /// rather than the rest of the run; rethrows cancellation so the loop
    /// actually stops.
    ///
    /// <para><b>This catch is not what protects finished sessions.</b> An
    /// earlier version of this comment claimed a failed tick "costs one data
    /// point", which was not true of the code: a session finalised inside the
    /// tick had already left the tracking state, so an insert that threw here
    /// lost it for good. The queue in <c>SessionWatcher</c> is what makes the
    /// claim true — a session stays queued until the database accepts it, and
    /// this handler only decides whether the <i>loop</i> continues. Reinstating
    /// a comment that asserts a safety property the code does not have is worse
    /// than having no comment at all.</para>
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
