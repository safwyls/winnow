using Winnow.Api.Contracts.Identity;

namespace Winnow.Api.Client;

public sealed class IdentityReviewClient(WinnowApiClient api)
{
    public Task<IdentityReviewResponse> GetAsync(CancellationToken ct = default) => api.GetAsync<IdentityReviewResponse>("identity/review/", ct);
    public Task<IdentityReviewMutation> LinkAsync(IdentityReviewLinkRequest request, CancellationToken ct = default) =>
        api.SendAsync<IdentityReviewLinkRequest, IdentityReviewMutation>(HttpMethod.Post, "identity/review/link", request, ct: ct);
    public Task<IdentityReviewMutation> DismissAsync(IdentityReviewDismissRequest request, CancellationToken ct = default) =>
        api.SendAsync<IdentityReviewDismissRequest, IdentityReviewMutation>(HttpMethod.Post, "identity/review/dismiss", request, ct: ct);
    public Task<IdentityReviewMutation> UndoAsync(IdentityReviewUndoRequest request, CancellationToken ct = default) =>
        api.SendAsync<IdentityReviewUndoRequest, IdentityReviewMutation>(HttpMethod.Post, "identity/review/undo", request, ct: ct);
    public Task<IdentityReviewMutation> SetHeaderAsync(IdentityReviewHeaderRequest request, CancellationToken ct = default) =>
        api.SendAsync<IdentityReviewHeaderRequest, IdentityReviewMutation>(HttpMethod.Put, "identity/review/header", request, ct: ct);
}
