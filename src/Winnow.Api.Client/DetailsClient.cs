using Winnow.Api.Contracts.Details;
using Winnow.Core.Queries;

namespace Winnow.Api.Client;

public sealed class DetailsClient(WinnowApiClient client)
{
    public Task<GameDetailsResponse> GetAsync(long workId, CancellationToken ct = default)
        => client.GetAsync<GameDetailsResponse>($"games/{workId}/details", ct);
    public Task<GameDetailsResponse> GetAsync(long workId, Winnow.Api.Contracts.Library.LibraryPreferences preferences, CancellationToken ct = default)
        => client.SendAsync<Winnow.Api.Contracts.Library.LibraryPreferences, GameDetailsResponse>(HttpMethod.Post, $"games/{workId}/details", preferences, ct: ct);
    public Task<VisibilityCountsResponse> GetVisibilityCountsAsync(CancellationToken ct = default)
        => client.GetAsync<VisibilityCountsResponse>("library/visibility-counts", ct);
    public Task ExemptFromDerelictAsync(DerelictExemptionRequest request, CancellationToken ct = default)
        => client.SendAsync(HttpMethod.Post, "library/derelict-exemptions", request, ct);
    public Task<JournalResponse> GetJournalAsync(long sessionId, CancellationToken ct = default)
        => client.GetAsync<JournalResponse>($"sessions/{sessionId}/journal", ct);
    public Task<JournalResponse> SaveJournalAsync(long sessionId, SaveJournalRequest request, CancellationToken ct = default)
        => client.SendAsync<SaveJournalRequest, JournalResponse>(HttpMethod.Put, $"sessions/{sessionId}/journal", request, ct: ct);
    public Task DeleteJournalAsync(long sessionId, string expectedRevision, CancellationToken ct = default)
        => client.SendAsync(HttpMethod.Delete, $"sessions/{sessionId}/journal", new DeleteJournalRequest(expectedRevision), ct);
    public Task<AcknowledgementResponse> AcknowledgeUpdatesAsync(long releaseId, UpdateAcknowledgementRequest request, CancellationToken ct = default)
        => client.SendAsync<UpdateAcknowledgementRequest, AcknowledgementResponse>(HttpMethod.Post, $"releases/{releaseId}/acknowledge-updates", request, ct: ct);
    public Task<AcknowledgementResponse> RestoreUpdatesAsync(long releaseId, CancellationToken ct = default)
        => client.SendAsync<object, AcknowledgementResponse>(HttpMethod.Post, $"releases/{releaseId}/restore-updates", new { }, ct: ct);
    public Task<MetadataResponse> GetMetadataAsync(long workId, CancellationToken ct = default)
        => client.GetAsync<MetadataResponse>($"games/{workId}/metadata", ct);
    public Task<MutationOutcome> SetMetadataAsync(long workId, EditMetadataRequest request, CancellationToken ct = default)
        => client.SendAsync<EditMetadataRequest, MutationOutcome>(HttpMethod.Put, $"games/{workId}/metadata", request, ct: ct);
    public Task<MutationOutcome> ResetMetadataAsync(long workId, ResetMetadataRequest request, CancellationToken ct = default)
        => client.SendAsync<ResetMetadataRequest, MutationOutcome>(HttpMethod.Post, $"games/{workId}/metadata/reset", request, ct: ct);
    public Task<IReadOnlyList<IgdbCandidateResponse>> SearchIgdbAsync(string title, CancellationToken ct = default)
        => client.GetAsync<IReadOnlyList<IgdbCandidateResponse>>("metadata/igdb/search?title=" + Uri.EscapeDataString(title), ct);
    public Task<IgdbCandidateResponse> GetIgdbCandidateAsync(long id, CancellationToken ct = default)
        => client.GetAsync<IgdbCandidateResponse>($"metadata/igdb/{id}", ct);
    public Task<IgdbClaimingGameResponse> GetIgdbClaimingGameAsync(long id, CancellationToken ct = default)
        => client.GetAsync<IgdbClaimingGameResponse>($"metadata/igdb/{id}/claiming-game", ct);
    public Task<MutationOutcome> AssignIgdbAsync(long workId, AssignIgdbRequest request, CancellationToken ct = default)
        => client.SendAsync<AssignIgdbRequest, MutationOutcome>(HttpMethod.Put, $"games/{workId}/igdb", request, ct: ct);
    public Task<bool> ClearIgdbAsync(long workId, ClearIgdbRequest request, CancellationToken ct = default)
        => client.SendAsync<ClearIgdbRequest, bool>(HttpMethod.Delete, $"games/{workId}/igdb", request, ct: ct);
    public Task<IgdbStateResponse> GetIgdbStateAsync(long workId, CancellationToken ct = default)
        => client.GetAsync<IgdbStateResponse>($"games/{workId}/igdb/state", ct);
    public Task<Winnow.Core.Domain.WorkIgdbPin> GetIgdbPinAsync(long workId, CancellationToken ct = default)
        => client.GetAsync<Winnow.Core.Domain.WorkIgdbPin>($"games/{workId}/igdb", ct);
    public Task<ActivityPage> GetActivityAsync(ActivityRequest request, CancellationToken ct = default)
        => client.SendAsync<ActivityRequest, ActivityPage>(HttpMethod.Post, "activity/query", request, ct: ct);
    public Task<GameplayStats> GetGameplayStatsAsync(GameplayStatisticsRequest request, CancellationToken ct = default)
        => client.SendAsync<GameplayStatisticsRequest, GameplayStats>(HttpMethod.Post, "statistics/gameplay", request, ct: ct);
    public Task<AccountStats> GetAccountStatsAsync(string source, CancellationToken ct = default)
        => client.GetAsync<AccountStats>("statistics/accounts/" + Uri.EscapeDataString(source), ct);
}
