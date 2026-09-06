using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Winnow.App.Services;

/// <summary>Publishes stable Epic manifest changes through local sync and the normal library reload.</summary>
public sealed class EpicInstallRefreshService(
    Func<string?> readFingerprint,
    Func<CancellationToken, Task> sync,
    Func<CancellationToken, Task> refresh,
    ILogger<EpicInstallRefreshService> logger,
    TimeProvider? timeProvider = null,
    bool enabled = true) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    private string? _pending;
    private string? _applied;

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
            logger.LogWarning(ex, "Epic installation refresh failed; the next stable manifest read will retry.");
        }
    }
}
