using Winnow.Api.Contracts.Connections;
using Winnow.Application.Connections;

namespace Winnow.Backend;

public static class StoreConnectionEndpoints
{
    public static void MapStoreConnectionApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/connections/stores").WithTags("Store connections");
        api.MapGet("", (StoreConnectionApplication service, CancellationToken ct) => service.GetAsync(ct));
        api.MapPut("/steam/key", (SaveSteamApiKey request, StoreConnectionApplication service, CancellationToken ct) => service.SaveSteamKeyAsync(request.Key, ct));
        api.MapPost("/steam/sign-in", (SteamAuthBegin request, StoreConnectionApplication service) => service.BeginSteam(request));
        api.MapPost("/steam/sign-in/complete", (SteamAuthComplete request, StoreConnectionApplication service, CancellationToken ct) => service.CompleteSteamAsync(request, ct));
        api.MapPost("/steam/sign-out", async (StoreConnectionApplication service, CancellationToken ct) =>
        { await service.SignOutSteamAsync(ct); return Results.NoContent(); });
        api.MapPost("/epic/sign-in", (PluginSignInRequest request, StoreConnectionApplication service, CancellationToken ct) => service.BeginEpicAsync(request.ClientId, ct));
        api.MapPost("/epic/sign-in/complete", (EpicAuthComplete request, StoreConnectionApplication service, CancellationToken ct) => service.CompleteEpicAsync(request, ct));
        api.MapPost("/epic/sign-out", async (StoreConnectionApplication service, CancellationToken ct) =>
        { await service.SignOutEpicAsync(ct); return Results.NoContent(); });
        api.MapPost("/sign-in/cancel", (StoreAuthCancel request, StoreConnectionApplication service) =>
        { service.Cancel(request); return Results.NoContent(); });
    }
}
