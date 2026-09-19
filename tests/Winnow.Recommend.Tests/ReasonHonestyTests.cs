using System.Text.RegularExpressions;
using Xunit;

namespace Winnow.Recommend.Tests;

/// <summary>
/// The honesty rule, made mechanical. A card may only claim what the engine
/// can prove about that game. The variant is chosen per card by hashing the
/// release id, with no knowledge of what any other card in the feed said,
/// so any phrasing asserting a rank, a maximum, a uniqueness or a share of
/// the whole library can render on two adjacent cards at once. That is what
/// shipped: two cards both called a genre the user's deepest pile, which is
/// a contradiction, and it read worse than the cookie-cutter copy it
/// replaced.
/// </summary>
public class ReasonHonestyTests : IDisposable
{
    private static readonly RecommendationTuning Tuning = RecommendationTuning.Default;
    private static readonly DateTime AsOf = RecommendHarness.AsOf;

    /// <summary>
    /// Five patterns, one per shape of library-wide assertion no card may
    /// make because nothing in Winnow.Recommend computes it. Checked against
    /// every variant in the phrasebook and against every rendered card in
    /// the feed-level test. All four strings that shipped with the fault
    /// trip the list; "one of your deepest piles" deliberately does not,
    /// because a comparative that holds for any qualifying game is the fix
    /// rather than the defect.
    /// </summary>
    private static readonly (string Name, Regex Pattern)[] Superlatives =
    [
        // "you have more hours in Survival than in anything else"
        ("comparison against the whole library",
            new Regex(@"\bthan (in )?(anything|everything|any other|anyone|all of)\b",
                RegexOptions.IgnoreCase)),

        // "which is your deepest pile" — but "one of your deepest piles" is a
        // comparative that holds for any qualifying game, so it stays legal.
        ("a bare rank",
            new Regex(@"(?<!one of )\b(your|the)\s+(deepest|biggest|largest|longest|oldest"
                + @"|newest|smallest|shortest|best|worst|most)\b",
                RegexOptions.IgnoreCase)),

        // "Survival is where most of your hours already live" — a majority
        // share the profile never measures.
        ("a quantified share of the library",
            new Regex(@"\bmost of (your|what)\b", RegexOptions.IgnoreCase)),

        ("an exclusivity claim",
            new Regex(@"\bnothing else\b|\bmore than any\b|\bthe (only|single) (game|thing|one)\b",
                RegexOptions.IgnoreCase)),

        // "which is unusual" — rarity is a count of the rest of the library,
        // and nothing counts it.
        ("a rarity claim",
            new Regex(@"\b(unusual|unusually|rare|rarest|unique|uniquely)\b", RegexOptions.IgnoreCase)),
    ];

    private readonly RecommendHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    // ── The phrasebook itself ──────────────────────────────────────────────

    [Fact]
    public void Ownership_only_reasons_do_not_infer_payment_or_a_bundle()
    {
        var unsupported = new Regex(@"\b(bought|paid|purchase|bundle|discount|spent)\b", RegexOptions.IgnoreCase);
        foreach (var signal in new[] { ReasonSignal.NeverOpened, ReasonSignal.BoughtTwice })
            foreach (var variant in ReasonPhrasebook.Variants(signal))
                Assert.False(unsupported.IsMatch(variant), variant);

        var facts = new CandidateFacts
        {
            OwnershipId = 1, ReleaseId = 1, WorkId = 1, Title = "Free or unknown acquisition",
            Store = "steam", Bucket = Winnow.Core.Queries.LibraryBuckets.NeverPlayed,
            PlaytimeMinutes = 0, StoreCount = 2,
        };
        var contribution = Assert.Single(RecommendationScorer.Score(facts,
            Winnow.Core.Queries.BucketThresholds.Default, Tuning, AsOf, 1),
            item => item.Signal == SignalNames.BoughtTwice);
        Assert.False(unsupported.IsMatch(contribution.Explanation), contribution.Explanation);
        Assert.Equal(Tuning.WeightBoughtTwice, contribution.Contribution);
    }

    [Fact]
    public void No_variant_anywhere_in_the_phrasebook_claims_a_rank_it_cannot_prove()
    {
        var offences = new List<string>();

        foreach (var signal in Enum.GetValues<ReasonSignal>())
        {
            foreach (var variant in ReasonPhrasebook.Variants(signal))
            {
                Check($"{signal}", variant, offences);
            }
        }

        Check("Fallback", ReasonPhrasebook.Fallback, offences);

        Assert.True(offences.Count == 0, string.Join('\n', offences));
    }

    [Fact]
    public void Primary_copy_does_not_invent_motives_or_unseen_updates()
    {
        var unsupported = new Regex(
            @"closed it for good|giving up|drifted off|refund line|have not seen|have not read|nothing has shipped",
            RegexOptions.IgnoreCase);
        foreach (var signal in Enum.GetValues<ReasonSignal>())
            foreach (var variant in ReasonPhrasebook.Variants(signal))
                Assert.False(unsupported.IsMatch(variant), variant);
    }

    // ── The reported bug, at feed level ────────────────────────────────────

    [Fact]
    public async Task Games_sharing_one_facet_never_render_competing_superlatives()
    {
        // Shared taste evidence remains inspectable without repeating it in prose.
        var committed = await _harness.SeedGameAsync(
            "The Long Dark", minutes: 5_000, lastPlayed: AsOf.AddYears(-2));
        await _harness.SeedGenreAsync(committed, "Survival");

        for (var i = 0; i < 5; i++)
        {
            var sealedGame = await _harness.SeedGameAsync($"Unopened Survival {i:00}");
            await _harness.SeedGenreAsync(sealedGame, "Survival");
        }

        // A second descriptor with real but much smaller weight, so the feed
        // also holds cards whose match is faint.
        var faint = await _harness.SeedGameAsync(
            "Small Roguelike", minutes: 150, lastPlayed: AsOf.AddYears(-3));
        await _harness.SeedGenreAsync(faint, "Roguelike");

        for (var i = 0; i < 2; i++)
        {
            var sealedGame = await _harness.SeedGameAsync($"Unopened Roguelike {i:00}");
            await _harness.SeedGenreAsync(sealedGame, "Roguelike");
        }

        var feed = await _harness.Engine.GetFeedAsync(RecommendHarness.Request());
        var reasons = feed.Items.Select(i => i.Reason).ToList();

        Assert.True(feed.Items.Count(i => i.Explanation.Evidence.TasteFacetName == "Survival") >= 2);
        Assert.All(reasons, reason => Assert.DoesNotContain("Survival", reason));

        var offences = new List<string>();
        foreach (var reason in reasons)
        {
            Check("card", reason, offences);
        }

        Assert.True(offences.Count == 0, string.Join('\n', offences));

    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static void Check(string label, string text, List<string> offences)
    {
        foreach (var (name, pattern) in Superlatives)
        {
            var match = pattern.Match(text);
            if (match.Success)
            {
                offences.Add($"{label}: {name} — \"{match.Value}\" in: {text}");
            }
        }
    }

}
