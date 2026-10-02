using Winnow.Api.Contracts.Connections;
using Winnow.App.Services;
using Winnow.Application.Connections;

namespace Winnow.Backend;

internal sealed class BackendProgressReporter(BackendEventHub events) : IProgress<EnrichmentProgress>
{
    private BackendProgress _snapshot = new(0, 0);
    public BackendProgress Snapshot => Volatile.Read(ref _snapshot);
    public void Report(EnrichmentProgress value)
    {
        Volatile.Write(ref _snapshot, new BackendProgress(value.Total, value.Remaining));
        events.Publish("progress.changed", "enrichment");
    }
}

internal static class OperationEndpoints
{
    public static void MapOperationApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").WithTags("Operations and diagnostics");
        api.MapPost("/identity/review/refresh", async (IMergeSuggestionRefresh service,
            Winnow.Application.IApplicationChangePublisher changes, CancellationToken ct) =>
        {
            var result = await service.RefreshAsync(ct);
            changes.Publish("library.changed", "identity");
            return result;
        });
        api.MapPost("/games/{workId:long}/refetch", async (long workId, IGameRefetch service,
            Winnow.Application.IApplicationChangePublisher changes, CancellationToken ct) =>
        {
            var result = await service.RefetchAsync(workId, ct);
            if (result.Succeeded) changes.Publish("library.changed", $"games/{workId}");
            return result;
        });
        api.MapGet("/operations", (ConnectionOperations operations) => operations.GetAll());
        api.MapGet("/operations/{id}", (string id, ConnectionOperations operations) => operations.Get(id));
        api.MapPost("/operations/metadata-sync", (StartMetadataSync request, ConnectionOperations operations) => operations.StartMetadata(request));
        api.MapPost("/operations/plugin-install", (StartPluginInstall request, ConnectionOperations operations) => operations.StartPlugin(request));
        api.MapPost("/operations/{id}/cancel", (string id, ConnectionOperations operations) =>
        { operations.Cancel(id); return Results.NoContent(); });
        api.MapGet("/progress", (BackendProgressReporter progress) => progress.Snapshot);
        var started = DateTimeOffset.UtcNow;
        api.MapGet("/diagnostics", (Winnow.Monitor.SessionWatcherHealth health) =>
            new BackendDiagnostics("1", Environment.ProcessId, Environment.Version.ToString(), started, health.Failures));
    }
}
