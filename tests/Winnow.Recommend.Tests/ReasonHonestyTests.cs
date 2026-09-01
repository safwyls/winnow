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
    public void No_variant_anywhere_in_the_phrasebook_claims_a_rank_it_cannot_prove()
    {
        var offences = new List<string>();

        foreach (var signal in Enum.GetValues<ReasonSignal>())
        {
            foreach (var clause in new[] { ReasonClause.Primary, ReasonClause.Secondary })
            {
                foreach (var variant in ReasonPhrasebook.Variants(signal, clause))
                {
                    Check($"{signal}/{clause}", variant, offences);
                }
            }
        }

        Check("Fallback", ReasonPhrasebook.Fallback, offences);

        Assert.True(offences.Count == 0, string.Join('\n', offences));
    }

    [Fact]
    public void The_taste_match_fallback_still_renders_when_the_facet_is_unknown()
    {
        // AC #3: the existing non-superlative variant is the token-free one,
        // and a game with a taste signal but no descriptor name must still get
        // a supporting clause rather than a bare opening.
        var reason = ReasonBuilder.Build(
            new RecommendationReason
            {
                Primary = ReasonSignal.NeverOpened,
                Secondary = ReasonSignal.TasteMatch,
                Evidence = Evidence(1) with { TasteFacetName = null, TasteAffinity = null },
            },
            Tuning);

        Assert.Contains("it sits squarely in what you actually play", reason, StringComparison.Ordinal);
    }

    // ── The gated phrasings ────────────────────────────────────────────────

    [Fact]
    public void A_strength_claim_is_unusable_until_the_measured_affinity_earns_it()
    {
        var strong = StrongPhrasings();
        Assert.NotEmpty(strong);

        // Just under the bar the On Your Taste shelf uses: the descriptor is a
        // real match, but not one the copy may call deep.
        foreach (var reason in Sweep(Tuning.OnTasteMinAffinity - 0.01))
        {
            foreach (var phrasing in strong)
            {
                Assert.DoesNotContain(phrasing, reason, StringComparison.Ordinal);
            }
        }

        // At the bar exactly, and above it, they become selectable.
        foreach (var affinity in new[] { Tuning.OnTasteMinAffinity, 1.0 })
        {
            var rendered = Sweep(affinity);
            Assert.Contains(rendered, r => strong.Any(p => r.Contains(p, StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void A_faint_match_still_gets_a_specific_clause_naming_its_descriptor()
    {
        // Degrade by widening the claim, never by going blank: a weak match
        // loses the strength phrasings and keeps the descriptor.
        var named = Sweep(0.05).Count(r => r.Contains("Survival", StringComparison.Ordinal));
        Assert.True(named > 0, "a faint taste match still names the descriptor it matched");
    }

    [Fact]
    public void The_gating_does_not_disturb_per_release_stability()
    {
        var first = Sweep(1.0);
        var second = Sweep(1.0);
        Assert.Equal(first, second);
    }

    // ── The reported bug, at feed level ────────────────────────────────────

    [Fact]
    public async Task Games_sharing_one_facet_never_render_competing_superlatives()
    {
        // The reported shape: several unplayed games carrying the descriptor
        // the user's hours actually sit in, so the taste clause fires on card
        // after card and the per-card hash spreads them across the variants.
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

        // The situation the bug needs must actually be on screen, or the
        // assertion below proves nothing.
        var sharing = reasons.Count(r => r.Contains("Survival", StringComparison.Ordinal));
        Assert.True(sharing >= 2, $"only {sharing} cards carry the shared descriptor:\n"
            + string.Join('\n', reasons));

        var offences = new List<string>();
        foreach (var reason in reasons)
        {
            Check("card", reason, offences);
        }

        Assert.True(offences.Count == 0, string.Join('\n', offences));

        // And the faint side of the library never borrows the strength copy.
        var strong = StrongPhrasings("Roguelike");
        foreach (var reason in reasons.Where(r => r.Contains("Roguelike", StringComparison.Ordinal)))
        {
            foreach (var phrasing in strong)
            {
                Assert.DoesNotContain(phrasing, reason, StringComparison.Ordinal);
            }
        }
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

    /// <summary>
    /// Every strength phrasing as it would read once filled, read from the
    /// phrasebook rather than hard-coded so rewording the copy can never
    /// quietly retire this test.
    /// </summary>
    private static List<string> StrongPhrasings(string facet = "Survival")
        => ReasonPhrasebook.Variants(ReasonSignal.TasteMatch, ReasonClause.Secondary)
            .Where(v => v.Contains("{strongFacet}", StringComparison.Ordinal))
            .Select(v => v
                .Replace("{strongFacet}", facet, StringComparison.Ordinal)
                .Replace("{facet}", facet, StringComparison.Ordinal))
            .ToList();

    /// <summary>Every sentence the taste clause renders across a spread of release ids at one affinity.</summary>
    private static List<string> Sweep(double affinity)
    {
        var rendered = new List<string>();
        for (var releaseId = 1; releaseId <= 60; releaseId++)
        {
            rendered.Add(ReasonBuilder.Build(
                new RecommendationReason
                {
                    Primary = ReasonSignal.NeverOpened,
                    Secondary = ReasonSignal.TasteMatch,
                    Evidence = Evidence(releaseId) with { TasteAffinity = affinity },
                },
                Tuning));
        }

        return rendered;
    }

    private static ReasonEvidence Evidence(long releaseId) => new()
    {
        ReleaseId = releaseId,
        Title = "Subnautica",
        Store = "steam",
        PlaytimeMinutes = 0,
        TasteFacetName = "Survival",
        TasteAffinity = 1.0,
    };
}
