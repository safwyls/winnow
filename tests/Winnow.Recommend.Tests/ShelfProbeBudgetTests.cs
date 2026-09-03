using Xunit;

namespace Winnow.Recommend.Tests;

/// <summary>
/// The probe budget's shape, not its number. The budget bounds how many
/// ownerships one shelf pass reads history for; only probed candidates are
/// scored and only scored candidates can fill a shelf. A budget spent in
/// claim order deletes the last shelves outright instead of trimming each
/// shelf's tail. That is the measured failure at the old default of 150:
/// patched_while_away and worth_another_look consumed the whole budget, the
/// last three shelves were never scored, and the feed was two shelves and
/// twelve items where the design intends five and twenty-eight.
/// </summary>
public class ShelfProbeBudgetTests : IClassFixture<ShelfProbeBudgetTests.CrowdedLibrary>
{
    private readonly CrowdedLibrary _library;

    public ShelfProbeBudgetTests(CrowdedLibrary library) => _library = library;

    private static DateTime AsOf => RecommendHarness.AsOf;

    private static readonly string[] AllShelves =
    [
        ShelfIds.PatchedWhileAway,
        ShelfIds.WorthAnotherLook,
        ShelfIds.ReadyToPlay,
        ShelfIds.BarelyTouched,
        ShelfIds.OnYourTaste,
    ];

    private static RecommendationRequest Request(int? probeLimit = null)
    {
        var tuning = probeLimit is { } limit
            ? RecommendationTuning.Default with { ShelfProbeLimit = limit }
            : RecommendationTuning.Default;

        return RecommendHarness.Request() with { MaxPerShelf = 6, Tuning = tuning };
    }

    /// <summary>
    /// The three limits (5, 25, 150) exist because the property under test is
    /// the shape, not the number: no plausible cap makes a claim-order fill
    /// fair, while round-robin is fair at any cap. 150 is the old default and
    /// the exact reported bug. Each case asserts the budget actually bound
    /// (probe count equals the limit) before asserting the feed, so a fixture
    /// that stopped exhausting it would fail loudly rather than pass vacuously.
    /// </summary>
    [Theory]
    [InlineData(5)]
    [InlineData(25)]
    [InlineData(150)]
    public async Task Every_shelf_is_scored_when_the_probe_budget_binds(int probeLimit)
    {
        var feed = await _library.Harness.Engine.GetShelvesAsync(Request(probeLimit));

        Assert.Equal(probeLimit, feed.HistoryProbeCount);

        var populated = feed.Shelves
            .Where(s => s.Items.Count > 0)
            .Select(s => s.Id)
            .ToArray();
        Assert.Equal(AllShelves, populated);
    }

    /// <summary>
    /// The default must be a brake on a pathological library, never a trim on a
    /// normal one. Compared against the same pass with the budget removed: the
    /// shelves, items, and order must be identical. A default too small for the
    /// library's natural probe demand silently changes which games are
    /// recommended, even once starvation is structurally impossible.
    /// </summary>
    [Fact]
    public async Task The_default_probe_budget_does_not_change_the_feed_it_bounds()
    {
        var bounded = await _library.Harness.Engine.GetShelvesAsync(Request());
        var unbounded = await _library.Harness.Engine.GetShelvesAsync(Request(int.MaxValue));

        Assert.Equal(Fingerprint(unbounded), Fingerprint(bounded));
        Assert.Equal(AllShelves, bounded.Shelves.Select(s => s.Id).ToArray());
    }

    private static string Fingerprint(ShelfFeed feed) => string.Join(
        " | ",
        feed.Shelves.Select(s => s.Id + ": " + string.Join(",", s.Items.Select(i => i.ReleaseId))));

    /// <summary>
    /// A library shaped like the measured one in the only respect this test
    /// needs: two early shelves whose shortlists alone exceed the old default,
    /// and three later shelves with real candidates waiting behind them. The
    /// five pools are deliberately disjoint (installed rows have no minutes,
    /// sampled rows are not installed, sealed rows are the only ones carrying
    /// the taste genre) so that a starved shelf is unambiguously the budget's
    /// doing and not a claim-order collision. Seeded once for the whole class
    /// because ~200 games is several thousand inserts.
    /// </summary>
    public sealed class CrowdedLibrary : IAsyncLifetime
    {
        public RecommendHarness Harness { get; } = new();

        public async Task InitializeAsync()
        {
            for (var i = 0; i < 90; i++)
            {
                var patched = await Harness.SeedGameAsync(
                    $"Patched{i} Comeback", minutes: 200 + i, lastPlayed: AsOf.AddYears(-3));
                await Harness.SeedMajorUpdateAsync(patched, AsOf.AddMonths(-1), $"v2.{i} Overhaul");
                await Harness.SeedGenreAsync(patched, "Shooter");
            }

            for (var i = 0; i < 90; i++)
            {
                var drifted = await Harness.SeedGameAsync(
                    $"Drifted{i} Off", minutes: 400 + i, lastPlayed: AsOf.AddYears(-2));
                await Harness.SeedGenreAsync(drifted, "Shooter");
            }

            for (var i = 0; i < 12; i++)
            {
                await Harness.SeedGameAsync($"Disk{i} Resident", installed: true);
            }

            for (var i = 0; i < 12; i++)
            {
                await Harness.SeedGameAsync(
                    $"Sampled{i} Once", minutes: 40 + i, lastPlayed: AsOf.AddYears(-2));
            }

            var anchor = await Harness.SeedGameAsync(
                "Beloved Survival Game", minutes: 3_000, lastPlayed: AsOf.AddYears(-1));
            await Harness.SeedGenreAsync(anchor, "Survival");

            for (var i = 0; i < 12; i++)
            {
                var sealedGame = await Harness.SeedGameAsync($"Sealed{i} Survival");
                await Harness.SeedGenreAsync(sealedGame, "Survival");
            }
        }

        public Task DisposeAsync()
        {
            Harness.Dispose();
            return Task.CompletedTask;
        }
    }
}
