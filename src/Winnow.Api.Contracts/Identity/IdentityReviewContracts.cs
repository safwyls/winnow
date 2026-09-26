using Winnow.Api.Contracts.Library;
using Winnow.Core.Domain;
using Winnow.Core.Identity;

namespace Winnow.Api.Contracts.Identity;

public sealed record IdentityReviewResponse(string Revision, bool HasCompletedSweep,
    IReadOnlyList<MergeCandidate> Candidates, IReadOnlyList<IdentityLink> History,
    IReadOnlyList<IdentityAct> Acts, IReadOnlyList<ExpansionGroupResponse> Expansions,
    LibraryWorkspaceResponse Workspace);
public sealed record ExpansionWorkResponse(long WorkId, string Title, IReadOnlyList<long> ReleaseIds);
public sealed record ExpansionMemberResponse(ExpansionWorkResponse Work, ExpansionEvidence Evidence,
    string Kind, string? RelationLabel, bool FromMetadata);
public sealed record ExpansionGroupResponse(ExpansionWorkResponse Base, IReadOnlyList<ExpansionMemberResponse> Members);
public sealed record IdentityReviewLinkRequest(string ExpectedRevision, long ParentWorkId,
    IReadOnlyList<long> ChildWorkIds, string Kind, string? RelationLabel,
    IReadOnlyList<long> RejectedCandidateIds, IReadOnlyList<ExpansionRefusalRequest> RefusedPairs);
public sealed record IdentityReviewDismissRequest(string ExpectedRevision, IReadOnlyList<long> CandidateIds,
    IReadOnlyList<ExpansionRefusalRequest> RefusedPairs);
public sealed record IdentityReviewUndoRequest(string ExpectedRevision, IReadOnlyList<long> ActIds,
    IReadOnlyList<long> CandidateIds, IReadOnlyList<ExpansionRefusalRequest> RefusedPairs);
public sealed record IdentityReviewHeaderRequest(string ExpectedRevision, long WorkId, string? Store);
public sealed record IdentityReviewMutation(string Revision, long? ActId = null, bool Changed = true);
