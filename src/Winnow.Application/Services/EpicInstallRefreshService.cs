using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Winnow.App.Services;

/// <summary>Publishes stable launcher manifest changes through local sync and the normal library reload.</summary>
/// <param name="baseline">
/// Shared record of what the local scans have already read. Optional; without
/// it the first stable read at launch scans the files the startup pipeline has
/// just scanned, which is the duplicate this parameter exists to remove.
/// </param>
/// <param name="source">Which half of that record this watcher owns.</param>
public class StableInstallRefreshService(
    Func<string?> readFingerprint,
    Func<CancellationToken, Task> sync,
    Func<CancellationToken, Task> refresh,
    ILogger logger,
    TimeProvider? timeProvider = null,
    bool enabled = true,
    LibraryScanBaseline? baseline = null,
    LibraryScanSource source = LibraryScanSource.Steam) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    private string? _pending;
    private string? _applied;

    /// <summary>
    /// Whether the launch-time question has been settled. Kept apart from
    /// <see cref="_applied"/>, which a failed publish clears: a genuine change
    /// that failed to resolve must be retried, never adopted.
    /// </summary>
    private bool _launchSettled;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!enabled) return;
        using var timer = new PeriodicTimer(Interval, timeProvider ?? TimeProvider.System);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                await PollAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    /// <summary>Requires two consecutive readable snapshots; failures are retried on the next poll.</summary>
    public async Task PollAsync(CancellationToken ct = default)
    {
        if (!enabled) return;
        ct.ThrowIfCancellationRequested();
        try
        {
            var current = readFingerprint();
            if (current is null || current != _pending)
            {
                _pending = current;
                return;
            }
            if (current == _applied) return;

            // A first stable read at launch is not a change. The startup
            // pipeline scans these same files as the window opens, so wait for
            // it and then adopt its answer; a manifest that moved in between
            // does not match what that pass covered and publishes as usual.
            if (!_launchSettled && baseline is not null)
            {
                if (baseline.PassExpected) return;
                _launchSettled = true;
                if (baseline.Covered(source, current))
                {
                    _applied = current;
                    return;
                }
            }

            _launchSettled = true;

            // The scan may write before a later read or reload fails. Even a return
            // to the previous fingerprint must then reconcile those writes.
            _applied = null;
            await sync(ct).ConfigureAwait(false);
            // A rewrite during the scan needs another stable pass before we publish.
            if (readFingerprint() != current)
            {
                _pending = null;
                return;
            }
            ct.ThrowIfCancellationRequested();
            await refresh(ct).ConfigureAwait(false);
            _applied = current;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _pending = null;
            logger.LogWarning(ex, "Installation refresh failed; the next stable manifest read will retry.");
        }
    }
}

public sealed class EpicInstallRefreshService(
    Func<string?> readFingerprint,
    Func<CancellationToken, Task> sync,
    Func<CancellationToken, Task> refresh,
    ILogger<EpicInstallRefreshService> logger,
    TimeProvider? timeProvider = null,
    bool enabled = true,
    LibraryScanBaseline? baseline = null)
    : StableInstallRefreshService(
        readFingerprint, sync, refresh, logger, timeProvider, enabled, baseline, LibraryScanSource.Epic);

public sealed class SteamInstallRefreshService(
    Func<string?> readFingerprint,
    Func<CancellationToken, Task> sync,
    Func<CancellationToken, Task> refresh,
    ILogger<SteamInstallRefreshService> logger,
    TimeProvider? timeProvider = null,
    bool enabled = true,
    LibraryScanBaseline? baseline = null)
    : StableInstallRefreshService(
        readFingerprint, sync, refresh, logger, timeProvider, enabled, baseline, LibraryScanSource.Steam);
