using Microsoft.AspNetCore.Mvc;
using Winnow.Api.Contracts.Library;
using Winnow.Application.Library;

namespace Winnow.Backend;

internal static class LibraryEndpoints
{
    public static void MapLibraryEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1");
        api.MapPost("/library/workspace", (LibraryPreferences preferences, ILibraryApplication service, CancellationToken ct) => service.GetWorkspaceAsync(preferences, ct));
        api.MapGet("/library/workspace", async (bool? showNonGameEntries, bool? showExplicitContent, string? maturityCap, ILibraryApplication service, CancellationToken ct) =>
        {
            var preferences = await service.GetPreferencesAsync(ct);
            return await service.GetWorkspaceAsync(new LibraryPreferences(showNonGameEntries ?? preferences.ShowNonGameEntries,
                showExplicitContent ?? preferences.ShowExplicitContent, maturityCap ?? preferences.MaturityCap), ct);
        });
        api.MapGet("/manual-games", (ILibraryApplication service, CancellationToken ct) => service.GetManualGamesAsync(ct));
        api.MapGet("/manual-games/{ownershipId:long}", async (long ownershipId, ILibraryApplication service, CancellationToken ct) =>
            await service.GetManualGameAsync(ownershipId, ct) is { } game ? Results.Ok(game) : Results.NotFound());
        api.MapPost("/lists/live", (CreateLiveListRequest request, ILibraryApplication service, CancellationToken ct) => service.CreateLiveListAsync(request, ct));
        api.MapPut("/lists/{listId:long}/filter", async (long listId, SetListFilterRequest request, ILibraryApplication service, CancellationToken ct) =>
        { return await service.SetListFilterAsync(listId, request, ct); });
        api.MapGet("/library", (ILibraryApplication service, CancellationToken ct) => service.GetLibraryAsync(ct));
        api.MapGet("/games/{workId:long}", async (long workId, ILibraryApplication service, CancellationToken ct) =>
            await service.GetGameAsync(workId, ct) is { } game ? Results.Ok(game) : Results.NotFound());
        api.MapGet("/hidden-games", (ILibraryApplication service, CancellationToken ct) => service.GetHiddenGamesAsync(ct));
        api.MapPut("/hidden-games", async (SetHiddenRequest request, ILibraryApplication service, CancellationToken ct) =>
        { await service.SetHiddenAsync(request, ct); return Results.NoContent(); });
        api.MapPost("/lists", (CreateListRequest request, ILibraryApplication service, CancellationToken ct) => service.CreateListAsync(request, ct));
        api.MapPut("/lists/{listId:long}", (long listId, EditListRequest request, ILibraryApplication service, CancellationToken ct) => service.EditListAsync(listId, request, ct));
        api.MapDelete("/lists/{listId:long}", async (long listId, [FromBody] DeleteListRequest request, ILibraryApplication service, CancellationToken ct) =>
        { await service.DeleteListAsync(listId, request.ExpectedRevision, ct); return Results.NoContent(); });
        api.MapPost("/lists/{listId:long}/members", (long listId, ListMembersRequest request, ILibraryApplication service, CancellationToken ct) => service.AddListMembersAsync(listId, request, ct));
        api.MapDelete("/lists/{listId:long}/members", (long listId, [FromBody] ListMembersRequest request, ILibraryApplication service, CancellationToken ct) => service.RemoveListMembersAsync(listId, request, ct));
        api.MapPut("/lists/{listId:long}/order", (long listId, ListMembersRequest request, ILibraryApplication service, CancellationToken ct) => service.ReorderListAsync(listId, request, ct));
        api.MapPost("/manual-games", (ManualGameRequest request, ILibraryApplication service, CancellationToken ct) => service.CreateManualGameAsync(request, ct));
        api.MapPut("/manual-games/{ownershipId:long}", (long ownershipId, ManualGameRequest request, ILibraryApplication service, CancellationToken ct) => service.UpdateManualGameAsync(ownershipId, request, ct));
        api.MapDelete("/manual-games/{ownershipId:long}", async (long ownershipId, ILibraryApplication service, CancellationToken ct) =>
        { await service.DeleteManualGameAsync(ownershipId, ct); return Results.NoContent(); });
        api.MapPost("/identity/links", (LinkGamesRequest request, ILibraryApplication service, CancellationToken ct) => service.LinkGamesAsync(request, ct));
        api.MapDelete("/identity/links/{childWorkId:long}", async (long childWorkId, [FromBody] SeparateGameRequest request, ILibraryApplication service, CancellationToken ct) =>
        { await service.SeparateGameAsync(childWorkId, request, ct); return Results.NoContent(); });
        api.MapPost("/identity/acts/{actId:long}/undo", async (long actId, ILibraryApplication service, CancellationToken ct) =>
        { await service.UndoIdentityActAsync(actId, ct); return Results.NoContent(); });
        api.MapGet("/preferences/library", (ILibraryApplication service, CancellationToken ct) => service.GetPreferencesAsync(ct));
        api.MapPut("/preferences/library", async (LibraryPreferences request, ILibraryApplication service, CancellationToken ct) =>
        { await service.SetPreferencesAsync(request, ct); return Results.NoContent(); });
    }
}
