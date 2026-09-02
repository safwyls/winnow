namespace Winnow.Core.Merging;

/// <summary>
/// Why a merge cannot proceed, or why it was limited to a work-only merge
/// instead of a full release collapse. <see cref="None"/> means nothing held
/// it back. A blocker that prevents any merge at all causes the plan to return
/// <see cref="MergeMode.NothingToDo"/>; the others downgrade a would-be
/// collapse to <see cref="MergeMode.WorkOnly"/>.
/// </summary>
public enum MergeBlocker
{
    /// <summary>Nothing held the merge back.</summary>
    None,

    /// <summary>No <c>merge_candidates</c> row with that id.</summary>
    CandidateNotFound,

    /// <summary>
    /// The row exists but the user has not confirmed it. Only a confirmed pair
    /// is reachable; section 5.3's "fuzzy matches never auto-merge" holds because
    /// the <c>status = 'confirmed'</c> predicate lives in the SQL statement.
    /// </summary>
    CandidateNotConfirmed,

    /// <summary>
    /// The two sides already share a work and no further collapse is available.
    /// Re-running is a no-op. Decided from state, not from the application log:
    /// a database merged by an older build, or restored from a backup, must still
    /// read as merged if its rows say so.
    /// </summary>
    AlreadyApplied,

    /// <summary>
    /// Platform, edition note, or IGDB version id disagree, or the caller's title
    /// evidence says the two sides are different editions. A collapse is refused so
    /// the two editions stay two releases under one work.
    /// </summary>
    DistinctEditions,

    /// <summary>
    /// Both releases carry achievement rows. The <c>achievements</c> table has no
    /// provider column, so two stores' achievement sets under one release_id would
    /// make section 6.2's never-blend rule unenforceable at query time. The
    /// collapse is refused outright.
    /// </summary>
    AchievementsOnBothSides,

    /// <summary>
    /// The two sides recorded different update facts at the same
    /// <c>(kind, occurred_at)</c>. Collapsing would drop one, and a merge that
    /// silently drops a fact is a worse outcome than no merge at all.
    /// </summary>
    ConflictingUpdateEvents,

    /// <summary>
    /// The request named a surviving work that is neither side of the pair.
    /// Refused rather than falling back to the ladder, so a stale choice held
    /// by a UI that has been overtaken by another answer can never merge in
    /// the wrong direction.
    /// </summary>
    PreferredSurvivorNotInPair,

    /// <summary>
    /// The absorbed work holds an <c>igdb_id</c> the survivor does not.
    /// <c>works.igdb_id</c> is UNIQUE, so the COALESCE fill in
    /// <c>UnifyWorksAsync</c> would collide with the very row it is about to
    /// delete. Under the ladder alone this is unreachable, because rung one
    /// always keeps the holder; it becomes reachable only once a survivor can
    /// be chosen. This is a limitation of destroying rows, not of the choice.
    /// The link model (TASK-70.2 onward) does not have the problem at all,
    /// because both <c>igdb_id</c> values stay on their own rows and the
    /// child keeps being enriched.
    /// </summary>
    SurvivorCannotHoldIgdbId,
}
