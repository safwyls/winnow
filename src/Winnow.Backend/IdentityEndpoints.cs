using Winnow.Api.Contracts.Identity;
using Winnow.Application.Identity;

namespace Winnow.Backend;

internal static class IdentityEndpoints
{
    internal static void MapIdentityEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/identity/review");
        api.MapGet("/", (IdentityReviewApplication service, CancellationToken ct) => service.GetAsync(ct));
        api.MapPost("/link", (IdentityReviewLinkRequest request, IdentityReviewApplication service, CancellationToken ct) => service.LinkAsync(request, ct));
        api.MapPost("/dismiss", (IdentityReviewDismissRequest request, IdentityReviewApplication service, CancellationToken ct) => service.DismissAsync(request, ct));
        api.MapPost("/undo", (IdentityReviewUndoRequest request, IdentityReviewApplication service, CancellationToken ct) => service.UndoAsync(request, ct));
        api.MapPut("/header", (IdentityReviewHeaderRequest request, IdentityReviewApplication service, CancellationToken ct) => service.SetHeaderAsync(request, ct));
    }
}
