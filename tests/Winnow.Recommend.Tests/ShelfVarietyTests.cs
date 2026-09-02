using Xunit;

namespace Winnow.Recommend.Tests;

// No two cards on one shelf may read the same sentence.
//
// The phrasing of a card is chosen by hashing its release id, which is
// deterministic and per-card and knows nothing about its neighbours. Observed
// 2026-09-02 on the "Patched while you were away" shelf: Stationeers and PEAK
// drew the same variant. Two cards side by side, one sentence.
//
// TASK-58 fixed the neighbouring defect (what a variant may claim) and left
// this one, the variant repeating at all. The fix is a ledger carried down one
// shelf's render plus enough variants to fill a shelf; both are needed, and
// the second test here is the one that fails without the variants.
public class ShelfVarietyTests : IDisposable
{
    private readonly RecommendHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private static DateTime AsOf => RecommendHarness.AsOf;

    private static readonly string[] PatchedGames =
    [
        "Stationeers", "PEAK", "Stormworks", "Dune Awakening",
        "Project Gorgon", "The Old Republic",
    ];

    [Fact]
    public async Task No_two_cards_on_the_patched_shelf_read_the_same()
    {
        foreach (var title in PatchedGames)
        {
            var game = await _harness.SeedGameAsync(
                title, minutes: 200, lastPlayed: AsOf.AddYears(-3));
            await _harness.SeedMajorUpdateAsync(game, AsOf.AddMonths(-1), $"{title} v2.0 Notes");
        }

        var shelf = await PatchedShelfAsync();

        Assert.Equal(PatchedGames.Length, shelf.Count);
        AssertAllDistinct(shelf);
    }

    // The worst case, and the one the running app was in: no update carries a
    // title, so every variant naming one is skipped and the shelf has to fill
    // itself from what is left.
    [Fact]
    public async Task No_two_cards_repeat_when_no_update_carries_a_title()
    {
        foreach (var title in PatchedGames)
        {
            var game = await _harness.SeedGameAsync(
                title, minutes: 200, lastPlayed: AsOf.AddYears(-3));
            await _harness.SeedMajorUpdateAsync(game, AsOf.AddMonths(-1), string.Empty);
        }

        var shelf = await PatchedShelfAsync();

        Assert.Equal(PatchedGames.Length, shelf.Count);
        Assert.All(shelf, item => Assert.Equal(
            ReasonSignal.PatchedSinceYouLeft, item.Explanation.Primary));
        AssertAllDistinct(shelf);
    }

    // The ledger is a tie-breaker, not a shuffler: a card whose own hash lands
    // on a free variant renders exactly what it rendered before the ledger
    // existed. So the first card on a shelf is never moved.
    [Fact]
    public async Task The_first_card_on_a_shelf_keeps_the_phrasing_its_own_id_chose()
    {
        var game = await _harness.SeedGameAsync(
            "Stationeers", minutes: 200, lastPlayed: AsOf.AddYears(-3));
        await _harness.SeedMajorUpdateAsync(game, AsOf.AddMonths(-1), "v1.0 Notes");

        var shelf = await PatchedShelfAsync();
        var item = Assert.Single(shelf);

        var unledgered = ReasonBuilder.Build(item.Explanation, RecommendationTuning.Default);
        Assert.Equal(unledgered, item.Reason);
    }

    // The shelf tests above run through the real engine, where two cards can
    // differ by a filled token even when they chose the same variant. This one
    // holds the ledger itself: six games identical in everything the copy can
    // say except their ids, so the ONLY thing that can separate two sentences
    // is the variant each one got.
    //
    // Without the ledger this fails by pigeonhole. With no update title, the
    // signal has four token-bearing variants and the builder prefers those, so
    // six cards drawing from four must repeat — which is the shelf the user
    // photographed.
    [Fact]
    public void Six_identical_games_on_one_shelf_take_six_phrasings()
    {
        var ledger = new ReasonVariantLedger();
        var rendered = new List<string>();

        for (var releaseId = 1L; releaseId <= 6; releaseId++)
        {
            rendered.Add(ReasonBuilder.Build(
                Patched(releaseId), RecommendationTuning.Default, ledger));
        }

        Assert.Equal(6, rendered.Distinct(StringComparer.Ordinal).Count());
    }

    // The same six without a ledger, to show the ledger is what is doing the
    // work rather than the new variants alone.
    [Fact]
    public void The_same_six_repeat_when_nothing_remembers_the_shelf()
    {
        var rendered = new List<string>();
        for (var releaseId = 1L; releaseId <= 6; releaseId++)
        {
            rendered.Add(ReasonBuilder.Build(Patched(releaseId), RecommendationTuning.Default));
        }

        Assert.True(
            rendered.Distinct(StringComparer.Ordinal).Count() < 6,
            "Six cards drawing unledgered from four usable variants did not repeat, so the "
            + "ledger test above no longer proves anything.");
    }

    /// <summary>
    /// A patched game with nothing about it a variant could use to differ:
    /// no update title, one update, no secondary clause.
    /// </summary>
    private static RecommendationReason Patched(long releaseId) => new()
    {
        Primary = ReasonSignal.PatchedSinceYouLeft,
        Secondary = ReasonSignal.None,
        Evidence = new ReasonEvidence
        {
            ReleaseId = releaseId,
            Title = "A Patched Game",
            PlaytimeMinutes = 200,
            UpdatesSinceLastPlayed = 1,
            LatestUpdateTitle = null,
        },
    };

    private static void AssertAllDistinct(IReadOnlyList<Recommendation> shelf)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in shelf)
        {
            Assert.False(
                seen.TryGetValue(item.Reason, out var first),
                $"'{item.Title}' and '{first}' render the same sentence: {item.Reason}");
            seen[item.Reason] = item.Title;
        }
    }

    private async Task<IReadOnlyList<Recommendation>> PatchedShelfAsync()
    {
        var feed = await _harness.Engine.GetShelvesAsync(RecommendHarness.Request());
        var shelf = feed.Shelves.FirstOrDefault(s => s.Id == ShelfIds.PatchedWhileAway);
        Assert.NotNull(shelf);
        return shelf!.Items;
    }
}
