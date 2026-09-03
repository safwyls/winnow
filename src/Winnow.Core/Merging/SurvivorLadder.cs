namespace Winnow.Core.Merging;

/// <summary>
/// One side of a pair as the ladder sees it: work id plus the three facts the
/// rungs test. A pure record so the ladder is testable without a database.
/// </summary>
public sealed record SurvivorCandidate
{
    /// <summary>The work row's id.</summary>
    public required long WorkId { get; init; }

    /// <summary>Whether the work holds an <c>igdb_id</c>.</summary>
    public bool HasIgdbId { get; init; }

    /// <summary>Whether the work's name is a machine-minted placeholder.</summary>
    public bool NameIsProvisional { get; init; }

    /// <summary>How many releases hang off this work.</summary>
    public int ReleaseCount { get; init; }
}

/// <summary>
/// The ladder's verdict: which work survives, which is absorbed (null when both
/// sides were already one work), and which rung decided.
/// </summary>
public readonly record struct SurvivorDecision(
    long SurvivingWorkId,
    long? AbsorbedWorkId,
    MergeSurvivorReason Reason);

/// <summary>
/// Picks the surviving work in a merge pair. Rung order: igdb id, then name not
/// provisional, then more releases, then lowest id. The result is
/// order-independent (swapping a and b gives the same survivor and the same
/// reason). Lifted out of <c>MergeExecutionRepository.ChooseWork</c> so it is
/// BCL-only and testable without a database, and so it survives the retirement
/// of the destructive executor in TASK-70.7, where it becomes the default
/// suggestion in the survivor picker rather than the write-path decider.
/// </summary>
public static class SurvivorLadder
{
    /// <summary>
    /// Whether <paramref name="preferredWorkId"/> names one of the two works,
    /// or is null. The same validation as the throw in <see cref="Choose"/>,
    /// as a boolean, so a caller on a read path can refuse without catching
    /// an exception.
    /// </summary>
    public static bool NamesOneOf(long? preferredWorkId, long a, long b)
        => preferredWorkId is not { } preferred || preferred == a || preferred == b;

    /// <summary>
    /// Runs the ladder over two candidates. <paramref name="preferredWorkId"/>
    /// is the survivor-choice contract: null keeps the ladder; a value naming
    /// one of the two works overrides every rung and reports
    /// <see cref="MergeSurvivorReason.ChosenByYou"/>; a value naming neither
    /// throws <see cref="ArgumentOutOfRangeException"/>. The validation runs
    /// before the same-work shortcut on purpose: a preference must always name
    /// one of the two works, even when they are already one.
    /// </summary>
    public static SurvivorDecision Choose(
        SurvivorCandidate a, SurvivorCandidate b, long? preferredWorkId = null)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        // A preference naming neither side is refused, never silently ignored,
        // because falling back to the ladder would merge in a direction the
        // user did not ask for.
        if (preferredWorkId is { } named && named != a.WorkId && named != b.WorkId)
        {
            throw new ArgumentOutOfRangeException(
                nameof(preferredWorkId),
                named,
                $"Work {named} is neither side of the pair ({a.WorkId}, {b.WorkId}).");
        }

        if (a.WorkId == b.WorkId)
        {
            return new SurvivorDecision(a.WorkId, null, MergeSurvivorReason.AlreadyOneGame);
        }

        if (preferredWorkId is { } preferred)
        {
            return preferred == a.WorkId
                ? new SurvivorDecision(a.WorkId, b.WorkId, MergeSurvivorReason.ChosenByYou)
                : new SurvivorDecision(b.WorkId, a.WorkId, MergeSurvivorReason.ChosenByYou);
        }

        var (aWins, reason) =
            a.HasIgdbId != b.HasIgdbId
                ? (a.HasIgdbId, MergeSurvivorReason.IgdbMatch)
            : a.NameIsProvisional != b.NameIsProvisional
                ? (!a.NameIsProvisional, MergeSurvivorReason.NamedByStore)
            : a.ReleaseCount != b.ReleaseCount
                ? (a.ReleaseCount > b.ReleaseCount, MergeSurvivorReason.MostStoreEntries)
            : (a.WorkId < b.WorkId, MergeSurvivorReason.AddedFirst);

        return aWins
            ? new SurvivorDecision(a.WorkId, b.WorkId, reason)
            : new SurvivorDecision(b.WorkId, a.WorkId, reason);
    }
}
