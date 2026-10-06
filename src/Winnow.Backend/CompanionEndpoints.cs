using Winnow.Api.Contracts.Companion;

namespace Winnow.Backend;

/// <summary>Loopback routes the desktop and fullscreen settings use to control phone sync.
/// They sit behind the same loopback and token checks as every other local route.</summary>
internal static class CompanionEndpoints
{
    public static void MapCompanionApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/companion").WithTags("Phone sync");
        api.MapGet("", (CompanionLanHost host, CancellationToken ct) => host.StatusAsync(ct));
        api.MapPut("/enabled", (SetCompanionEnabled request, CompanionLanHost host, CancellationToken ct) => host.SetEnabledAsync(request.Enabled, ct));
        api.MapPost("/pairing", (CompanionLanHost host, CancellationToken ct) => host.OpenPairingAsync(ct));
        api.MapDelete("/pairing", (CompanionLanHost host, CancellationToken ct) => host.ClosePairingAsync(ct));
        api.MapDelete("/devices/{id}", async (string id, CompanionLanHost host, CancellationToken ct) =>
            await host.RemoveDeviceAsync(id, ct) ? Results.NoContent() : Results.NotFound());
    }
}
