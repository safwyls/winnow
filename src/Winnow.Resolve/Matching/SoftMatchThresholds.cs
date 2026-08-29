namespace Winnow.Resolve.Matching;

/// <summary>
/// All scoring thresholds and signal weights for the soft matcher. Title
/// similarity is the gate and base score; other signals are signed adjustments.
/// Absent evidence contributes zero (never renormalised away).
/// </summary>
public sealed record SoftMatchThresholds
{
    public static SoftMatchThresholds Default { get; } = new();

    // ── Bands ────────────────────────────────────────────────────────────────

    /// <summary>Title similarity below which the pair is vetoed outright.</summary>
    public double TitleSimilarityFloor { get; init; } = 0.70;

    /// <summary>Below this the pair is discarded, never written to <c>merge_candidates</c>.</summary>
    public double QueueFloor { get; init; } = 0.45;

    /// <summary>At or above this the pair is shown first in the review queue. Not an auto-merge threshold.</summary>
    public double PriorityThreshold { get; init; } = 0.85;

    // ── Title ────────────────────────────────────────────────────────────────

    /// <summary>Base score for a perfect normalised-title match.</summary>
    public double TitleWeight { get; init; } = 0.65;

    // ── Release year (§5.3: "release year within ±1") ────────────────────────

    public double YearExactBonus { get; init; } = 0.20;

    /// <summary>±1 year. Store listings and IGDB disagree about launch dates by a day either side of new year, and regional launches straddle it.</summary>
    public double YearAdjacentBonus { get; init; } = 0.15;

    /// <summary>Largest delta still treated as "near but wrong" rather than "different game".</summary>
    public int YearNearMaxDelta { get; init; } = 3;

    public double YearNearPenalty { get; init; } = -0.10;

    /// <summary>Four or more years apart. Sized so title + far year lands below <see cref="QueueFloor"/>.</summary>
    public double YearFarPenalty { get; init; } = -0.30;

    // ── Publisher ────────────────────────────────────────────────────────────

    public double PublisherMatchBonus { get; init; } = 0.12;

    /// <summary>Weak evidence against -- rights move between publishers and feeds often name distributors.</summary>
    public double PublisherMismatchPenalty { get; init; } = -0.15;

    // ── Cover perceptual hash (Hamming distance over 64 bits) ────────────────

    public int CoverStrongMaxDistance { get; init; } = 6;
    public double CoverStrongBonus { get; init; } = 0.15;

    public int CoverWeakMaxDistance { get; init; } = 12;
    public double CoverWeakBonus { get; init; } = 0.07;

    /// <summary>Distance at or above which the covers are treated as evidence against.</summary>
    public int CoverMismatchMinDistance { get; init; } = 21;
    public double CoverMismatchPenalty { get; init; } = -0.10;

    // ── Editions ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Small penalty when one side is a content bundle (GOTY/Complete/Deluxe) and
    /// the other is not. Rebuild editions (Remastered, Anniversary) are vetoed
    /// instead, not penalised.
    /// </summary>
    public double BundleEditionMismatchPenalty { get; init; } = -0.05;

    /// <summary>Throws if the bands are ordered nonsensically. Called by <see cref="SoftMatcher"/>'s constructor.</summary>
    public void Validate()
    {
        if (QueueFloor is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(QueueFloor), QueueFloor, "Must be within [0,1].");
        }

        if (PriorityThreshold is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(PriorityThreshold), PriorityThreshold, "Must be within [0,1].");
        }

        if (TitleSimilarityFloor is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(TitleSimilarityFloor), TitleSimilarityFloor, "Must be within [0,1].");
        }

        if (PriorityThreshold < QueueFloor)
        {
            throw new ArgumentException(
                $"PriorityThreshold ({PriorityThreshold}) must be >= QueueFloor ({QueueFloor}).",
                nameof(PriorityThreshold));
        }
    }
}
