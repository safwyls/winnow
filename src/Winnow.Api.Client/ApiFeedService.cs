using Winnow.Api.Contracts.Feed;
using Winnow.App.Services;

namespace Winnow.Api.Client;

/// <summary>Preserves the presentation feed contract over the public API.</summary>
public sealed class ApiFeedService(WinnowApiClient api) : IFeedService
{
    public async Task<FeedSnapshot> GetShelvesAsync(CancellationToken ct = default)
    {
        var additional = ReadSupplementAsync(ct);
        var snapshot = await api.GetAsync<FeedSnapshot>("feed", ct).ConfigureAwait(false);
        return snapshot with { AdditionalShelves = additional };
    }

    private async Task<FeedSupplement> ReadSupplementAsync(CancellationToken ct)
    {
        try { return await api.GetAsync<FeedSupplement>("feed/supplement", ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return new([], 0); }
        catch (HttpRequestException) { return new([], 0); }
    }

    public Task RecordSurfacedAsync(long releaseId, string shelfId, CancellationToken ct = default)
        => api.SendAsync(HttpMethod.Post, "feed/impressions", new FeedImpressionRequest(releaseId, shelfId), ct);

    public Task<FeedVerdictOutcome> RecordVerdictAsync(long releaseId, FeedVerdictKind kind, CancellationToken ct = default)
        => api.SendAsync<FeedFeedbackRequest, FeedVerdictOutcome>(HttpMethod.Post, "feed/feedback", new(releaseId, kind), ct: ct);

    public Task<bool> RevokeVerdictAsync(long releaseId, FeedVerdictKind kind, CancellationToken ct = default)
        => api.SendAsync<FeedFeedbackRequest, bool>(HttpMethod.Post, "feed/feedback/revoke", new(releaseId, kind), ct: ct);

    public Task<IReadOnlyList<FeedVerdictRecord>> GetHistoryAsync(CancellationToken ct = default)
        => api.GetAsync<IReadOnlyList<FeedVerdictRecord>>("feed/history", ct);
}
