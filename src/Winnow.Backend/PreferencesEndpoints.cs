using Winnow.Api.Contracts.Preferences;
using Winnow.App.Services;
using Winnow.Application;

namespace Winnow.Backend;

public static class PreferencesEndpoints
{
    public static void MapPreferencesApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").WithTags("Preferences and setup");
        api.MapGet("/preferences/artwork-sources", (ArtworkPreferences preferences) => preferences.AvailableSources);
        api.MapGet("/preferences/presentation", (PresentationPreferencesService service, CancellationToken ct)
            => service.ReadAsync(ct));
        api.MapPut("/preferences/presentation/{preference}", async (PresentationPreference preference,
            SetPresentationPreference request, PresentationPreferencesService service, CancellationToken ct) =>
        {
            if (!Enum.IsDefined(preference)) return Results.BadRequest();
            await service.SetAsync(preference, request.Value, ct);
            return Results.NoContent();
        });
        api.MapGet("/setup", async (FirstRunSetupService service, CancellationToken ct)
            => new SetupProgress(await service.LoadAsync(ct), service.StartupProblem));
        api.MapPut("/setup", async (SetSetupProgress request, FirstRunSetupService service,
            IApplicationChangePublisher changes, CancellationToken ct) =>
        {
            await service.SaveAsync(request.Step, ct);
            changes.Publish("setup.changed");
            return Results.NoContent();
        });
    }
}
