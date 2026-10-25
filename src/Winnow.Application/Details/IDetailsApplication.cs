using Winnow.Api.Contracts.Details;
using Winnow.Api.Contracts.Library;
using Winnow.Core.Domain;
using Winnow.Core.Queries;

namespace Winnow.Application.Details;

public interface IDetailsApplication
{
    Task<VisibilityCountsResponse> GetVisibilityCountsAsync(CancellationToken ct = default);
    Task ExemptFromDerelictAsync(DerelictExemptionRequest request, CancellationToken ct = default);
    Task<GameDetailsResponse> GetDetailsAsync(long workId, LibraryPreferences? preferences = null, CancellationToken ct = default);
    Task<JournalResponse> GetJournalAsync(long sessionId, CancellationToken ct = default);
    Task<JournalResponse> SaveJournalAsync(long sessionId, SaveJournalRequest request, CancellationToken ct = default);
    Task DeleteJournalAsync(long sessionId, string expectedRevision, CancellationToken ct = default);
    Task<AcknowledgementResponse> AcknowledgeUpdatesAsync(long releaseId, UpdateAcknowledgementRequest request, CancellationToken ct = default);
    Task<AcknowledgementResponse> RestoreUpdatesAsync(long releaseId, CancellationToken ct = default);
    Task<AcknowledgementResponse> GetAcknowledgementAsync(long releaseId, CancellationToken ct = default);
    Task<MetadataResponse> GetMetadataAsync(long workId, CancellationToken ct = default);
    Task<MutationOutcome> SetMetadataAsync(long workId, EditMetadataRequest request, CancellationToken ct = default);
    Task<MutationOutcome> ResetMetadataAsync(long workId, ResetMetadataRequest request, CancellationToken ct = default);
    Task<MutationOutcome> UploadArtAsync(long workId, UploadMetadataArtRequest request, CancellationToken ct = default);
    Task<MutationOutcome> DownloadArtAsync(long workId, DownloadMetadataArtRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<IgdbCandidateResponse>> SearchIgdbAsync(string title, CancellationToken ct = default);
    Task<IgdbCandidateResponse?> GetIgdbCandidateAsync(long igdbId, CancellationToken ct = default);
    Task<IgdbClaimingGameResponse?> GetIgdbClaimingGameAsync(long igdbId, CancellationToken ct = default);
    Task<MutationOutcome> AssignIgdbAsync(long workId, AssignIgdbRequest request, CancellationToken ct = default);
    Task<bool> ClearIgdbAsync(long workId, ClearIgdbRequest request, CancellationToken ct = default);
    Task<IgdbStateResponse> GetIgdbStateAsync(long workId, CancellationToken ct = default);
    Task<WorkIgdbPin?> GetIgdbPinAsync(long workId, CancellationToken ct = default);
    Task<ActivityPage> GetActivityAsync(ActivityRequest request, CancellationToken ct = default);
    Task<GameplayStats> GetGameplayStatsAsync(GameplayStatisticsRequest request, CancellationToken ct = default);
    Task<AccountStats> GetAccountStatsAsync(string source, CancellationToken ct = default);
    Task<SteamActivityResponse> GetSteamActivityAsync(SteamActivityRequest request, CancellationToken ct = default);
    Task<SessionPromptResponse> GetSessionPromptAsync(long sessionId, CancellationToken ct = default);
}
