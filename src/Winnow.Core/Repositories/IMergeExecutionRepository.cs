using Winnow.Core.Merging;

namespace Winnow.Core.Repositories;

/// <summary>
/// Plans and applies merge execution for confirmed merge candidates. Both
/// methods refuse any candidate that is not <c>status = 'confirmed'</c>; the
/// predicate lives in the SQL statement, not in a caller's <c>if</c>, so
/// section 5.3's "fuzzy matches never auto-merge" holds regardless of what
/// is asked.
/// </summary>
public interface IMergeExecutionRepository
{
    /// <summary>
    /// Reads only. Returns the repository's verdict on the candidate without
    /// writing anything, so the UI can preview the merge before the user commits.
    /// </summary>
    Task<MergePlan> PlanAsync(MergeRequest request, CancellationToken ct = default);

    /// <summary>
    /// Atomic: runs in one transaction and either completes fully or leaves the
    /// database exactly as it was. Plans internally, then applies the plan.
    /// </summary>
    Task<MergeOutcome> ApplyAsync(MergeRequest request, CancellationToken ct = default);

    /// <summary>
    /// Every confirmed candidate whose two releases do not yet share a work.
    /// Pairs that have already been unified are filtered out so the caller does
    /// not have to plan every historical decision to find the live ones.
    /// </summary>
    Task<IReadOnlyList<long>> GetConfirmedUnappliedCandidateIdsAsync(CancellationToken ct = default);
}
