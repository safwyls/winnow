namespace Hoard.Recommend;

/// <summary>
/// Every weight and threshold the scoring model uses, named and defaulted.
/// Deliberately a parameter object, never schema: like §6.1's
/// <c>BucketThresholds</c>, retuning any of these changes the very next feed
/// and touches no stored data.
///
/// <para>The justifications live in docs/recommendation-engine.md §5, argued
/// against the measured library (2026-08-26 snapshot) rather than against
/// round numbers. The short form is repeated on each member so the code can be
/// read alone.</para>
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

    /// <summary>
    /// The candidate carries a descriptor the user's hours concentrate in.
    /// Deliberately small: genre similarity is the commodity incumbents already
    /// ship — here it is only the tiebreaker inside the 700-row shelfware pile,
    /// which nothing else can order at Tier 0.
    /// </summary>
    public double WeightTasteAffinity { get; init; } = 0.10;

    /// <summary>Tier 1: distinct return episodes. 40 minutes across six sessions is someone trying to like it.</summary>
    public double WeightTriedToLikeIt { get; init; } = 0.10;

    /// <summary>On disk right now — zero friction to launch.</summary>
    public double WeightInstalled { get; init; } = 0.05;

    /// <summary>Same work owned on two stores: a purchase made twice. Fires only after cross-store merges are confirmed.</summary>
    public double WeightBoughtTwice { get; init; } = 0.05;

    // ── Penalties ───────────────────────────────────────────────────────────

    /// <summary>
    /// Played within the fresh window: not forgotten, not this feed's
    /// business. Sized to dominate — the maximum realistic positive sum for a
    /// non-stale row is ≈0.55, so nothing played yesterday can crack the top.
    /// </summary>
    public double PenaltyRecentlyPlayed { get; init; } = 0.60;

    /// <summary>
    /// Fair shake + deep dormancy + nothing changed since: "you were probably
    /// right to move on", said out loud instead of nagging. Drops the row to
    /// the back of the feed without erasing it.
    /// </summary>
    public double PenaltyProbablyDone { get; init; } = 0.30;

    /// <summary>
    /// The caller told us the feed showed this recently — rotate it behind its
    /// unshown near-peers. Small on purpose: if the top item is genuinely the
    /// top item, repeating it once or twice is honest.
    /// </summary>
    public double PenaltyRecentlySurfaced { get; init; } = 0.20;

    // ── Time thresholds ─────────────────────────────────────────────────────

    /// <summary>
    /// Days within which a game counts as "playing it now". 14 because that is
    /// Steam's own current-activity window (`playtime_2weeks`) — the one
    /// non-arbitrary recency number available, the same way the 120-minute
    /// refund line anchors the buckets.
    /// </summary>
    public double FreshPlayWindowDays { get; init; } = 14;

    /// <summary>
    /// Years of dormancy at which the ramp saturates. 2.0: p25 of the measured
    /// distribution is 2.4 years — the ramp deliberately treats the older
    /// three-quarters of the library as equally, fully dormant rather than
    /// pretending 5-vs-9-years is a meaningful ordering.
    /// </summary>
    public double DormancySaturationYears { get; init; } = 2.0;

    /// <summary>
    /// Dormancy gate for the probably-done penalty. Past ~4 years the person
    /// who bounced is, in taste terms, a different player; the median bounced
    /// game (5.7y dormant) sits beyond this on purpose — the FairShakeMinutes
    /// gate is what keeps the penalty narrow.
    /// </summary>
    public double DeepDormancyYears { get; init; } = 4.0;

    // ── Playtime thresholds ─────────────────────────────────────────────────
    // The refund line and retired floor are NOT here: they arrive on the
    // request's BucketThresholds, because they are §6.1's numbers and two
    // copies would drift.

    /// <summary>
    /// Minutes past which "abandoned" usually means "finished with it".
    /// 2,000 ≈ 33 hours ≈ the published aggregate main-story-plus-extras
    /// completion time for story-driven games. Explicitly provisional: §6.1's
    /// HowLongToBeat [VERIFY] item is the real answer, and this parameter is
    /// where per-game numbers would plug in.
    /// </summary>
    public double FairShakeMinutes { get; init; } = 2_000;

    // ── Curve shape ─────────────────────────────────────────────────────────

    /// <summary>
    /// Commitment value as playtime approaches the retired floor: near-retired
    /// is near-finished, not forgotten — but stays above zero because a
    /// 90-hour game someone left IS more interesting than one never opened.
    /// </summary>
    public double CommitmentFloorValue { get; init; } = 0.15;

    /// <summary>
    /// Never-opened base value. Each shelfware row alone is weak evidence of
    /// intent (the measured pile is 412 zero-and-dateless rows); the base
    /// keeps the pile ranked without letting it outrank anyone with a history.
    /// </summary>
    public double ShelfwareBaseValue { get; init; } = 0.35;

    /// <summary>
    /// Sampled (1..refund-line minutes) ramps from this… — launching at all
    /// shows intent shelfware lacks.
    /// </summary>
    public double SampledBaseValue { get; init; } = 0.50;

    /// <summary>
    /// …across this span, topping out at 0.70 — strictly below the bounced
    /// peak, because §6.1 says sub-refund minutes are still "never played it",
    /// and the jump AT the line is the line's meaning: crossing it is a
    /// different fact, not more of the same one.
    /// </summary>
    public double SampledSpanValue { get; init; } = 0.20;

    /// <summary>
    /// Return episodes at which tried-to-like-it saturates. Coming back twice
    /// after the first taste is already trying; demanding more would gate the
    /// signal on history depth a young library cannot have.
    /// </summary>
    public int TriedToLikeSaturationEpisodes { get; init; } = 3;

    /// <summary>
    /// Jitter's maximum. Below the smallest deliberate weight gap (0.05), so
    /// the daily shuffle can only reorder rows no real signal separates.
    /// </summary>
    public double JitterAmplitude { get; init; } = 0.03;

    // ── Tier detection and probing ──────────────────────────────────────────

    /// <summary>Sessions needed (with the span below) to call the library Established.</summary>
    public int Tier2MinSessions { get; init; } = 50;

    /// <summary>Weeks-in-days the sessions must span: "months in" made concrete as ~8 weeks.</summary>
    public double Tier2MinSpanDays { get; init; } = 56;

    /// <summary>
    /// How many shortlist candidates get their per-ownership history read.
    /// The Core interfaces read snapshots/sessions one ownership at a time, so
    /// the engine probes the plausible top of the feed (3× a 20-item feed)
    /// instead of issuing two thousand queries per refresh.
    /// </summary>
    public int HistoryProbeLimit { get; init; } = 60;

    /// <summary>
    /// How many most-recently-played ownerships get probed IN ADDITION, purely
    /// for tier detection: history physically accrues on the games being
    /// played, which are exactly the rows the feed ranks lowest — probing only
    /// the shortlist would systematically miss the evidence.
    /// </summary>
    public int RecentProbeLimit { get; init; } = 25;

    /// <summary>The defaults above, shared. Records are immutable, so sharing is safe.</summary>
    public static RecommendationTuning Default { get; } = new();
}
