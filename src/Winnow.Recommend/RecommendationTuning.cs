namespace Winnow.Recommend;

/// <summary>
/// Every weight and threshold the scoring model uses, named and defaulted.
/// Retuning changes the next feed without touching stored data.
/// </summary>
public sealed record RecommendationTuning
{
    // ── Positive signal weights ─────────────────────────────────────────────
    // Values are all in [0,1]; contribution = weight × value. Ranked by how
    // much of the moat each signal is: patch-after-dormancy is the fact no
    // storefront can compute and is retroactively available on day one, so it
    // leads by a distance.

    /// <summary>Bucket `stale_but_patched`: a correlated major update landed after the user walked away. The headline.</summary>
    public double WeightPatchAfterDormancy { get; init; } = 0.40;

    /// <summary>Where the playtime sits against the refund line — the bounced pile peaks (committed, then gave up).</summary>
    public double WeightCommitment { get; init; } = 0.25;

    /// <summary>Time since last played, saturating (never decaying — see the measured dormancy distribution).</summary>
    public double WeightDormancy { get; init; } = 0.15;

    /// <summary>Candidate carries a descriptor the user's hours concentrate in. Tiebreaker for shelfware.</summary>
    public double WeightTasteAffinity { get; init; } = 0.10;

    /// <summary>Tier 1: distinct return episodes. 40 minutes across six sessions is someone trying to like it.</summary>
    public double WeightTriedToLikeIt { get; init; } = 0.10;

    /// <summary>On disk right now — zero friction to launch.</summary>
    public double WeightInstalled { get; init; } = 0.05;

    /// <summary>Same work owned on two stores: a purchase made twice. Fires only after cross-store merges are confirmed.</summary>
    public double WeightBoughtTwice { get; init; } = 0.05;

    // ── Penalties ───────────────────────────────────────────────────────────

    /// <summary>Played within the fresh window. Sized to dominate all positive signals.</summary>
    public double PenaltyRecentlyPlayed { get; init; } = 0.60;

    /// <summary>Fair shake + deep dormancy + no changes since. Drops the row to the back of the feed.</summary>
    public double PenaltyProbablyDone { get; init; } = 0.30;

    /// <summary>Recently surfaced — rotate behind unshown near-peers.</summary>
    public double PenaltyRecentlySurfaced { get; init; } = 0.20;

    /// <summary>Candidate is on the wrong side of the single-player/online line. Demotion, not exclusion (mode facets can be wrong).</summary>
    public double PenaltyModeMismatch { get; init; } = 0.10;

    // ── Mode-mismatch evidence floors ───────────────────────────────────────

    /// <summary>Committed mode-carrying games required before the profile may claim a dominant mode.</summary>
    public int ModeEvidenceMinGames { get; init; } = 20;

    /// <summary>Share of committed mode-carrying games on one side before the other counts as a mismatch.</summary>
    public double ModeDominanceShare { get; init; } = 0.85;

    // ── Time thresholds ─────────────────────────────────────────────────────

    /// <summary>Days within which a game counts as "playing it now" (matches Steam's playtime_2weeks).</summary>
    public double FreshPlayWindowDays { get; init; } = 14;

    /// <summary>Years of dormancy at which the ramp saturates (older games are treated as equally dormant).</summary>
    public double DormancySaturationYears { get; init; } = 2.0;

    /// <summary>Dormancy gate (years) for the probably-done penalty.</summary>
    public double DeepDormancyYears { get; init; } = 4.0;

    // ── Playtime thresholds ─────────────────────────────────────────────────
    // The refund line and retired floor are NOT here: they arrive on the
    // request's BucketThresholds, because they are §6.1's numbers and two
    // copies would drift.

    /// <summary>Minutes past which "abandoned" usually means "finished with it" (~33 hours). Provisional.</summary>
    public double FairShakeMinutes { get; init; } = 2_000;

    // ── Curve shape ─────────────────────────────────────────────────────────

    /// <summary>Commitment value as playtime approaches the retired floor.</summary>
    public double CommitmentFloorValue { get; init; } = 0.15;

    /// <summary>Never-opened base value. Keeps shelfware ranked without outranking rows with history.</summary>
    public double ShelfwareBaseValue { get; init; } = 0.35;

    /// <summary>Sampled (1..refund-line minutes) ramps from this base.</summary>
    public double SampledBaseValue { get; init; } = 0.50;

    /// <summary>Span added to <see cref="SampledBaseValue"/>, topping out below the bounced peak.</summary>
    public double SampledSpanValue { get; init; } = 0.20;

    /// <summary>Return episodes at which the tried-to-like-it signal saturates.</summary>
    public int TriedToLikeSaturationEpisodes { get; init; } = 3;

    /// <summary>Jitter maximum. Below the smallest deliberate weight gap so only near-ties swap.</summary>
    public double JitterAmplitude { get; init; } = 0.03;

    // ── Shelves ─────────────────────────────────────────────────────────────

    /// <summary>Taste-affinity floor for the <see cref="ShelfIds.OnYourTaste"/> shelf (normalised against the profile's peak).</summary>
    public double OnTasteMinAffinity { get; init; } = 0.6;

    /// <summary>Prevalence share past which a facet is too generic to count as taste.</summary>
    public double TasteFacetMaxPrevalence { get; init; } = 0.25;

    /// <summary>Absolute carrier count below which a facet is never treated as generic regardless of share.</summary>
    public int TasteFacetPrevalenceFloor { get; init; } = 8;

    /// <summary>Max entries one franchise may hold on one shelf. Never relaxed.</summary>
    public int ShelfFranchiseCap { get; init; } = 1;

    /// <summary>
    /// Max entries sharing any single genre on one shelf (soft -- relaxation pass refills).
    /// Coupled to shelf size: no genre should take a majority. Rarely binds in practice.
    /// </summary>
    public int ShelfGenreCap { get; init; } = 3;

    /// <summary>Candidates short-listed per shelf as a multiple of shelf size, before diversity passes.</summary>
    public int ShelfOverfetchFactor { get; init; } = 3;

    /// <summary>Hard ceiling on ownerships probed for history across all shelf shortlists combined.</summary>
    public int ShelfProbeLimit { get; init; } = 150;

    // ── Feedback loop windows ───────────────────────────────────────────────
    // Read by FeedbackSets (which turns the stored feedback into a request's
    // id sets), not by the scorer — the engine itself only ever sees the sets.

    /// <summary>Days back the recently-surfaced set reaches, excluding today.</summary>
    public int SurfacedWindowDays { get; init; } = 3;

    /// <summary>Days after a surfacing within which a feed-launched session counts as an endorsement.</summary>
    public int EndorsementWindowDays { get; init; } = 3;

    // ── Tier detection and probing ──────────────────────────────────────────

    /// <summary>Sessions needed (with the span below) to call the library Established.</summary>
    public int Tier2MinSessions { get; init; } = 50;

    /// <summary>Weeks-in-days the sessions must span: "months in" made concrete as ~8 weeks.</summary>
    public double Tier2MinSpanDays { get; init; } = 56;

    /// <summary>Max shortlist candidates that get per-ownership history probed.</summary>
    public int HistoryProbeLimit { get; init; } = 60;

    /// <summary>Most-recently-played ownerships probed in addition to the tier sample, for tier detection.</summary>
    public int RecentProbeLimit { get; init; } = 25;

    /// <summary>
    /// Ownerships drawn UNIFORMLY from every row that could hold history, for
    /// the maturity-tier estimate. The tier is a claim about the LIBRARY, so it
    /// cannot be read off the candidate shortlist (which excludes exactly the
    /// games being played) or off the recently-played rows (which are the
    /// densest in sessions); both are biased, and in opposite directions.
    /// 120 is roughly a third of the measured library's history-bearing rows,
    /// enough that the scaling is not carried by a handful of rows, and it
    /// costs two indexed point reads apiece.
    /// </summary>
    public int TierSampleOwnerships { get; init; } = 120;

    /// <summary>Fixed salt for the tier sample's deterministic draw, so one library always samples the same rows.</summary>
    public int TierSampleSeed { get; init; } = 0x5715_0F5E;

    // ── Explanation ─────────────────────────────────────────────────────────

    /// <summary>
    /// Longest reason sentence a card may carry. One sentence is the contract,
    /// and 180 is the length at which one sentence stays one sentence: it fits
    /// the longest primary and secondary the selection rules can pair, quoted
    /// update title included, so the builder never has to drop a clause the
    /// honesty rules put there. Lower it and truncation starts deciding what
    /// the user is told.
    /// </summary>
    public int ReasonCharacterBudget { get; init; } = 180;

    /// <summary>The defaults above, shared. Records are immutable, so sharing is safe.</summary>
    public static RecommendationTuning Default { get; } = new();
}
