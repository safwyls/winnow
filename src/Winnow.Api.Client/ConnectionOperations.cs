using Winnow.Api.Contracts.Connections;
using Winnow.App.Services;

namespace Winnow.Api.Client;

public sealed class ConnectionManualMetadataSyncService(WinnowApiClient api) : IManualMetadataSyncService
{
    public async Task<MetadataSyncResult> SyncAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var operation = await api.SendAsync<StartMetadataSync, BackendOperation>(HttpMethod.Post, "operations/metadata-sync", new(Guid.NewGuid().ToString("N")), ct: ct);
        operation = await ConnectionOperationPolling.WaitAsync(api, operation, message => progress?.Report(message), ct);
        return operation.MetadataResult ?? MetadataSyncResult.RefreshFailed;
    }
}

public sealed class ConnectionOfficialPluginInstaller(WinnowApiClient api) : IOfficialPluginInstaller
{
    public async Task<PluginInstallResult> InstallAsync(PluginInstallRequest request, IProgress<PluginInstallProgress>? progress = null, CancellationToken ct = default)
    {
        var operation = await api.SendAsync<StartPluginInstall, BackendOperation>(HttpMethod.Post, "operations/plugin-install", new(Guid.NewGuid().ToString("N"), request), ct: ct);
        operation = await ConnectionOperationPolling.WaitAsync(api, operation, message => progress?.Report(new(message)), ct);
        return operation.PluginResult ?? new PluginInstallResult(PluginInstallOutcome.Failed, request.PluginId, operation.Message);
    }
    // A frontend disconnect never cancels another frontend's operation.
    public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
}

internal static class ConnectionOperationPolling
{
    public static async Task<BackendOperation> WaitAsync(WinnowApiClient api, BackendOperation operation, Action<string> report, CancellationToken ct)
    {
        try
        {
            while (operation.State == "running")
            {
                report(operation.Message);
                await Task.Delay(500, ct);
                operation = await api.GetAsync<BackendOperation>("operations/" + operation.Id, ct);
            }
            report(operation.Message);
            return operation;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await api.SendAsync<object?>(HttpMethod.Post, "operations/" + operation.Id + "/cancel", null, cleanup.Token); }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException) { }
            throw;
        }
    }
}
