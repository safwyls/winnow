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

    /// <summary>
    /// The candidate sits entirely on the wrong side of the single-player /
    /// online line for this user (see <see cref="ModeMismatch"/>). Equal to
    /// the taste weight on purpose: a perfect genre match on a game the user
    /// will never launch with strangers should net to zero, not to a
    /// recommendation. A demotion rather than an exclusion because mode facets
    /// can be missing or miscoded — measured on the real library this fires on
    /// 12 never-opened rows (Team Fortress Classic, Deathmatch Classic, …),
    /// every one of them a wrong thing to surface to a 93%-single-player user.
    /// </summary>
    public double PenaltyModeMismatch { get; init; } = 0.10;

    // ── Mode-mismatch evidence floors ───────────────────────────────────────

    /// <summary>
    /// Committed mode-carrying games required before the profile may claim a
    /// dominant mode at all. 20: below it, a handful of purchases could fake a
    /// dominance; at 20+ games with an 85% share, chance is off the table.
    /// The measured library has 261.
    /// </summary>
    public int ModeEvidenceMinGames { get; init; } = 20;

    /// <summary>
    /// Share of committed mode-carrying games that must sit on one side before
    /// the other side counts as a mismatch. 0.85: past seventeen-in-twenty the
    /// minority mode is occasional experimentation, not a second taste the
    /// model should serve. The measured library is at 0.93 single-player.
    /// </summary>
    public double ModeDominanceShare { get; init; } = 0.85;

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

    // ── Shelves ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Taste-affinity floor for the <see cref="ShelfIds.OnYourTaste"/> shelf:
    /// how strongly a never-opened game must match the profile before "right
    /// up your alley" is an honest sentence. Affinity is normalised against
    /// the profile's peak DISTINCTIVE facet (see the prevalence cut), so 0.6
    /// means "carries a descriptor at least 60% as loved as your most-loved
    /// one". Measured on the real library this admits a rotating pool of
    /// ~200 of the 427 never-opened rows — months of daily rotation, not
    /// three favourites and not the whole pile.
    /// </summary>
    public double OnTasteMinAffinity { get; init; } = 0.6;

    /// <summary>
    /// Share of the facet-carrying library past which a descriptor stops
    /// counting as taste. 0.25: measured, "Action" sits on ~two-thirds of the
    /// real library's releases and saturated the affinity metric (266 of 427
    /// never-opened rows at a perfect score); at a quarter, the profile's
    /// peaks become the user's distinctive tastes (Survival, Sandbox,
    /// Crafting on the measured library) rather than the library's furniture.
    /// </summary>
    public double TasteFacetMaxPrevalence { get; init; } = 0.25;

    /// <summary>
    /// Absolute carrier count below which a facet is never treated as generic
    /// regardless of share — in a 20-game library, five carriers of one genre
    /// is a small collection, not genericity. 8: the prevalence cut only
    /// starts meaning something once a facet could have that many carriers.
    /// </summary>
    public int TasteFacetPrevalenceFloor { get; init; } = 8;

    /// <summary>
    /// Entries one franchise may hold on one shelf. 1, and deliberately never
    /// relaxed: the measured library has 14 unplayed "Infinity Blade" entries,
    /// and a shelf that is one franchise five times is a broken feed even when
    /// every individual score is right. The rest of the franchise rotates
    /// through later days.
    /// </summary>
    public int ShelfFranchiseCap { get; init; } = 1;

    /// <summary>
    /// Entries sharing any single genre on one shelf before the strict pass
    /// skips further ones.
    ///
    /// <para><b>3, and it moved with the shelf size rather than independently.</b>
    /// The property being defended is that no genre may take a MAJORITY of a
    /// shelf, and the number that expresses it depends on the shelf. At the old
    /// size of 10 that was 4 — below half. At 6 it is 3: half at most, never
    /// four of six. Left at 4 it would have allowed two-thirds of a shelf to
    /// share one genre, which is the outcome this constant exists to prevent,
    /// so the two caps are coupled and must move together.</para>
    ///
    /// <para>Soft, not hard: a pool that genuinely IS six survival games should
    /// still fill its shelf, and the relaxation pass refills. The franchise cap
    /// above is the hard one, because fourteen Infinity Blades is never an
    /// honest shelf and six survival games can be.</para>
    ///
    /// <para><b>Measured: on realistic data this cap rarely binds, and the strict
    /// pass is NOT pinned by a test.</b> An attempt to pin it — twelve sampled
    /// games, six of each genre, variety fully available — came back three and
    /// three with the cap set to 3, to 4, and disabled entirely at 99. Candidates
    /// that sit close together on every scored dimension are separated by the
    /// day-seeded jitter, which interleaves genres on its own, so the cap has
    /// nothing to trim. Forcing it to bite means making one genre dominate the
    /// score order, and the prevalence cut fights that directly: seeding enough
    /// committed play to bias the taste profile toward a genre pushes that genre
    /// past the 25% prevalence threshold, which cuts it from the profile.</para>
    ///
    /// <para>So this is a <b>safety net for skewed libraries</b>, not a mechanism
    /// the ordinary path leans on — and the honest statement is that a wrong
    /// value here would not fail the suite. Recorded rather than papered over
    /// with a test that passes whatever the number says.</para>
    /// </summary>
    public int ShelfGenreCap { get; init; } = 3;

    /// <summary>
    /// Candidates short-listed per shelf, as a multiple of the shelf size,
    /// before history probing and the diversity passes. 3×: enough slack for
    /// the caps and cross-shelf claims to bite without probing the library.
    /// </summary>
    public int ShelfOverfetchFactor { get; init; } = 3;

    /// <summary>
    /// Hard ceiling on ownerships probed for history across all shelf
    /// shortlists combined — the shelf-feed analogue of
    /// <see cref="HistoryProbeLimit"/>, kept separate because five shelves
    /// legitimately probe more than one list does.
    /// </summary>
    public int ShelfProbeLimit { get; init; } = 150;

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
