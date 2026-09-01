namespace Winnow.Core.Merging;

/// <summary>
/// Why an applied merge cannot be reversed faithfully. An undo either restores
/// the absorbed identity and everything repointed away from it, or refuses and
/// says why; there is no partial reversal. These are gate one: cheap,
/// identity-scoped, recomputed every time the history screen loads and never
/// cached, because reversibility depends on every merge applied after this one.
/// </summary>
public enum MergeUndoBlocker
{
    /// <summary>Nothing holds the reversal back.</summary>
    None,

    /// <summary>No <c>merge_applications</c> row with that id.</summary>
    ApplicationNotFound,

    /// <summary>
    /// <c>undone_at</c> is already set. Undo is refused rather than repeated,
    /// so a second call restores nothing.
    /// </summary>
    AlreadyUndone,

    /// <summary>
    /// <c>undo_journal_version</c> is NULL. The merge was applied by a build
    /// that wrote no journal, so nothing records which rows moved or what the
    /// overwritten columns held. Vacuous on any install that has never applied
    /// a merge; real for one that upgrades across 0017.
    /// </summary>
    PredatesUndoSupport,

    /// <summary>
    /// The surviving work, or for a release collapse the surviving release, no
    /// longer exists. Its rows are gone, so there is nothing left to move back.
    /// </summary>
    GameNoLongerExists,

    /// <summary>
    /// A later merge that still stands names one of this merge's identities.
    /// Merge A absorbs R2 into R1; merge B absorbs R1 into R5. Undoing A would
    /// have to restore R2 and leave R1's rows on R1, but R1 no longer exists,
    /// and no journal can reconstruct a state that never existed. The
    /// constructive path is to undo the later merge first, which is why
    /// <see cref="MergeUndoPlan"/> names it.
    /// </summary>
    LaterMergeConsumedIdentity,
}
