namespace Winnow.Resolve;

/// <summary>What one comparison pass did.</summary>
/// <param name="Compared">Distinct release pairs scored this pass.</param>
/// <param name="Queued">New <c>status='pending'</c> rows written.</param>
/// <param name="Priority">Subset of <paramref name="Queued"/> at or above the priority threshold.</param>
/// <param name="SkippedBelowFloor">Scored below the queue floor, or vetoed.</param>
/// <param name="AlreadyPending">A pending row for this pair already existed.</param>
/// <param name="PreviouslyRejected">User already said "Different games". Terminal.</param>
/// <param name="Rescored">Pending pairs whose score was refreshed on new metadata.</param>
/// <param name="Withdrawn">Pending pairs removed because they no longer clear the queue floor.</param>
/// <param name="Retired">
/// Pending pairs removed by reconciliation because no sweep could propose them
/// again — a member is gone or no longer admitted, the two sides now belong to
/// one work, or their titles no longer share a blocking key. Counted apart from
/// <paramref name="Compared"/>: these pairs were never submitted this pass, which
/// is exactly why they needed reconciling.
/// </param>
public sealed record SoftMatchOutcome(
    int Compared,
    int Queued,
    int Priority,
    int SkippedBelowFloor,
    int AlreadyPending,
    int PreviouslyRejected,
    int Rescored = 0,
    int Withdrawn = 0,
    int Retired = 0)
{
    public static SoftMatchOutcome Empty { get; } = new(0, 0, 0, 0, 0, 0);

    /// <summary>Always zero. Soft matches never auto-merge (§5.3).</summary>
    public int AutoMerged => 0;
}
