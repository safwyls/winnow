using Winnow.Core.Domain;
using Winnow.Core.Queries;

namespace Winnow.Api.Contracts.Details;

public sealed record GameDetailsResponse(long WorkId, DateTime ReadAtUtc,
    IReadOnlyList<UpdateEvent> Events, IReadOnlyDictionary<long, DateTime> Acknowledgements,
    IReadOnlyDictionary<long, IReadOnlyList<PlaytimeSnapshot>> History,
    IReadOnlyDictionary<long, IReadOnlyList<Session>> Sessions,
    IReadOnlyList<WorkRating> Ratings, IReadOnlyList<WorkImages> Images,
    IReadOnlyList<Ownership> Ownerships, IReadOnlyList<SessionJournalEntry> JournalEntries,
    IReadOnlyList<GameListMembership> ListMemberships, IReadOnlyList<ReleaseAchievementSummary> Achievements);
public sealed record JournalResponse(long SessionId, string? Note, int? Rating, string Revision);
public sealed record SaveJournalRequest(string? Note, int? Rating, string ExpectedRevision);
public sealed record DeleteJournalRequest(string ExpectedRevision);
public sealed record UpdateAcknowledgementRequest(IReadOnlyList<long> ObservedEventIds);
public sealed record AcknowledgementResponse(string Result, DateTime? AcknowledgedThrough);
public sealed record MetadataFieldResponse(string Field, string? Value, string? Source);
public sealed record MetadataResponse(long WorkId, string Title, bool IsPinned, IReadOnlyList<MetadataFieldResponse> Fields, string Revision, bool Available = true);
public sealed record EditMetadataRequest(string Field, string? Value, string ExpectedRevision);
public sealed record ResetMetadataRequest(string Field, string ExpectedRevision);
public sealed record UploadMetadataArtRequest(string Field, byte[] Content, string ExpectedRevision);
public sealed record DownloadMetadataArtRequest(string Field, string Url, string ExpectedRevision);
public sealed record MutationOutcome(string Outcome);
public sealed record IgdbCandidateResponse(long IgdbId, string Name, string? CoverUrl, int? FirstReleaseYear, IReadOnlyList<string> Platforms);
public sealed record IgdbClaimingGameResponse(long WorkId, string Title, string? CoverUrl, int? FirstReleaseYear);
public sealed record AssignIgdbRequest(long IgdbId, string ExpectedRevision);
public sealed record ClearIgdbRequest(string ExpectedRevision);
public sealed record IgdbStateResponse(long WorkId, long MappingRevision, WorkIgdbPin? Pin, string Revision, bool Available = true);
public sealed record ActivityRequest(DateTime FromUtc, DateTime UntilUtc, ActivitySection Section,
    ActivityCursor? After = null, int PageSize = 50, long? WorkId = null);
public sealed record GameplayStatisticsRequest(DateTime FromUtc, DateTime UntilUtc, DateTime AsOfUtc,
    IReadOnlyList<GameplayTimeBin> TimeBins, string? Store = null);
public sealed record VisibilityCountsResponse(int AccountHidden, int ExplicitHidden, int RatingCapHidden);
public sealed record DerelictExemptionRequest(IReadOnlyList<long> WorkIds);
public sealed record SteamActivityRequest(IReadOnlyList<long> OwnershipIds);
public sealed record SteamActivityResponse(bool AccountConfirmationRequired, IReadOnlyList<SteamReportedActivity> Activity);
public sealed record JournalPreferences(bool PromptAfterPlay);
public sealed record SessionPromptResponse(long SessionId, long OwnershipId, long DurationSeconds);
