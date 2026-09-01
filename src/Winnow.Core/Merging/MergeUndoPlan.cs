namespace Winnow.Core.Merging;

/// <summary>
/// One row of <c>merge_applications</c> as the history screen needs it: which
/// two identities became one, when, and (for a release collapse) which release
/// absorbed which. Read-only.
/// </summary>
public sealed record MergeApplicationRecord
{
    public required long Id { get; init; }

    public required long CandidateId { get; init; }

    public required long LeftReleaseId { get; init; }

    public required long RightReleaseId { get; init; }

    public required MergeMode Mode { get; init; }

    public required long SurvivingWorkId { get; init; }

    public long? AbsorbedWorkId { get; init; }

    public long? SurvivingReleaseId { get; init; }

    public long? AbsorbedReleaseId { get; init; }

    public required DateTime AppliedAt { get; init; }

    /// <summary>
    /// The surviving work's current name, read live from <c>works</c>. Null when
    /// the surviving work no longer exists, which is itself a blocker.
    /// </summary>
    public string? SurvivingTitle { get; init; }

    /// <summary>
    /// The absorbed work's name as migration 0017's undo journal recorded it
    /// before the merge deleted the row. This is the only surviving record of
    /// that name: the <c>works</c> row is gone, and for a release collapse the
    /// <c>merge_candidates</c> row that held both titles in its
    /// <c>signals_json</c> is cascaded away with the absorbed release. Null for
    /// a merge that predates the journal, which is exactly the set that cannot
    /// be reversed.
    /// </summary>
    public string? AbsorbedTitle { get; init; }

    public DateTime? UndoneAt { get; init; }

    /// <summary>
    /// NULL for a merge applied before migration 0017, which is exactly the set
    /// that cannot be reversed. 1 is the only version that exists.
    /// </summary>
    public int? UndoJournalVersion { get; init; }

    public string? SummaryJson { get; init; }

    /// <summary>
    /// <see cref="SummaryJson"/> decoded, so a caller outside the data layer can
    /// show what moved without owning the payload's shape. Null when the row
    /// carries no summary or one that cannot be read.
    /// </summary>
    public MergeRepointCounts? Counts { get; init; }
}

/// <summary>
/// The verdict on whether one applied merge can be reversed faithfully, plus
/// every reason it cannot. A preview; it writes nothing. Reversibility depends
/// on every merge applied after this one, so the history screen recomputes it
/// on every load and never caches it.
/// </summary>
public sealed record MergeUndoPlan
{
    public required long ApplicationId { get; init; }

    /// <summary>Null when no such application row exists.</summary>
    public MergeApplicationRecord? Application { get; init; }

    public IReadOnlyList<MergeUndoBlocker> Blockers { get; init; } = [];

    /// <summary>
    /// The earliest later merge that still stands and names one of this merge's
    /// identities. Set only alongside
    /// <see cref="MergeUndoBlocker.LaterMergeConsumedIdentity"/>, and the
    /// reason the UI can offer "undo that one first" instead of a dead end.
    /// </summary>
    public long? BlockingApplicationId { get; init; }

    public bool Reversible => Blockers.Count == 0;

    /// <summary>
    /// The first blocker, or <see cref="MergeUndoBlocker.None"/> when
    /// reversible. Blockers are recorded in the order gate one tests them, so
    /// the first is the one worth showing.
    /// </summary>
    public MergeUndoBlocker PrimaryBlocker
        => Blockers.Count == 0 ? MergeUndoBlocker.None : Blockers[0];

    public static MergeUndoPlan Refused(long applicationId, MergeUndoBlocker blocker) => new()
    {
        ApplicationId = applicationId,
        Blockers = [blocker],
    };
}

/// <summary>
/// What one successful reversal put back. A refusal never produces one of these
/// (gate one throws), so every instance describes a completed undo.
/// </summary>
public sealed record MergeUndoResult
{
    public required long ApplicationId { get; init; }

    /// <summary>
    /// Where the absorbed work came back. Equal to the original id unless a
    /// later insert had taken it, in which case the identity is restored at a
    /// fresh one. Null for a merge that unified nothing at the work layer.
    /// </summary>
    public long? RestoredWorkId { get; init; }

    public long? RestoredReleaseId { get; init; }

    /// <summary>
    /// True when at least one absorbed identity had to be restored at a fresh
    /// id because a later insert reused the original. SQLite allocates rowids
    /// as max+1 without AUTOINCREMENT, so this is reachable. Nothing outside
    /// the database persists a work, release or ownership id, so the user
    /// cannot observe the difference.
    /// </summary>
    public bool IdentityIdsReused { get; init; }

    public int RowsReinserted { get; init; }

    public int RowsRepointedBack { get; init; }

    public int RowsRestoredInPlace { get; init; }
}
