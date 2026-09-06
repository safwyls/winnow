using Xunit;

namespace Winnow.Recommend.Tests;

/// <summary>
/// A caller that wants replacements ready for a dismissed card asks one pass
/// for more than it shows and holds the rest. The whole feature rests on that
/// deeper ask being invisible to the reader, and there are two ways it would
/// not be.
///
/// <para>The shelves are filled in claim order against one shared set of
/// claimed works, so an early shelf holding six spare cards would claim six
/// works a later shelf's VISIBLE slice needed — a deeper ask that shrinks the
/// feed. And the reason ledger's variety caps are derived from how many cards
/// a surface holds, so asking for twelve to show six would double how many of
/// those six may cite the same supporting fact, undoing the fact-deduplication
/// the ledger exists for.</para>
///
/// <para>Both are pinned here against the feed a plain request returns: the
/// same shelves, each holding the same number of cards, and the same cap on how
/// many of those cards may cite one fact.</para>
/// </summary>
public class ShelfReserveTests : IClassFixture<ShelfReserveTests.ContestedLibrary>
{
    private const int Visible = 6;
    private const int Depth = 10;

    private readonly ContestedLibrary _library;

    public ShelfReserveTests(ContestedLibrary library) => _library = library;

    private static RecommendationRequest Plain => RecommendHarness.Request() with
    {
        MaxPerShelf = Visible,
    };

    private static RecommendationRequest WithReserve => RecommendHarness.Request() with
    {
        MaxPerShelf = Depth,
        VisiblePerShelf = Visible,
    };

    /// <summary>
    /// The property a reserve must not cost: the same shelves, each holding the
    /// same number of cards. A deeper ask may swap a card for a better one —
    /// asking for twelve shortlists more candidates for history, and one of
    /// them can turn out to belong on screen, which is the shortlist bound
    /// working rather than the reserve leaking. What it may never do is hand a
    /// later shelf fewer cards because an earlier shelf claimed the works for a
    /// reserve nobody has seen.
    /// </summary>
    [Fact]
    public async Task A_reserve_costs_no_shelf_and_no_card_on_any_shelf()
    {
        var plain = await _library.Harness.Engine.GetShelvesAsync(Plain);
        var deep = await _library.Harness.Engine.GetShelvesAsync(WithReserve);

        Assert.Equal(
            plain.Shelves.Select(s => s.Id),
            deep.Shelves.Select(s => s.Id));

        Assert.Equal(
            plain.Shelves.Select(s => $"{s.Id}: {s.Items.Count}"),
            deep.Shelves.Select(s => $"{s.Id}: {Math.Min(s.Items.Count, Visible)}"));
    }

    /// <summary>
    /// The specific collision the two-pass fill exists to prevent, isolated:
    /// every installed game in this library also qualifies for the shelf that
    /// claims after it, so a first shelf allowed to take twelve leaves the
    /// second with two. The visible slices must be six and six.
    /// </summary>
    [Fact]
    public async Task A_shelf_holding_a_reserve_does_not_take_the_next_shelfs_visible_cards()
    {
        var deep = await _library.Harness.Engine.GetShelvesAsync(WithReserve);

        Assert.Equal(
            Visible,
            Shelf(deep, ShelfIds.ReadyToPlay).Items.Take(Visible).Count());
        Assert.Equal(
            Visible,
            Shelf(deep, ShelfIds.BarelyTouched).Items.Take(Visible).Count());
    }

    [Fact]
    public async Task A_shelf_holds_the_items_past_the_visible_slice_as_its_reserve()
    {
        var deep = await _library.Harness.Engine.GetShelvesAsync(WithReserve);

        var patched = Shelf(deep, ShelfIds.PatchedWhileAway);
        Assert.Equal(Depth, patched.Items.Count);

        // The reserve is ordered among itself, not merged into the slice on
        // screen: the first six are what the caller shows.
        var reserve = patched.Items.Skip(Visible).ToList();
        Assert.Equal(
            reserve.OrderByDescending(i => i.Score).Select(i => i.ReleaseId),
            reserve.Select(i => i.ReleaseId));
    }

    /// <summary>
    /// One work, one card, reserve included. A replacement that is already on
    /// the shelf it would join is not a replacement, and the per-work claim is
    /// what makes a reserve item legal to swap in without re-checking it.
    /// </summary>
    [Fact]
    public async Task No_work_is_both_on_screen_and_in_a_reserve()
    {
        var deep = await _library.Harness.Engine.GetShelvesAsync(WithReserve);

        var releases = deep.Shelves.SelectMany(s => s.Items).Select(i => i.ReleaseId).ToList();
        Assert.Equal(releases.Count, releases.Distinct().Count());
    }

    [Fact]
    public async Task Showing_everything_it_asks_for_is_the_pass_it_always_was()
    {
        var unstated = await _library.Harness.Engine.GetShelvesAsync(Plain);
        var stated = await _library.Harness.Engine.GetShelvesAsync(
            Plain with { VisiblePerShelf = Visible });

        Assert.Equal(Fingerprint(unstated, Visible), Fingerprint(stated, Visible));
    }

    private static RecommendationShelf Shelf(ShelfFeed feed, string id)
        => Assert.Single(feed.Shelves, s => s.Id == id);

    private static string Fingerprint(ShelfFeed feed, int take) => string.Join(
        "\n",
        feed.Shelves.Select(s => s.Id + ": " + string.Join(
            " / ", s.Items.Take(take).Select(i => $"{i.ReleaseId} {i.Reason}"))));

    /// <summary>
    /// A library where the shelves compete for the same games. Every installed
    /// game carries minutes under the refund line, so it qualifies for
    /// <c>ready_to_play</c> AND for <c>barely_touched</c>, and the first of
    /// those claims first — which is exactly the condition under which a
    /// deeper ask would eat the second shelf's visible slice. The two earlier
    /// shelves are seeded deep enough to hold a full reserve, and the sealed
    /// games all share one genre so the reason ledger's fact cap actually
    /// binds on the shelf that draws them.
    /// </summary>
    public sealed class ContestedLibrary : IAsyncLifetime
    {
        private static DateTime AsOf => RecommendHarness.AsOf;

        public RecommendHarness Harness { get; } = new();

        public async Task InitializeAsync()
        {
            for (var i = 0; i < 20; i++)
            {
                var patched = await Harness.SeedGameAsync(
                    $"Patched{i} Comeback", minutes: 200 + i, lastPlayed: AsOf.AddYears(-3));
                await Harness.SeedMajorUpdateAsync(patched, AsOf.AddMonths(-1), $"v2.{i} Overhaul");
                await Harness.SeedGenreAsync(patched, "Shooter");
            }

            for (var i = 0; i < 20; i++)
            {
                var drifted = await Harness.SeedGameAsync(
                    $"Drifted{i} Off", minutes: 400 + i, lastPlayed: AsOf.AddYears(-2));
                await Harness.SeedGenreAsync(drifted, "Puzzle");
            }

            // Installed AND sampled: both sub-refund shelves want these ten.
            for (var i = 0; i < 10; i++)
            {
                await Harness.SeedGameAsync(
                    $"Disk{i} Resident",
                    minutes: 40 + i,
                    lastPlayed: AsOf.AddYears(-2),
                    installed: true);
            }

            // Sampled but not installed: only the second of the two wants these.
            for (var i = 0; i < 8; i++)
            {
                await Harness.SeedGameAsync(
                    $"Sampled{i} Once", minutes: 30 + i, lastPlayed: AsOf.AddYears(-2));
            }

            var anchor = await Harness.SeedGameAsync(
                "Beloved Survival Game", minutes: 3_000, lastPlayed: AsOf.AddYears(-1));
            await Harness.SeedGenreAsync(anchor, "Survival");

            for (var i = 0; i < 14; i++)
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
