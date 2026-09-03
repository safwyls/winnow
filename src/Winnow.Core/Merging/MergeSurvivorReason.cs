namespace Winnow.Core.Merging;

/// <summary>
/// Which rung of <see cref="SurvivorLadder.Choose"/> decided the surviving work.
/// The repository picks the survivor; the view layer words the reason. This enum
/// is the value the UI renders, not a prose string built in the repository.
/// </summary>
public enum MergeSurvivorReason
{
    /// <summary>No survivor was chosen. A plan that can do nothing.</summary>
    None,

    /// <summary>Both sides already sit under one work, so there was nothing to decide.</summary>
    AlreadyOneGame,

    /// <summary>
    /// One side holds an <c>igdb_id</c> and the other does not. First rung,
    /// because <c>works.igdb_id</c> is UNIQUE and therefore the one fact that
    /// cannot be copied onto the other row; preferring its holder is the only
    /// way to keep it.
    /// </summary>
    IgdbMatch,

    /// <summary>
    /// One side's name is not provisional (a real title from a store, not a
    /// machine-minted placeholder).
    /// </summary>
    NamedByStore,

    /// <summary>One side already has more releases hanging off it.</summary>
    MostStoreEntries,

    /// <summary>
    /// Nothing else discriminated, so the lower work id won. That is ingestion
    /// order, and the card says so rather than staying silent.
    /// </summary>
    AddedFirst,

    /// <summary>
    /// The user picked the survivor, overriding every rung. The picker UI
    /// lands in TASK-70.3; the contract is defined here.
    /// </summary>
    ChosenByYou,
}
