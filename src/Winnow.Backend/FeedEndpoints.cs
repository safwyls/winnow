using Winnow.Api.Contracts.Feed;
using Winnow.App.Services;
using Winnow.Application;

namespace Winnow.Backend;

public static class FeedEndpoints
{
    public static void MapFeedApi(this WebApplication app)
    {
        var feed = app.MapGroup("/api/v1/feed").WithTags("Recommendations");
        feed.MapGet("", (FeedService service, CancellationToken ct) => service.GetBuiltInShelvesAsync(ct));
        feed.MapGet("/supplement", (PluginFeedService service, TimeProvider clock, CancellationToken ct)
            => service.GetShelvesAsync(clock.GetUtcNow().UtcDateTime, ct));
        feed.MapGet("/history", (IFeedService service, CancellationToken ct) => service.GetHistoryAsync(ct));
        feed.MapPost("/impressions", async (FeedImpressionRequest request, IFeedService service, CancellationToken ct) =>
        {
            if (request.ReleaseId <= 0 || string.IsNullOrWhiteSpace(request.ShelfId) || request.ShelfId.Length > 128)
                return Results.BadRequest();
            await service.RecordSurfacedAsync(request.ReleaseId, request.ShelfId, ct);
            return Results.NoContent();
        });
        feed.MapPost("/feedback", async (FeedFeedbackRequest request, IFeedService service,
            IApplicationChangePublisher changes, CancellationToken ct) =>
        {
            if (request.ReleaseId <= 0 || !Enum.IsDefined(request.Kind)) return Results.BadRequest();
            var result = await service.RecordVerdictAsync(request.ReleaseId, request.Kind, ct);
            if (result.Saved) changes.Publish("feed.changed");
            return Results.Ok(result);
        });
        feed.MapPost("/feedback/revoke", async (FeedFeedbackRequest request, IFeedService service,
            IApplicationChangePublisher changes, CancellationToken ct) =>
        {
            if (request.ReleaseId <= 0 || !Enum.IsDefined(request.Kind)) return Results.BadRequest();
            var result = await service.RevokeVerdictAsync(request.ReleaseId, request.Kind, ct);
            if (result) changes.Publish("feed.changed");
            return Results.Ok(result);
        });
    }
}
