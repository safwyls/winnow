using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;

namespace Winnow.App.Services;

/// <summary>The application operation used by startup, scheduled backfill and explicit refresh requests.</summary>
public sealed class OwnershipRefreshCoordinator(
    IRemoteOwnershipSync remote,
    LibraryRefreshPipeline pipeline,
    ILogger<OwnershipRefreshCoordinator> logger) : IRemoteOwnershipSync
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task<LibrarySyncReport> SyncAsync(CancellationToken ct = default) => RunAsync(null, ct);
    public Task<LibrarySyncReport> SyncAsync(LocalLibraryScan scan, CancellationToken ct = default) => RunAsync(scan, ct);

    private async Task<LibrarySyncReport> RunAsync(LocalLibraryScan? scan, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            LibrarySyncReport? report = null;
            Exception? failure = null;
            try
            {
                report = scan is { } local ? await remote.SyncAsync(local, ct).ConfigureAwait(false)
                    : await remote.SyncAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                failure = ex;
                logger.LogWarning("Ownership refresh failed; refreshing any facts already committed ({FaultType}).", ex.GetType().Name);
            }

            // Ownerships are browsable while metadata fills in. A later phase
            // failing must not hide an earlier committed acquisition.
            await pipeline.PublishAsync(ct).ConfigureAwait(false);
            await pipeline.RunAsync(ct).ConfigureAwait(false);
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            return report!;
        }
        finally { _gate.Release(); }
    }
}

/// <summary>Successful account actions request background work without holding their UI command open.</summary>
public sealed class OwnershipRefreshRequests
{
    public event Action? Requested;
    public void Request() => Requested?.Invoke();
}
