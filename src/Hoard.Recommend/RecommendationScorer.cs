using System.Globalization;
using Hoard.Core.Queries;

namespace Hoard.Recommend;

/// <summary>
/// The model itself: pure functions from <see cref="CandidateFacts"/> to
/// signal contributions. No IO, no clock, no randomness that is not seeded —
/// every curve here is testable to the decimal, and public on purpose so the
/// model can be argued with (docs/recommendation-engine.md is the argument).
///
/// <para><b>Missing evidence contributes zero and is absent from the
/// breakdown.</b> Nothing is renormalised: a cold-start library produces
/// lower absolute scores, which is true, and hiding it by scaling up the
/// signals that remain would be the model overclaiming.</para>
/// </summary>
public static class RecommendationScorer
{
    /// <summary>
    /// Scores one candidate. Returns every signal that moved the score,
    /// penalties included; the total is <see cref="Total"/> over the result.
    /// </summary>
    public static IReadOnlyList<SignalContribution> Score(
        CandidateFacts facts,
        BucketThresholds thresholds,
        RecommendationTuning tuning,
        DateTime asOfUtc,
        int shuffleSeed)
    {
        var contributions = new List<SignalContribution>(8);

        void Add(string signal, double weight, double value, string explanation)
        {
            if (value <= 0 || weight == 0)
            {
                return;
            }

            contributions.Add(new SignalContribution(
                signal, weight, value, weight * value, explanation));
        }

        // ── Patch after dormancy — the headline ────────────────────────────
        // The scoring input is BUCKET MEMBERSHIP, computed by the §6.1 query
        // with the correlated build-push + announcement heuristic. Recounting
        // updates here would be a second definition of "major update" that
        // could disagree with the badge the user is looking at.
        if (facts.Bucket == LibraryBuckets.StaleButPatched)
        {
            Add(SignalNames.PatchAfterDormancy, tuning.WeightPatchAfterDormancy, 1.0,
                facts.LatestUpdateTitle is { Length: > 0 } updateTitle
                    ? $"A major update (\"{updateTitle}\") landed after you last played."
                    : "A major update landed after you last played.");
        }

        // ── Commitment shape ───────────────────────────────────────────────
        var (commitment, commitmentWhy) = CommitmentValue(facts, thresholds, tuning);
        Add(SignalNames.Commitment, tuning.WeightCommitment, commitment, commitmentWhy);

        // ── Dormancy ───────────────────────────────────────────────────────
        var dormancyDays = DormancyDays(facts, asOfUtc);
        if (dormancyDays is { } days)
        {
            if (days >= tuning.FreshPlayWindowDays)
            {
                var span = tuning.DormancySaturationYears * 365.25 - tuning.FreshPlayWindowDays;
                var value = span <= 0 ? 1.0 : Math.Clamp((days - tuning.FreshPlayWindowDays) / span, 0.0, 1.0);
                Add(SignalNames.Dormancy, tuning.WeightDormancy, value,
                    $"Untouched for {Phrases.Age(days)}.");
            }
        }
        else if (facts.LastPlayedAt is null && facts.PlaytimeMinutes > 0)
        {
            // Steam's pre-timestamp sentinel, mapped to NULL upstream
            // (migration 0008): played, unknown when, certainly ancient.
            // "Unknown" must read as maximally dormant, not as fresh — the
            // same call the bucket query makes for the same rows.
            Add(SignalNames.Dormancy, tuning.WeightDormancy, 1.0,
                "Last played before Steam kept dates — as dormant as it gets.");
        }

        // ── Taste affinity (tiebreaker) ────────────────────────────────────
        if (facts.TasteAffinity is { } affinity && affinity > 0)
        {
            Add(SignalNames.TasteAffinity, tuning.WeightTasteAffinity, Math.Clamp(affinity, 0.0, 1.0),
                facts.TasteFacetName is { Length: > 0 } facetName
                    ? $"{facetName} is where your hours go, and this is one."
                    : "It matches where your hours go.");
        }

        // ── Tried to like it (Tier 1) ──────────────────────────────────────
        // Episodes beyond the first: one taste is the sampled/bounced fact the
        // commitment curve already scored; coming BACK is the new information.
        if (facts.ReturnEpisodes is { } episodes && episodes >= 2)
        {
            var saturation = Math.Max(2, tuning.TriedToLikeSaturationEpisodes);
            var value = Math.Clamp((episodes - 1) / (double)(saturation - 1), 0.0, 1.0);
            Add(SignalNames.TriedToLikeIt, tuning.WeightTriedToLikeIt, value,
                $"You've gone back to it {episodes} separate times — that is someone trying to like a game.");
        }

        // ── Friction and intent ────────────────────────────────────────────
        if (facts.Installed)
        {
            Add(SignalNames.Installed, tuning.WeightInstalled, 1.0,
                "It's installed and ready to launch.");
        }

        if (facts.StoreCount >= 2)
        {
            Add(SignalNames.BoughtTwice, tuning.WeightBoughtTwice, 1.0,
                $"You bought it on {facts.StoreCount} different stores.");
        }

        // ── Penalties ──────────────────────────────────────────────────────
        if (dormancyDays is { } d && d < tuning.FreshPlayWindowDays)
        {
            Add(SignalNames.RecentlyPlayed, -tuning.PenaltyRecentlyPlayed, 1.0,
                $"You played this {Phrases.Age(d)} ago — it isn't forgotten.");
        }

        // "You were right to drop this": a fair shake of hours, deeply
        // dormant, and — because §6.1 precedence would have put the row in
        // stale_but_patched otherwise — nothing major has changed since.
        if (facts.Bucket == LibraryBuckets.Bounced
            && facts.PlaytimeMinutes >= tuning.FairShakeMinutes
            && dormancyDays is { } dd
            && dd >= tuning.DeepDormancyYears * 365.25)
        {
            Add(SignalNames.ProbablyDone, -tuning.PenaltyProbablyDone, 1.0,
                $"You gave it {Phrases.Duration(facts.PlaytimeMinutes)} and walked away " +
                $"{Phrases.Age(dd)} ago, and nothing major has changed since — " +
                "you were probably right to move on.");
        }

        // Mode mismatch: the library's hours sit overwhelmingly on one side of
        // the single-player/online line and this candidate lives entirely on
        // the other. Sized to at least cancel a perfect taste match — a genre
        // hit on a game the user will never actually launch with strangers is
        // a false positive — but not to bury the row: mode facets can be
        // missing or wrong, and a demotion is recoverable where an exclusion
        // is not.
        if (facts.ModeMismatch != ModeMismatch.None)
        {
            Add(SignalNames.ModeMismatch, -tuning.PenaltyModeMismatch, 1.0,
                facts.ModeMismatch == ModeMismatch.OnlineOnlyForSoloPlayer
                    ? "It's online multiplayer only, and nearly everything you actually play is single-player."
                    : "It's single-player only, and nearly everything you actually play is online.");
        }

        if (facts.RecentlySurfaced)
        {
            Add(SignalNames.RecentlySurfaced, -tuning.PenaltyRecentlySurfaced, 1.0,
                "Shown recently — rotated back so the feed isn't the same five games.");
        }

        // ── Jitter ─────────────────────────────────────────────────────────
        // Deterministic per (seed, release): the same request re-run is the
        // same feed, tomorrow's is a different hand — and the amplitude sits
        // below every deliberate weight gap, so only near-ties can swap.
        if (tuning.JitterAmplitude > 0)
        {
            Add(SignalNames.Jitter, tuning.JitterAmplitude, JitterValue(shuffleSeed, facts.ReleaseId),
                "Daily shuffle, so near-ties rotate instead of repeating.");
        }

        return contributions;
    }

    /// <summary>The score is exactly the sum of its parts — by construction, nothing else.</summary>
    public static double Total(IReadOnlyList<SignalContribution> contributions)
    {
        var total = 0.0;
        foreach (var c in contributions)
        {
            total += c.Contribution;
        }

        return total;
    }

    /// <summary>
    /// Days since last played, or null when there is no date. Callers must
    /// pair null with the minutes to tell "never opened" from the ancient-play
    /// sentinel — see <see cref="CandidateFacts.LastPlayedAt"/>.
    /// </summary>
    private static double? DormancyDays(CandidateFacts facts, DateTime asOfUtc)
        => facts.LastPlayedAt is { } lastPlayed
            ? Math.Max(0.0, (asOfUtc - lastPlayed).TotalDays)
            : null;

    /// <summary>
    /// The commitment curve — where the minutes sit against §6.1's refund
    /// line. Piecewise, with a deliberate DISCONTINUITY at the line: 0.70 on
    /// one side, 1.00 on the other, because crossing it is a different fact
    /// about the user (committed past the point of no return, then gave up —
    /// the highest-value pile), not more of the same fact. A smooth curve here
    /// would quietly repeal the §6.1 boundary this module inherits.
    /// </summary>
    private static (double Value, string Why) CommitmentValue(
        CandidateFacts facts, BucketThresholds thresholds, RecommendationTuning tuning)
    {
        var minutes = facts.PlaytimeMinutes;
        var refund = Math.Max(1, thresholds.BouncedFloorMinutes);
        var retired = Math.Max(refund + 1, thresholds.RetiredFloorMinutes);

        if (minutes <= 0)
        {
            // Zero beside a real date is the 'active' residue: launched, but
            // no source measured the minutes. Same base value as shelfware —
            // unknown minutes claim nothing — but the sentence must not lie
            // by saying "never opened" about a game with a play date.
            return facts.LastPlayedAt is null
                ? (tuning.ShelfwareBaseValue, "Never opened since it joined your library.")
                : (tuning.ShelfwareBaseValue, "Launched at least once, but no store measured the minutes.");
        }

        if (minutes < refund)
        {
            // Sampled: they showed intent shelfware lacks, but §6.1 says these
            // minutes are still "never played it", so the ramp tops out below
            // the bounced peak.
            var value = tuning.SampledBaseValue + tuning.SampledSpanValue * (minutes / (double)refund);
            return (value, $"You tried it for {Phrases.Duration(minutes)} and never went back.");
        }

        // Bounced: peak at the refund line, decaying toward the retired floor
        // — a game at 90 of 100 hours is nearly finished, not forgotten.
        var progress = Math.Clamp((minutes - refund) / (double)(retired - refund), 0.0, 1.0);
        var bounced = 1.0 - (1.0 - tuning.CommitmentFloorValue) * progress;
        return (bounced, $"You put {Phrases.Duration(minutes)} in — past the refund line — then let it go.");
    }

    /// <summary>
    /// SplitMix64 over (seed, releaseId), folded to [0, 1). Explicit integer
    /// mixing rather than <c>GetHashCode</c>/<c>Random</c> because the jitter
    /// must be identical across runs, processes and platforms — "the feed I
    /// saw this morning" has to be reconstructible from its inputs.
    /// </summary>
    public static double JitterValue(int seed, long releaseId)
    {
        unchecked
        {
            var x = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL ^ (ulong)releaseId + 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            x ^= x >> 31;
            return (x >> 11) * (1.0 / (1UL << 53));
        }
    }
}

/// <summary>
/// The two number-to-prose helpers every explanation shares. Invariant
/// culture: reason strings are data the UI renders, and a Turkish locale must
/// produce the same feed as any other.
/// </summary>
internal static class Phrases
{
    /// <summary>"40 minutes", "5.2 hours", "33 hours".</summary>
    public static string Duration(long minutes)
    {
        if (minutes < 120)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{minutes} minutes");
        }

        var hours = minutes / 60.0;
        return hours < 10
            ? string.Create(CultureInfo.InvariantCulture, $"{hours:0.#} hours")
            : string.Create(CultureInfo.InvariantCulture, $"{Math.Round(hours)} hours");
    }

    /// <summary>"3 days", "a month", "8 months", "a year", "6 years".</summary>
    public static string Age(double days)
    {
        if (days >= 365.25 * 2)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)(days / 365.25)} years");
        }

        if (days >= 365.25)
        {
            return "a year";
        }

        if (days >= 60)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)(days / 30.44)} months");
        }

        if (days >= 30)
        {
            return "a month";
        }

        var wholeDays = Math.Max(1, (int)days);
        return wholeDays == 1 ? "a day" : string.Create(CultureInfo.InvariantCulture, $"{wholeDays} days");
    }
}
