using Xunit;

// Fact variety on one surface (TASK-76). When the variant ledger (TASK-71)
// kept every sentence distinct but the same facet name appeared on three of
// six cards, the shelf still read as templated. These tests pin the citation
// cap, prove the cap binds on the reported shelf, prove it does not bind
// when lifted, and verify the three safety properties: no false claim, a
// card whose fact is spent reaches for the next-strongest rather than
// repeating, and a card with nothing else to say says less. Determinism is
// tested at both the engine level and the ledger level.
namespace Winnow.Recommend.Tests;

public class ShelfFactVarietyTests : IDisposable
{
    private static readonly RecommendationTuning Tuning = RecommendationTuning.Default;
    private static DateTime AsOf => RecommendHarness.AsOf;

    private static readonly string[] PatchedGames =
    [
        "Stormworks", "Stationeers", "Project Gorgon",
        "Dune Awakening", "PEAK", "The Old Republic",
    ];

    private readonly RecommendHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    // Pins the cap for a 6-card shelf (2), a 20-card feed (6), and the
    // floor for surfaces shorter than the ratio would otherwise silence.
    [Fact]
    public void The_citation_cap_is_stated_rather_than_implied()
    {
        Assert.Equal(2, ShelfReasonLedger.CapFor(6, Tuning));
        Assert.Equal(6, ShelfReasonLedger.CapFor(20, Tuning));

        // A short surface is never silenced below the floor.
        Assert.Equal(2, ShelfReasonLedger.CapFor(1, Tuning));
        Assert.Equal(2, ShelfReasonLedger.CapFor(3, Tuning));
    }

    // Reproduces the photographed shelf: a beloved 50-hour Sandbox anchor
    // plus the six patched Sandbox games from the report. Asserts no more
    // than the cap name the facet.
    [Fact]
    public async Task No_more_than_the_cap_of_one_shelf_names_the_same_facet()
    {
        await SeedSandboxShelfAsync();

        var shelf = await PatchedShelfAsync();

        Assert.Equal(PatchedGames.Length, shelf.Count);

        var naming = shelf.Count(i => i.Reason.Contains("Sandbox", StringComparison.Ordinal));
        Assert.True(
            naming <= ShelfReasonLedger.CapFor(shelf.Count, Tuning),
            $"{naming} of {shelf.Count} cards name Sandbox:\n"
                + string.Join('\n', shelf.Select(i => $"{i.Title}: {i.Reason}")));
    }

    // Runs the identical shelf with the cap lifted and asserts the facet
    // DOES repeat beyond the cap, so the test above cannot quietly stop
    // proving anything.
    [Fact]
    public async Task The_same_shelf_repeats_the_facet_when_nothing_counts_it()
    {
        await SeedSandboxShelfAsync();

        var uncapped = await PatchedShelfAsync(Tuning with { FactCitationFloor = 99 });
        var naming = uncapped.Count(i => i.Reason.Contains("Sandbox", StringComparison.Ordinal));

        Assert.True(
            naming > ShelfReasonLedger.CapFor(uncapped.Count, Tuning),
            "The uncapped shelf did not repeat the facet, so the capped test above no longer "
            + "proves anything:\n"
                + string.Join('\n', uncapped.Select(i => $"{i.Title}: {i.Reason}")));
    }

    // Adds a Roguelike game to the Sandbox shelf and asserts every facet
    // name a card prints belongs to that card's own evidence. Variety is
    // never bought with a false claim.
    [Fact]
    public async Task No_card_names_a_facet_it_does_not_carry()
    {
        await SeedSandboxShelfAsync();

        var odd = await _harness.SeedGameAsync(
            "Caves of Qud", minutes: 200, lastPlayed: AsOf.AddYears(-3));
        await _harness.SeedGenreAsync(odd, "Roguelike");
        await _harness.SeedMajorUpdateAsync(odd, AsOf.AddMonths(-1), string.Empty);

        var shelf = await PatchedShelfAsync();

        foreach (var item in shelf)
        {
            foreach (var facet in new[] { "Sandbox", "Roguelike" })
            {
                if (item.Reason.Contains(facet, StringComparison.Ordinal))
                {
                    Assert.Equal(facet, item.Explanation.Evidence.TasteFacetName);
                }
            }
        }
    }

    // Three cards each carrying taste match and installed. The first two
    // say the taste clause; the third says the installed clause instead
    // and does not name the facet.
    [Fact]
    public void A_card_whose_facet_is_spent_reaches_for_its_next_strongest_fact()
    {
        var ledger = Ledger(3);
        var rendered = new List<string>();

        for (var releaseId = 1L; releaseId <= 3; releaseId++)
        {
            rendered.Add(ReasonBuilder.Build(
                Sealed(releaseId, ReasonSignal.TasteMatch, ReasonSignal.Installed), Tuning, ledger));
        }

        Assert.All(rendered.Take(2), r => Assert.True(
            Says(r, ReasonSignal.TasteMatch), r));
        Assert.False(Says(rendered[2], ReasonSignal.TasteMatch), rendered[2]);
        Assert.True(Says(rendered[2], ReasonSignal.Installed), rendered[2]);
        Assert.DoesNotContain("Sandbox", rendered[2], StringComparison.Ordinal);
    }

    // The same three cards with taste match as their ONLY supporting fact.
    // The third renders as a bare never-opened opening, matched against the
    // phrasebook so the assertion cannot drift from the copy.
    [Fact]
    public void A_card_with_nothing_else_to_add_says_less_rather_than_repeating()
    {
        var ledger = Ledger(3);
        var rendered = new List<string>();

        for (var releaseId = 1L; releaseId <= 3; releaseId++)
        {
            rendered.Add(ReasonBuilder.Build(
                Sealed(releaseId, ReasonSignal.TasteMatch), Tuning, ledger));
        }

        Assert.All(rendered.Take(2), r => Assert.True(
            Says(r, ReasonSignal.TasteMatch), r));

        // Exactly one clause: the whole sentence is a never-opened opening and
        // nothing was borrowed to fill the gap.
        var openings = ReasonPhrasebook.Variants(ReasonSignal.NeverOpened, ReasonClause.Primary)
            .Select(v => v + ".")
            .ToList();

        Assert.Contains(rendered[2], openings, StringComparer.Ordinal);
    }

    // A third card carrying Roguelike is not blocked by two Sandbox cards
    // ahead of it, because a different descriptor is a different claim.
    [Fact]
    public void A_different_facet_is_a_different_claim_and_is_not_blocked()
    {
        var ledger = Ledger(3);

        ReasonBuilder.Build(Sealed(1, ReasonSignal.TasteMatch), Tuning, ledger);
        ReasonBuilder.Build(Sealed(2, ReasonSignal.TasteMatch), Tuning, ledger);

        var other = ReasonBuilder.Build(
            Sealed(3, ReasonSignal.TasteMatch) with
            {
                Evidence = Evidence(3) with { TasteFacetName = "Roguelike" },
            },
            Tuning,
            ledger);

        Assert.True(Says(other, ReasonSignal.TasteMatch, "Roguelike"), other);
    }

    // Six mode-mismatched cards all keep the mismatch clause. Demotion
    // disclosures are exempt from the citation cap because withholding one
    // would hide ranking information.
    [Fact]
    public void A_demotion_is_still_disclosed_on_every_card_that_earned_it()
    {
        var ledger = Ledger(6);

        for (var releaseId = 1L; releaseId <= 6; releaseId++)
        {
            var reason = ReasonBuilder.Build(
                Sealed(releaseId, ReasonSignal.OnlineOnlyMismatch, ReasonSignal.Installed),
                Tuning,
                ledger);

            Assert.True(Says(reason, ReasonSignal.OnlineOnlyMismatch), reason);
        }
    }

    // Determinism at the engine level: the same library renders the same
    // shelf with the same sentences on a second pass.
    [Fact]
    public async Task The_same_shelf_renders_the_same_way_twice()
    {
        await SeedSandboxShelfAsync();

        var first = await PatchedShelfAsync();
        var second = await PatchedShelfAsync();

        Assert.Equal(
            first.Select(i => (i.ReleaseId, i.Reason)),
            second.Select(i => (i.ReleaseId, i.Reason)));
    }

    // Determinism at the ledger level: the same cards in the same order
    // produce the same sentences from two independent ledger instances.
    // The ledger is order-driven, not clock-driven.
    [Fact]
    public void The_same_cards_in_the_same_order_render_the_same_sentences()
    {
        Assert.Equal(RenderRun(), RenderRun());

        static List<string> RenderRun()
        {
            var ledger = Ledger(6);
            var rendered = new List<string>();
            for (var releaseId = 1L; releaseId <= 6; releaseId++)
            {
                rendered.Add(ReasonBuilder.Build(
                    Sealed(releaseId, ReasonSignal.TasteMatch, ReasonSignal.Installed),
                    Tuning,
                    ledger));
            }

            return rendered;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static ShelfReasonLedger Ledger(int surfaceCards)
        => new(ShelfReasonLedger.CapFor(surfaceCards, Tuning));

    /// <summary>
    /// Whether <paramref name="reason"/> contains a secondary-clause phrasing
    /// for <paramref name="signal"/>, with <paramref name="facet"/> substituted
    /// into the phrasebook tokens. Matches against the phrasebook directly so
    /// the assertion cannot drift from the shipped copy.
    /// </summary>
    private static bool Says(string reason, ReasonSignal signal, string facet = "Sandbox")
        => ReasonPhrasebook.Variants(signal, ReasonClause.Secondary)
            .Select(v => v
                .Replace("{strongFacet}", facet, StringComparison.Ordinal)
                .Replace("{facet}", facet, StringComparison.Ordinal))
            .Any(v => reason.Contains(v, StringComparison.Ordinal));

    private static RecommendationReason Sealed(long releaseId, params ReasonSignal[] supporting)
        => new()
        {
            Primary = ReasonSignal.NeverOpened,
            Secondary = supporting.Length > 0 ? supporting[0] : ReasonSignal.None,
            SupportingSignals = supporting,
            Evidence = Evidence(releaseId),
        };

    private static ReasonEvidence Evidence(long releaseId) => new()
    {
        ReleaseId = releaseId,
        Title = "A Sealed Game",
        Store = "steam",
        PlaytimeMinutes = 0,
        TasteFacetName = "Sandbox",
        TasteAffinity = 1.0,
    };

    /// <summary>
    /// Seeds the shelf photographed on 2026-09-02: one high-hour Sandbox
    /// anchor that establishes the taste profile, plus the six patched
    /// Sandbox games from the report.
    /// </summary>
    private async Task SeedSandboxShelfAsync()
    {
        var anchor = await _harness.SeedGameAsync(
            "Beloved Sandbox", minutes: 3_000, lastPlayed: AsOf.AddYears(-1));
        await _harness.SeedGenreAsync(anchor, "Sandbox");

        foreach (var title in PatchedGames)
        {
            var game = await _harness.SeedGameAsync(
                title, minutes: 200, lastPlayed: AsOf.AddYears(-3));
            await _harness.SeedGenreAsync(game, "Sandbox");
            await _harness.SeedMajorUpdateAsync(game, AsOf.AddMonths(-1), string.Empty);
        }
    }

    private async Task<IReadOnlyList<Recommendation>> PatchedShelfAsync(
        RecommendationTuning? tuning = null)
    {
        var feed = await _harness.Engine.GetShelvesAsync(
            RecommendHarness.Request() with { Tuning = tuning ?? Tuning });
        var shelf = feed.Shelves.FirstOrDefault(s => s.Id == ShelfIds.PatchedWhileAway);
        Assert.NotNull(shelf);
        return shelf!.Items;
    }
}
