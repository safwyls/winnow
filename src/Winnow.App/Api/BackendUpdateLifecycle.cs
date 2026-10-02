using System.Diagnostics;
using Winnow.Api.Client;

namespace Winnow.App.Api;

/// <summary>Releases backend binaries before installer handoff and restores service after failed preparation.</summary>
internal sealed class BackendUpdateLifecycle(string dataDirectory, IReadOnlyList<string>? arguments = null)
    : Services.ILibraryServiceLifecycle, IDisposable
{
    private Process? _stopping;
    private readonly SemaphoreSlim _operation = new(1, 1);
    private bool _updateOwnsOperation;

    public void Dispose() => _stopping?.Dispose();

    public async Task StopAsync(CancellationToken ct)
    {
        await _operation.WaitAsync(ct);
        _updateOwnsOperation = true;
        await StopCoreAsync(ct);
    }

    private async Task StopCoreAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var discovery = await BackendConnection.ReadAsync(dataDirectory, timeout.Token);
        using var connection = new WinnowApiClient(discovery);
        var health = await connection.GetHealthAsync(timeout.Token);
        if (health.Epoch != discovery.Epoch)
            throw new IOException("The library service restarted. Try restarting for the update again.");
        _stopping?.Dispose();
        _stopping = Process.GetProcessById(discovery.ProcessId);
        try
        {
            await connection.SendAsync(HttpMethod.Post, "lifecycle/shutdown", new { }, timeout.Token);
        }
        catch (BackendApiException)
        {
            // A received refusal did not schedule shutdown. Recovery can attach to the still-running
            // owner; only an uncertain transport failure warrants waiting for possible process exit.
            _stopping.Dispose();
            _stopping = null;
            throw;
        }
        await _stopping.WaitForExitAsync(timeout.Token);
    }

    public async Task RecoverAsync(CancellationToken ct)
    {
        if (!_updateOwnsOperation) return;
        try { await RecoverCoreAsync(ct); }
        finally { _updateOwnsOperation = false; _operation.Release(); }
    }

    private async Task RecoverCoreAsync(CancellationToken ct)
    {
        // A cancelled shutdown request may already have reached the backend. Wait for that process
        // before attaching; otherwise an apparently healthy but stopping process could win the probe.
        if (_stopping is not null)
        {
            await _stopping.WaitForExitAsync(ct);
            _stopping.Dispose();
            _stopping = null;
        }
        // Seeding is a one-time startup request. Only preserve the non-mutating worker opt-out.
        var restartArguments = arguments?.Any(argument => argument is "--no-sync" or "--seed-sample") == true ? new[] { "--no-sync" } : [];
        using var attached = await BackendProcessLauncher.AttachOrStartAsync(dataDirectory, restartArguments, ct);
    }

    public async Task RestartAsync(CancellationToken ct = default)
    {
        await _operation.WaitAsync(ct);
        try
        {
            try { await StopCoreAsync(ct); }
            finally
            {
                // Once shutdown has started, cancellation must not leave connected windows offline.
                using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                await RecoverCoreAsync(recovery.Token);
            }
        }
        finally { _operation.Release(); }
    }
}
