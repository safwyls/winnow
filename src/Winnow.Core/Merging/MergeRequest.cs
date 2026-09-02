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

    /// <summary>
    /// The user's chosen survivor, or null to let the ladder decide. Must name
    /// one of the pair's two works; a value naming neither is refused
    /// (<see cref="MergeBlocker.PreferredSurvivorNotInPair"/>), never quietly
    /// ignored. Unlike <see cref="AllowReleaseCollapse"/>, which is a ceiling
    /// the repository ANDs with its own verdict, this one genuinely overrides
    /// the repository's preference about which side wins, because which title
    /// the library keeps is the user's call, not the data's.
    /// </summary>
    public long? PreferredSurvivingWorkId { get; init; }
}
