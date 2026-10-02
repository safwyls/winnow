using Microsoft.AspNetCore.Mvc;
using Winnow.Api.Contracts.Details;
using Winnow.Api.Contracts.Library;
using Winnow.Application.Details;

namespace Winnow.Backend;

internal static class DetailsEndpoints
{
    public static void MapDetailsEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1");
        api.MapGet("/journal/preferences", async (Winnow.App.Services.SessionJournalService service, CancellationToken ct) =>
        { await service.LoadAsync(ct); return new JournalPreferences(service.PromptEnabled); });
        api.MapPut("/journal/preferences", async (JournalPreferences request, Winnow.App.Services.SessionJournalService service, Winnow.Application.IApplicationChangePublisher changes, CancellationToken ct) =>
        { await service.SetPromptEnabledAsync(request.PromptAfterPlay, ct); changes.Publish("preferences.changed", "journal"); return Results.NoContent(); });
        api.MapGet("/sessions/{sessionId:long}/prompt", (long sessionId, IDetailsApplication service, CancellationToken ct) => service.GetSessionPromptAsync(sessionId, ct));
        api.MapGet("/games/{workId:long}/details", (long workId, IDetailsApplication service, CancellationToken ct) => service.GetDetailsAsync(workId, ct: ct));
        api.MapPost("/games/{workId:long}/details", (long workId, LibraryPreferences preferences, IDetailsApplication service, CancellationToken ct) => service.GetDetailsAsync(workId, preferences, ct));
        api.MapGet("/library/visibility-counts", (IDetailsApplication service, CancellationToken ct) => service.GetVisibilityCountsAsync(ct));
        api.MapPost("/library/derelict-exemptions", async (DerelictExemptionRequest request, IDetailsApplication service, CancellationToken ct) =>
        { await service.ExemptFromDerelictAsync(request, ct); return Results.NoContent(); });
        api.MapGet("/sessions/{sessionId:long}/journal", (long sessionId, IDetailsApplication service, CancellationToken ct) => service.GetJournalAsync(sessionId, ct));
        // Once the complete note is accepted, closing its frontend must not cancel the write.
        // The host drains this request during graceful shutdown; a lost response is reconciled by revision.
        api.MapPut("/sessions/{sessionId:long}/journal", (long sessionId, SaveJournalRequest request, IDetailsApplication service) => service.SaveJournalAsync(sessionId, request, CancellationToken.None));
        api.MapDelete("/sessions/{sessionId:long}/journal", async (long sessionId, [FromBody] DeleteJournalRequest request, IDetailsApplication service, CancellationToken ct) =>
        { await service.DeleteJournalAsync(sessionId, request.ExpectedRevision, ct); return Results.NoContent(); });
        api.MapPost("/releases/{releaseId:long}/acknowledge-updates", (long releaseId, UpdateAcknowledgementRequest request, IDetailsApplication service, CancellationToken ct) => service.AcknowledgeUpdatesAsync(releaseId, request, ct));
        api.MapPost("/releases/{releaseId:long}/restore-updates", (long releaseId, IDetailsApplication service, CancellationToken ct) => service.RestoreUpdatesAsync(releaseId, ct));
        api.MapGet("/releases/{releaseId:long}/acknowledgement", (long releaseId, IDetailsApplication service, CancellationToken ct) => service.GetAcknowledgementAsync(releaseId, ct));
        api.MapGet("/games/{workId:long}/metadata", (long workId, IDetailsApplication service, CancellationToken ct) => service.GetMetadataAsync(workId, ct));
        api.MapPut("/games/{workId:long}/metadata", (long workId, EditMetadataRequest request, IDetailsApplication service, CancellationToken ct) => service.SetMetadataAsync(workId, request, ct));
        api.MapPost("/games/{workId:long}/metadata/reset", (long workId, ResetMetadataRequest request, IDetailsApplication service, CancellationToken ct) => service.ResetMetadataAsync(workId, request, ct));
        api.MapPost("/games/{workId:long}/metadata/art-upload", (long workId, UploadMetadataArtRequest request, IDetailsApplication service, CancellationToken ct) => service.UploadArtAsync(workId, request, ct));
        api.MapPost("/games/{workId:long}/metadata/art-download", (long workId, DownloadMetadataArtRequest request, IDetailsApplication service, CancellationToken ct) => service.DownloadArtAsync(workId, request, ct));
        api.MapGet("/metadata/igdb/search", (string title, IDetailsApplication service, CancellationToken ct) => service.SearchIgdbAsync(title, ct));
        api.MapGet("/metadata/igdb/{igdbId:long}", async (long igdbId, IDetailsApplication service, CancellationToken ct) => await service.GetIgdbCandidateAsync(igdbId, ct) is { } candidate ? Results.Ok(candidate) : Results.NotFound());
        api.MapGet("/metadata/igdb/{igdbId:long}/claiming-game", async (long igdbId, IDetailsApplication service, CancellationToken ct) => await service.GetIgdbClaimingGameAsync(igdbId, ct) is { } holder ? Results.Ok(holder) : Results.NotFound());
        api.MapGet("/games/{workId:long}/igdb", async (long workId, IDetailsApplication service, CancellationToken ct) => await service.GetIgdbPinAsync(workId, ct) is { } pin ? Results.Ok(pin) : Results.NotFound());
        api.MapGet("/games/{workId:long}/igdb/state", (long workId, IDetailsApplication service, CancellationToken ct) => service.GetIgdbStateAsync(workId, ct));
        api.MapPut("/games/{workId:long}/igdb", (long workId, AssignIgdbRequest request, IDetailsApplication service, CancellationToken ct) => service.AssignIgdbAsync(workId, request, ct));
        api.MapDelete("/games/{workId:long}/igdb", (long workId, [Microsoft.AspNetCore.Mvc.FromBody] ClearIgdbRequest request, IDetailsApplication service, CancellationToken ct) => service.ClearIgdbAsync(workId, request, ct));
        api.MapPost("/activity/query", (ActivityRequest request, IDetailsApplication service, CancellationToken ct) => service.GetActivityAsync(request, ct));
        api.MapPost("/activity/steam", (SteamActivityRequest request, IDetailsApplication service, CancellationToken ct) => service.GetSteamActivityAsync(request, ct));
        api.MapPost("/statistics/gameplay", (GameplayStatisticsRequest request, IDetailsApplication service, CancellationToken ct) => service.GetGameplayStatsAsync(request, ct));
        api.MapGet("/statistics/accounts/{source}", (string source, IDetailsApplication service, CancellationToken ct) => service.GetAccountStatsAsync(source, ct));
    }
}
