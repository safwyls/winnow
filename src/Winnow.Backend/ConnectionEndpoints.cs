using Winnow.Api.Contracts.Connections;
using Winnow.App.Services;
using Winnow.Application;
using Winnow.Application.Connections;

namespace Winnow.Backend;

public static class ConnectionEndpoints
{
    public static void MapConnectionApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/connections").WithTags("Provider connections");
        api.MapGet("/account-visibility", (IAccountVisibility service, CancellationToken ct) => service.GetAsync(ct));
        api.MapPut("/account-visibility", async (SetAccountVisibility request, IAccountVisibility service,
            IApplicationChangePublisher changes, CancellationToken ct) =>
        {
            await service.SetOwnAccountOnlyAsync(request.OwnAccountOnly, ct);
            changes.Publish("library.changed", "account-visibility");
            return Results.NoContent();
        });
        api.MapGet("/igdb", (IIgdbSettingsService service, CancellationToken ct) => service.LoadAsync(ct));
        api.MapPut("/igdb", async (SaveIgdbCredentials request, IIgdbSettingsService service, IApplicationChangePublisher changes) =>
        {
            if (request.ClientId is null || request.ClientSecret is null || request.ClientId.Length > 512 || request.ClientSecret.Length > 4096
                || request.ClientId.Any(char.IsControl) || request.ClientSecret.Any(char.IsControl))
                return Results.BadRequest();
            var result = await service.SaveAsync(request.ClientId, request.ClientSecret);
            if (result == IgdbSettingsSaveResult.Saved) changes.Publish("connections.changed", "igdb");
            return Results.Ok(result);
        });
        api.MapDelete("/igdb", async (IIgdbSettingsService service, IApplicationChangePublisher changes) =>
        {
            var remaining = await service.RemoveAsync();
            changes.Publish("connections.changed", "igdb");
            return Results.Ok(remaining);
        });
        api.MapGet("/plugins", (IPluginSettingsBackend service, CancellationToken ct) => service.LoadAsync(ct));
        api.MapGet("/plugins/directory", (IPluginSettingsBackend service) => new PluginDirectoryResponse(service.UserPluginDirectory));
        api.MapPut("/plugins/{pluginId}/settings", async (string pluginId, PluginSettingsValues request,
            IPluginSettingsBackend service, IApplicationChangePublisher changes, CancellationToken ct) =>
        {
            if (request.Values is null || request.Values.Count > 128 || request.Values.Any(v => v.Value is null)) return Results.BadRequest();
            await service.SaveAsync(pluginId, request.Values, ct);
            changes.Publish("connections.changed", pluginId);
            return Results.NoContent();
        });
        api.MapDelete("/plugins/{pluginId}/secrets/{key}", async (string pluginId, string key,
            IPluginSettingsBackend service, IApplicationChangePublisher changes, CancellationToken ct) =>
        { await service.RemoveSecretAsync(pluginId, key, ct); changes.Publish("connections.changed", pluginId); return Results.NoContent(); });
        api.MapPut("/plugins/{pluginId}/enabled", async (string pluginId, SetPluginEnabled request,
            IPluginSettingsBackend service, IApplicationChangePublisher changes, CancellationToken ct) =>
        { await service.SetEnabledAsync(pluginId, request.Enabled, ct); changes.Publish("connections.changed", pluginId); return Results.NoContent(); });
        api.MapPost("/plugins/{pluginId}/refresh", async (string pluginId, IPluginSettingsBackend service, CancellationToken ct) =>
        { await service.RefreshAsync(pluginId, ct); return Results.NoContent(); });
        api.MapPost("/plugins/{pluginId}/sign-in", async (string pluginId, PluginSignInRequest request, PluginConnections service, CancellationToken ct)
            => new PluginChallengeResponse(await service.BeginAsync(pluginId, request.ClientId, ct)));
        api.MapPost("/plugins/{pluginId}/sign-in/poll", async (string pluginId, PluginSignInAttempt request,
            PluginConnections service, IApplicationChangePublisher changes, CancellationToken ct) =>
        {
            var result = await service.PollAsync(pluginId, request.ClientId, request.AttemptId, ct);
            if (result.State == Winnow.PluginSdk.PluginSignInState.Connected) changes.Publish("connections.changed", pluginId);
            return result;
        });
        api.MapPost("/plugins/{pluginId}/sign-in/cancel", async (string pluginId, PluginSignInAttempt request, PluginConnections service, CancellationToken ct) =>
        { await service.CancelAsync(pluginId, request.ClientId, request.AttemptId, ct); return Results.NoContent(); });
        api.MapPost("/plugins/{pluginId}/sign-out", async (string pluginId, IPluginSettingsBackend service,
            IApplicationChangePublisher changes, CancellationToken ct) =>
        { await service.SignOutAsync(pluginId, ct); changes.Publish("connections.changed", pluginId); return Results.NoContent(); });
    }
}
