using Winnow.Core.Merging;

namespace Winnow.Core.Repositories;

/// <summary>
/// Plans and applies merge execution for merge candidates. The two write-path
/// methods (<see cref="PlanAsync"/> and <see cref="ApplyAsync"/>) refuse any
/// candidate that is not <c>status = 'confirmed'</c>; the predicate lives in
/// the SQL statement, not in a caller's <c>if</c>, so section 5.3's "fuzzy
/// matches never auto-merge" holds regardless of what is asked.
/// <see cref="PreviewAsync"/> is the exception: it admits <c>pending</c> as
/// well, because the review card must state what an answer will do before the
/// answer is given. It writes nothing, so admitting a pending pair does not
/// weaken section 5.3.
/// </summary>
public interface IMergeExecutionRepository
{
    /// <summary>
    /// Reads only. Returns the repository's verdict on the candidate without
    /// writing anything, so the UI can preview the merge before the user commits.
    /// </summary>
    Task<MergePlan> PlanAsync(MergeRequest request, CancellationToken ct = default);

    /// <summary>
    /// Reads only, writes nothing. The one method on this interface that will
    /// look at a pair the user has <b>not answered yet</b>, admitting both
    /// <c>pending</c> and <c>confirmed</c> statuses. <see cref="PlanAsync"/>
    /// and <see cref="ApplyAsync"/> refuse anything that is not
    /// <c>confirmed</c>. This exists because the review card must state what
    /// an answer will do before the answer is given, and there is no second
    /// step to catch a wrong outcome. It does not weaken section 5.3: nothing
    /// that writes can reach a pending pair through this path.
    /// </summary>
    Task<MergePlan> PreviewAsync(MergeRequest request, CancellationToken ct = default);

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
