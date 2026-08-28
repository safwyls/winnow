namespace Winnow.Core.Domain;

/// <summary>
/// A soft-match pair of releases awaiting human confirmation. Never
/// auto-merged: fuzzy matching will confidently merge Prey (2006) with
/// Prey (2017). Status values come from <see cref="MergeCandidateStatuses"/>;
/// score is 0.0–1.0.
/// </summary>
public sealed record MergeCandidate
{
    public long Id { get; init; }
    public required long LeftReleaseId { get; init; }
    public required long RightReleaseId { get; init; }
    public required double Score { get; init; }
    public string? SignalsJson { get; init; }
    public string Status { get; init; } = MergeCandidateStatuses.Pending;
}
