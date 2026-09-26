namespace Winnow.Resolve;

/// <summary>What one library comparison pass did.</summary>
/// <param name="Releases">Releases the sweep considered.</param>
/// <param name="Excluded">Releases skipped (non-game classification, provisional name, or empty normalised title).</param>
/// <param name="Blocks">Blocking keys that produced at least one pair.</param>
/// <param name="PairsProposed">Distinct release pairs handed to the resolver.</param>
/// <param name="Truncated">
/// True when the comparison limit cut the pass short.
/// A truncated pass does NOT record a completion time — it did not compare the
/// library, only a window of it — but it does record where to resume.
/// </param>
/// <param name="Outcome">What the resolver did with those pairs.</param>
/// <param name="Elapsed">Wall-clock time for read, blocking and resolve.</param>
/// <param name="ExcludedWithdrawn">
/// Pending pairs retired because no future sweep could propose them: a member is
/// no longer admitted (reclassified as a non-game, renamed to nothing, deleted),
/// the two sides now belong to one work, or their titles no longer share a
/// blocking key. Only proposals are retired; a confirmed or rejected answer is
/// never touched.
/// </param>
public sealed record SoftMatchSweepReport(
    int Releases,
    int Excluded,
    int Blocks,
    int PairsProposed,
    bool Truncated,
    SoftMatchOutcome Outcome,
    TimeSpan Elapsed,
    int ExcludedWithdrawn = 0)
{
    public static SoftMatchSweepReport Empty { get; } =
        new(0, 0, 0, 0, false, SoftMatchOutcome.Empty, TimeSpan.Zero);
}
