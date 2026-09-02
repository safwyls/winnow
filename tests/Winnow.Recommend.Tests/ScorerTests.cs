using Winnow.Core.Queries;
using Xunit;

namespace Winnow.Recommend.Tests;

/// <summary>
/// The pure model: every curve and penalty in
/// <see cref="RecommendationScorer"/>, no database. Thresholds are §6.1
/// defaults (refund line 120, retired floor 6,000); tuning is the documented
/// defaults, because these tests ARE the defaults' executable statement.
/// </summary>
public class ScorerTests
{
    private static readonly BucketThresholds Thresholds = BucketThresholds.Default;
    private static readonly RecommendationTuning Tuning = RecommendationTuning.Default;
    private static readonly DateTime AsOf = RecommendHarness.AsOf;

    private static CandidateFacts Facts(
        string bucket,
        long minutes,
        DateTime? lastPlayed,
        long releaseId = 1,
        bool installed = false,
        int storeCount = 1,
        bool recentlySurfaced = false,
        int? returnEpisodes = null,
        double? tasteAffinity = null,
        UpdateCoverage updateCoverage = UpdateCoverage.Observed)
        => new()
        {
            OwnershipId = releaseId,
            ReleaseId = releaseId,
            WorkId = releaseId,
            Title = "Fixture",
            Store = "steam",
            Bucket = bucket,
            PlaytimeMinutes = minutes,
            LastPlayedAt = lastPlayed,
            Installed = installed,
            StoreCount = storeCount,
            RecentlySurfaced = recentlySurfaced,
            ReturnEpisodes = returnEpisodes,
            TasteAffinity = tasteAffinity,
            TasteFacetName = tasteAffinity is null ? null : "Survival",
            UpdateCoverage = updateCoverage,
        };

    private static IReadOnlyList<SignalContribution> Score(CandidateFacts facts, int seed = 1)
        => RecommendationScorer.Score(facts, Thresholds, Tuning, AsOf, seed);

    private static double Value(IReadOnlyList<SignalContribution> signals, string name)
        => signals.SingleOrDefault(s => s.Signal == name)?.Value ?? 0;

    private static SignalContribution? Find(IReadOnlyList<SignalContribution> signals, string name)
        => signals.SingleOrDefault(s => s.Signal == name);

    // ── Arithmetic contract ────────────────────────────────────────────────

    [Fact]
    public void Total_is_exactly_the_sum_of_contributions_and_each_is_weight_times_value()
    {
        var signals = Score(Facts(
            LibraryBuckets.StaleButPatched, 40, AsOf.AddYears(-3),
            installed: true, storeCount: 2, tasteAffinity: 0.8, returnEpisodes: 3));

        Assert.NotEmpty(signals);
        foreach (var s in signals)
        {
            Assert.Equal(s.Weight * s.Value, s.Contribution, precision: 12);
            Assert.False(string.IsNullOrWhiteSpace(s.Explanation));
        }

        Assert.Equal(signals.Sum(s => s.Contribution), RecommendationScorer.Total(signals), precision: 12);
    }

    // ── Commitment curve ───────────────────────────────────────────────────

    [Fact]
    public void Never_opened_scores_the_shelfware_base()
    {
        var signals = Score(Facts(LibraryBuckets.NeverPlayed, 0, null));
        Assert.Equal(Tuning.ShelfwareBaseValue, Value(signals, SignalNames.Commitment), precision: 12);
    }

    [Fact]
    public void Sampled_minutes_ramp_between_base_and_base_plus_span_and_stay_below_the_bounced_peak()
    {
        var low = Value(Score(Facts(LibraryBuckets.NeverPlayed, 10, AsOf.AddYears(-3))), SignalNames.Commitment);
        var high = Value(Score(Facts(LibraryBuckets.NeverPlayed, 119, AsOf.AddYears(-3))), SignalNames.Commitment);

        Assert.InRange(low, Tuning.SampledBaseValue, Tuning.SampledBaseValue + Tuning.SampledSpanValue);
        Assert.True(high > low, "more sampled minutes must mean more commitment");
        Assert.True(high < 1.0, "the sampled ramp must stay strictly below the bounced peak");
    }

    [Fact]
    public void Crossing_the_refund_line_is_a_jump_not_a_ramp()
    {
        // §6.1's boundary carried into the curve: 119 minutes and 120 minutes
        // are different FACTS, and the discontinuity is deliberate.
        var justUnder = Value(Score(Facts(LibraryBuckets.NeverPlayed, 119, AsOf.AddYears(-3))), SignalNames.Commitment);
        var atLine = Value(Score(Facts(LibraryBuckets.Bounced, 120, AsOf.AddYears(-3))), SignalNames.Commitment);

        Assert.Equal(1.0, atLine, precision: 12);
        Assert.True(atLine - justUnder > 0.25, "the refund line must be a visible jump");
    }

    [Fact]
    public void Bounced_commitment_decays_toward_the_floor_as_playtime_approaches_retired()
    {
        var fresh = Value(Score(Facts(LibraryBuckets.Bounced, 150, AsOf.AddYears(-3))), SignalNames.Commitment);
        var deep = Value(Score(Facts(LibraryBuckets.Bounced, 5_900, AsOf.AddYears(-3))), SignalNames.Commitment);

        Assert.True(fresh > deep, "a near-retired game is near-finished, not forgotten");
        Assert.True(deep >= Tuning.CommitmentFloorValue - 1e-9);
        Assert.True(deep < Tuning.ShelfwareBaseValue,
            "near the retired floor commitment falls below even shelfware — dropping a 98-hour game is closure");
    }

    // ── Dormancy ───────────────────────────────────────────────────────────

    [Fact]
    public void Dormancy_saturates_and_never_decays()
    {
        var twoYears = Value(Score(Facts(LibraryBuckets.Bounced, 300, AsOf.AddYears(-2))), SignalNames.Dormancy);
        var nineYears = Value(Score(Facts(LibraryBuckets.Bounced, 300, AsOf.AddYears(-9))), SignalNames.Dormancy);

        // The measured library's median dormancy is 6.9 years; decay past
        // saturation would suppress the older HALF of the pile the app exists
        // to surface.
        Assert.Equal(1.0, twoYears, precision: 2);
        Assert.Equal(1.0, nineYears, precision: 12);
    }

    [Fact]
    public void Unknown_last_played_beside_real_minutes_reads_as_maximally_dormant()
    {
        // Steam's pre-timestamp sentinel (migration 0008): played, unknown
        // when, certainly ancient. Treating it as fresh would structurally
        // exclude the oldest pile in the library.
        var signals = Score(Facts(LibraryBuckets.Bounced, 300, lastPlayed: null));
        Assert.Equal(1.0, Value(signals, SignalNames.Dormancy), precision: 12);
    }

    [Fact]
    public void Never_opened_has_no_time_evidence_so_no_dormancy_signal_either_way()
    {
        var signals = Score(Facts(LibraryBuckets.NeverPlayed, 0, null));
        Assert.Null(Find(signals, SignalNames.Dormancy));
        Assert.Null(Find(signals, SignalNames.RecentlyPlayed));
    }

    // ── Penalties ──────────────────────────────────────────────────────────

    [Fact]
    public void A_game_played_this_week_is_sunk_below_everything_realistic()
    {
        var fresh = Score(Facts(LibraryBuckets.Bounced, 300, AsOf.AddDays(-3),
            installed: true, storeCount: 2, tasteAffinity: 1.0));
        var dormant = Score(Facts(LibraryBuckets.NeverPlayed, 0, null, releaseId: 2));

        Assert.NotNull(Find(fresh, SignalNames.RecentlyPlayed));
        Assert.True(RecommendationScorer.Total(fresh) < RecommendationScorer.Total(dormant),
            "a game played three days ago must rank below plain shelfware, whatever else is true of it");
    }

    [Fact]
    public void Probably_done_fires_only_on_fair_shake_plus_deep_dormancy_and_says_so()
    {
        var done = Score(Facts(LibraryBuckets.Bounced, 2_500, AsOf.AddYears(-8)));
        var contribution = Find(done, SignalNames.ProbablyDone);

        Assert.NotNull(contribution);
        Assert.True(contribution.Contribution < 0);
        Assert.Contains("right to move on", contribution.Explanation);

        // A modest bounce at the same age is the feed's bread and butter, not
        // a closed case.
        Assert.Null(Find(Score(Facts(LibraryBuckets.Bounced, 300, AsOf.AddYears(-8))), SignalNames.ProbablyDone));
        // Recent fair shakes are not "done" — four years is the gate.
        Assert.Null(Find(Score(Facts(LibraryBuckets.Bounced, 2_500, AsOf.AddYears(-2))), SignalNames.ProbablyDone));
        // And a patched game is by definition not "nothing has changed since".
        Assert.Null(Find(Score(Facts(LibraryBuckets.StaleButPatched, 2_500, AsOf.AddYears(-8))), SignalNames.ProbablyDone));
    }

    [Fact]
    public void Probably_done_needs_proven_update_coverage_and_never_claims_silence_without_it()
    {
        // F15. Identical rows; the only difference is whether Winnow has ever
        // read this release's update history. Coverage begins when polling
        // begins, so an empty update history is absence of evidence — not
        // proof that nothing shipped — and a penalty resting on that claim
        // must not fire.
        var shape = Facts(LibraryBuckets.Bounced, 2_500, AsOf.AddYears(-8));
        Assert.True(RecommendationScorer.HasProbablyDoneShape(shape, Tuning, AsOf));

        var unwatched = Score(shape with { UpdateCoverage = UpdateCoverage.Unknown });
        Assert.Null(Find(unwatched, SignalNames.ProbablyDone));

        var watched = Score(shape with { UpdateCoverage = UpdateCoverage.Observed });
        Assert.NotNull(Find(watched, SignalNames.ProbablyDone));

        // The row is not silently demoted either: without coverage it simply
        // scores higher, and no sentence anywhere asserts nothing changed.
        Assert.True(RecommendationScorer.Total(unwatched) > RecommendationScorer.Total(watched));
        foreach (var contribution in unwatched)
        {
            Assert.DoesNotContain("nothing", contribution.Explanation, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ── The headline ───────────────────────────────────────────────────────

    [Fact]
    public void Stale_but_patched_outranks_an_otherwise_identical_bounced_game()
    {
        var patched = Score(Facts(LibraryBuckets.StaleButPatched, 300, AsOf.AddYears(-3)));
        var quiet = Score(Facts(LibraryBuckets.Bounced, 300, AsOf.AddYears(-3)));

        Assert.Equal(1.0, Value(patched, SignalNames.PatchAfterDormancy), precision: 12);
        Assert.True(
            RecommendationScorer.Total(patched) - RecommendationScorer.Total(quiet)
                >= Tuning.WeightPatchAfterDormancy - Tuning.JitterAmplitude,
            "the patch signal must dominate jitter by construction");
    }

    // ── Tier 1 bonus ───────────────────────────────────────────────────────

    [Fact]
    public void Return_episodes_are_a_bonus_that_saturates_and_a_single_taste_earns_nothing()
    {
        Assert.Null(Find(Score(Facts(LibraryBuckets.Bounced, 200, AsOf.AddYears(-1), returnEpisodes: 1)),
            SignalNames.TriedToLikeIt));

        var two = Value(Score(Facts(LibraryBuckets.Bounced, 200, AsOf.AddYears(-1), returnEpisodes: 2)),
            SignalNames.TriedToLikeIt);
        var many = Value(Score(Facts(LibraryBuckets.Bounced, 200, AsOf.AddYears(-1), returnEpisodes: 12)),
            SignalNames.TriedToLikeIt);

        Assert.True(two is > 0 and < 1);
        Assert.Equal(1.0, many, precision: 12);
    }

    // ── Jitter ─────────────────────────────────────────────────────────────

    [Fact]
    public void Jitter_is_deterministic_bounded_and_seed_sensitive()
    {
        var a = RecommendationScorer.JitterValue(7, 42);
        Assert.Equal(a, RecommendationScorer.JitterValue(7, 42), precision: 15);
        Assert.InRange(a, 0.0, 1.0);
        Assert.NotEqual(a, RecommendationScorer.JitterValue(8, 42));
        Assert.NotEqual(a, RecommendationScorer.JitterValue(7, 43));

        var contribution = Find(Score(Facts(LibraryBuckets.NeverPlayed, 0, null)), SignalNames.Jitter);
        Assert.NotNull(contribution);
        Assert.InRange(contribution.Contribution, 0.0, Tuning.JitterAmplitude);
    }

    // ── Duration phrasing (TASK-73) ──────────────────────────────────────────

    [Fact]
    public void Duration_renders_one_minute_as_singular_not_a_bare_count_of_one()
    {
        Assert.Equal("a minute", Phrases.Duration(1));
    }

    [Fact]
    public void Duration_renders_every_other_minute_count_under_the_hours_branch_as_plural()
    {
        Assert.Equal("2 minutes", Phrases.Duration(2));
        Assert.Equal("119 minutes", Phrases.Duration(119));
    }
}
