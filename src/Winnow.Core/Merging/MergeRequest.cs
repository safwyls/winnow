namespace Winnow.Core.Merging;

/// <summary>
/// Asks the repository to plan or apply a merge for one confirmed candidate.
/// </summary>
public sealed record MergeRequest
{
    /// <summary>The <c>merge_candidates</c> row to act on.</summary>
    public required long CandidateId { get; init; }

    /// <summary>
    /// A ceiling, not a switch. The repository re-derives its own safety verdict
    /// from the stored rows and ANDs it with this flag, so a caller can withhold
    /// a collapse the data would permit but can never authorise one the data
    /// forbids. Defaults to <c>true</c> (let the data decide).
    /// </summary>
    public bool AllowReleaseCollapse { get; init; } = true;
}
