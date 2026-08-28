namespace Winnow.Resolve.Matching;

/// <summary>
/// Every number the soft matcher uses, in one place, so none of them is a magic
/// constant buried in a scoring expression. Tuning happens here (and in the
/// tests that pin the behaviour), never by editing arithmetic.
///
/// <para><b>Shape of the model.</b> Title similarity is a gate and the base of
/// the score; every other signal is a signed adjustment. Absent evidence adds
/// nothing — it is never renormalised away, because "we know nothing about the
/// year" must not score the same as "the years agree exactly". That single
/// rule is what keeps an unverified identical-title pair out of the top
/// band.</para>
///
/// <para>Defaults are calibrated so that, with no corroborating metadata at all,
/// two identical normalised titles land at <c>0.65</c>: comfortably queued for
/// a human, nowhere near the priority band. Corroboration lifts it; contrary
/// evidence sinks it below the floor.</para>
/// </summary>
public sealed record SoftMatchThresholds
{
    public static SoftMatchThresholds Default { get; } = new();

    // ── Bands ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Normalised title similarity below which the pair is not scored at all
    /// (score 0, discarded). Title agreement is a NECESSARY condition in §5.3;
    /// without this gate a publisher + year coincidence could carry a pair with
    /// half-matching titles over the queue floor, and the queue fills with
    /// same-publisher-same-year noise nobody clears.
    /// </summary>
    public double TitleSimilarityFloor { get; init; } = 0.70;

    /// <summary>
    /// Below this the pair is discarded outright — never written to
    /// <c>merge_candidates</c>. A queue full of noise is a queue nobody clears,
    /// and an uncleared queue silently converts into "the app's numbers are
    /// wrong and I can't tell why".
    /// </summary>
    public double QueueFloor { get; init; } = 0.45;

    /// <summary>
    /// At or above this the pair is queued FIRST in the review order.
    ///
    /// <para><b>This is not an auto-merge threshold and there is no auto-merge
    /// threshold.</b> In M1 the external-id hard join is the only thing allowed
    /// to merge without asking (§5.3 step 1). A 0.99 soft score means "show the
    /// user this one first", nothing more. <see cref="SoftMatchScore.AutoMergeAllowed"/>
    /// is hard-coded false for exactly this reason.</para>
    /// </summary>
    public double PriorityThreshold { get; init; } = 0.85;

    // ── Title ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Base score contributed by a perfect normalised-title match. Chosen below
    /// <see cref="PriorityThreshold"/> on purpose: an identical title is on its
    /// own never strong enough to be the top of the queue, because identical
    /// titles are precisely the Prey trap.
    /// </summary>
    public double TitleWeight { get; init; } = 0.65;

    // ── Release year (§5.3: "release year within ±1") ────────────────────────

    public double YearExactBonus { get; init; } = 0.20;

    /// <summary>±1 year. Store listings and IGDB disagree about launch dates by a day either side of new year, and regional launches straddle it.</summary>
    public double YearAdjacentBonus { get; init; } = 0.15;

    /// <summary>Largest delta still treated as "near but wrong" rather than "different game".</summary>
    public int YearNearMaxDelta { get; init; } = 3;

    public double YearNearPenalty { get; init; } = -0.10;

    /// <summary>
    /// Four or more years apart. Sized (with <see cref="TitleWeight"/>) so that
    /// a perfect title match plus a far year delta lands at 0.35 — below
    /// <see cref="QueueFloor"/>. Prey (2006) vs Prey (2017) is not a "maybe";
    /// it is a no, and it does not belong in a human's review queue.
    /// </summary>
    public double YearFarPenalty { get; init; } = -0.30;

    // ── Publisher ────────────────────────────────────────────────────────────

    public double PublisherMatchBonus { get; init; } = 0.12;

    /// <summary>
    /// Publishers differing is weak evidence against, not strong: rights move
    /// between publishers, and regional/store feeds name distributors rather
    /// than publishers often enough that a hard penalty would suppress real
    /// cross-store matches.
    /// </summary>
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
    /// Cost of one side being a content bundle (GOTY/Complete/Deluxe) and the
    /// other not. Small: it is a judgement call the user should make, and the
    /// point of the penalty is only to rank a clean pair above a bundle pair.
    ///
    /// <para>A <i>rebuild</i> edition disagreement (Special Edition, Remastered,
    /// Anniversary) is not a penalty at all — it is a veto. Those are different
    /// Releases (§9 pitfall 5) and offering to merge them is offering to
    /// corrupt the data model.</para>
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
