using Winnow.Api.Contracts.Actions;
using Winnow.Application;

namespace Winnow.Backend;

public static class GameActionEndpoints
{
    public static void MapGameActionApi(this WebApplication app)
        => app.MapPost("/api/v1/entries/{ownershipId:long}/actions",
            (long ownershipId, GameActionRequest request, GameActionApplication service, CancellationToken ct)
                => service.ExecuteAsync(ownershipId, request, ct)).WithTags("Game actions");
}
