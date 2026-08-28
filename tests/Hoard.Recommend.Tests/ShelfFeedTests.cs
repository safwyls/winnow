using Hoard.Core.Queries;
using Xunit;

namespace Hoard.Recommend.Tests;

/// <summary>
/// The shelf surface: several themed rails over one scoring pass, every one
/// of them working at Tier 0. These tests pin the membership rules, the
/// claim order, and the diversity caps — the properties that make a shelf
/// feed better than the same items in one long list.
/// </summary>
public class ShelfFeedTests : IDisposable
{
    private readonly RecommendHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private static DateTime AsOf => RecommendHarness.AsOf;

    private Task<ShelfFeed> GetShelvesAsync(RecommendationRequest? request = null)
        => _harness.Engine.GetShelvesAsync(request ?? RecommendHarness.Request());

    private static RecommendationShelf? Shelf(ShelfFeed feed, string id)
        => feed.Shelves.FirstOrDefault(s => s.Id == id);

    private static bool OnShelf(ShelfFeed feed, string shelfId, SeededGame game)
        => Shelf(feed, shelfId)?.Items.Any(i => i.ReleaseId == game.ReleaseId) ?? false;

    private static bool OnAnyShelf(ShelfFeed feed, SeededGame game)
        => feed.Shelves.Any(s => s.Items.Any(i => i.ReleaseId == game.ReleaseId));

    [Fact]
    public async Task Every_shelf_populates_from_tier_zero_facts_alone()
    {
        // One game per shelf story, all on single-snapshot, zero-session data.
        var stale = await _harness.SeedGameAsync("Patched Comeback", minutes: 200, lastPlayed: AsOf.AddYears(-3));
        await _harness.SeedMajorUpdateAsync(stale, AsOf.AddMonths(-1), "v2.0 Overhaul");
        var bounced = await _harness.SeedGameAsync("Drifted Off", minutes: 400, lastPlayed: AsOf.AddYears(-2));
        var installed = await _harness.SeedGameAsync("On Disk", installed: true);
        var sampled = await _harness.SeedGameAsync("Forty Minutes", minutes: 40, lastPlayed: AsOf.AddYears(-2));
        var onTaste = await _harness.SeedGameAsync("Sealed Survival Game");

        // Taste evidence: a committed game sharing the sealed game's genre.
        var anchor = await _harness.SeedGameAsync("Beloved Survival Game", minutes: 3_000, lastPlayed: AsOf.AddYears(-1));
        await _harness.SeedGenreAsync(anchor, "Survival");
        await _harness.SeedGenreAsync(onTaste, "Survival");

        var feed = await GetShelvesAsync();

        Assert.Equal(DataTier.ColdStart, feed.Tier);
        Assert.True(OnShelf(feed, ShelfIds.PatchedWhileAway, stale));
        Assert.True(OnShelf(feed, ShelfIds.WorthAnotherLook, bounced));
        Assert.True(OnShelf(feed, ShelfIds.ReadyToPlay, installed));
        Assert.True(OnShelf(feed, ShelfIds.BarelyTouched, sampled));
        Assert.True(OnShelf(feed, ShelfIds.OnYourTaste, onTaste));

        // Presentation order is the claim order: strongest story first.
        Assert.Equal(
            [ShelfIds.PatchedWhileAway, ShelfIds.WorthAnotherLook, ShelfIds.ReadyToPlay, ShelfIds.BarelyTouched, ShelfIds.OnYourTaste],
            feed.Shelves.Select(s => s.Id).ToArray());
    }

    [Fact]
    public async Task The_patched_shelf_reason_carries_the_update_detail()
    {
        var stale = await _harness.SeedGameAsync("Patched Comeback", minutes: 200, lastPlayed: AsOf.AddYears(-3));
        await _harness.SeedMajorUpdateAsync(stale, AsOf.AddMonths(-1), "v2.0 Overhaul");

        var feed = await GetShelvesAsync();

        var item = Assert.Single(Shelf(feed, ShelfIds.PatchedWhileAway)!.Items);
        Assert.Contains("v2.0 Overhaul", item.Reason);
    }

    [Fact]
    public async Task A_work_is_claimed_by_exactly_one_shelf()
    {
        // Installed AND sampled: eligible for ready-to-play (claim order 3)
        // and barely-touched (4). The earlier shelf claims it; the later one
        // must not repeat it — two rails fronting one game is the samey-feed
        // failure at shelf granularity.
        var both = await _harness.SeedGameAsync("Installed Sample", minutes: 40, lastPlayed: AsOf.AddYears(-1), installed: true);

        var feed = await GetShelvesAsync();

        Assert.True(OnShelf(feed, ShelfIds.ReadyToPlay, both));
        Assert.False(OnShelf(feed, ShelfIds.BarelyTouched, both));
    }

    [Fact]
    public async Task Probably_done_games_are_kept_off_worth_another_look()
    {
        var eligible = await _harness.SeedGameAsync("Drifted Off", minutes: 300, lastPlayed: AsOf.AddYears(-2));
        // A fair shake of hours, deeply dormant, nothing changed since: the
        // model says "you were right to drop this" — so the comeback shelf,
        // whose blurb argues the opposite, must not carry it.
        var done = await _harness.SeedGameAsync("Fair Shake Given", minutes: 3_000, lastPlayed: AsOf.AddYears(-6));

        var feed = await GetShelvesAsync();

        Assert.True(OnShelf(feed, ShelfIds.WorthAnotherLook, eligible));
        Assert.False(OnShelf(feed, ShelfIds.WorthAnotherLook, done));
    }

    [Fact]
    public async Task Never_opened_games_need_taste_evidence_to_reach_the_taste_shelf()
    {
        var anchor = await _harness.SeedGameAsync("Beloved Survival Game", minutes: 3_000, lastPlayed: AsOf.AddYears(-1));
        await _harness.SeedGenreAsync(anchor, "Survival");

        var onTaste = await _harness.SeedGameAsync("Sealed Survival Game");
        await _harness.SeedGenreAsync(onTaste, "Survival");
        var offTaste = await _harness.SeedGameAsync("Sealed Mystery Game");

        var feed = await GetShelvesAsync();

        Assert.True(OnShelf(feed, ShelfIds.OnYourTaste, onTaste));
        Assert.False(OnAnyShelf(feed, offTaste));
        Assert.Equal(3, feed.CandidateCount); // absent from shelves ≠ absent from the pool
    }

    [Fact]
    public async Task One_franchise_entry_per_shelf()
    {
        // The measured library holds 14 unplayed "Infinity Blade" entries; a
        // shelf that is one franchise several times over is a broken feed
        // even when every individual score is right.
        var first = await _harness.SeedGameAsync("Saga: Alpha", minutes: 40, lastPlayed: AsOf.AddYears(-2));
        var second = await _harness.SeedGameAsync("Saga: Beta", minutes: 45, lastPlayed: AsOf.AddYears(-2));

        var feed = await GetShelvesAsync();

        var shelf = Shelf(feed, ShelfIds.BarelyTouched)!;
        Assert.Single(shelf.Items);
        Assert.Contains(shelf.Items[0].ReleaseId, new[] { first.ReleaseId, second.ReleaseId });
    }

    [Fact]
    public async Task The_genre_cap_prefers_variety_then_relaxes_to_fill()
    {
        // Six roguelikes and one puzzle game, all sampled. Strict pass takes
        // the cap's worth of roguelikes and then the puzzle game; relaxation
        // refills the remaining slots with roguelikes rather than leaving a
        // short shelf, because a pool that genuinely IS mostly roguelikes
        // should still fill. This test is about the RELAXATION half; see
        // ShelfGenreCap's remarks for why the strict half is not pinned here.
        var rogues = new List<SeededGame>();
        foreach (var name in new[] { "Gloom Alpha", "Gloom Beta", "Gloom Gamma", "Gloom Delta", "Gloom Epsilon", "Gloom Zeta" })
        {
            var game = await _harness.SeedGameAsync(name, minutes: 40, lastPlayed: AsOf.AddYears(-2));
            await _harness.SeedGenreAsync(game, "Roguelike");
            rogues.Add(game);
        }

        var puzzle = await _harness.SeedGameAsync("Quiet Puzzle", minutes: 30, lastPlayed: AsOf.AddYears(-2));
        await _harness.SeedGenreAsync(puzzle, "Puzzle");

        var feed = await GetShelvesAsync(RecommendHarness.Request() with { MaxPerShelf = 6 });

        var shelf = Shelf(feed, ShelfIds.BarelyTouched)!;
        Assert.Equal(6, shelf.Items.Count);
        Assert.Contains(shelf.Items, i => i.ReleaseId == puzzle.ReleaseId);
        Assert.Equal(5, shelf.Items.Count(i => rogues.Any(r => r.ReleaseId == i.ReleaseId)));
    }

    [Fact]
    public async Task Shelf_items_render_in_score_order_despite_the_diversity_passes()
    {
        // Whatever the passes decided about membership, the visible order is
        // the scores' — a relaxation refill must not dangle at the bottom
        // above nothing, nor a capped-then-readmitted strong item sit below
        // a weaker survivor.
        for (var i = 0; i < 8; i++)
        {
            var game = await _harness.SeedGameAsync($"Sampled {i}", minutes: 30 + i * 10, lastPlayed: AsOf.AddYears(-2));
            await _harness.SeedGenreAsync(game, i < 6 ? "Roguelike" : "Puzzle");
        }

        var feed = await GetShelvesAsync(RecommendHarness.Request() with { MaxPerShelf = 6 });

        var scores = Shelf(feed, ShelfIds.BarelyTouched)!.Items.Select(i => i.Score).ToList();
        Assert.Equal(scores.OrderByDescending(s => s).ToList(), scores);
    }

    [Fact]
    public async Task Recently_played_games_reach_no_shelf()
    {
        var fresh = await _harness.SeedGameAsync("Playing It Now", minutes: 300, lastPlayed: AsOf.AddDays(-3));
        var freshInstalled = await _harness.SeedGameAsync("Installed Yesterday", minutes: 30, lastPlayed: AsOf.AddDays(-1), installed: true);

        var feed = await GetShelvesAsync();

        Assert.False(OnAnyShelf(feed, fresh));
        Assert.False(OnAnyShelf(feed, freshInstalled));
    }

    [Fact]
    public async Task Hard_exclusions_hold_for_shelves_too()
    {
        var retired = await _harness.SeedGameAsync("Finished 200h Game", minutes: 12_000, lastPlayed: AsOf.AddYears(-2));
        await _harness.SeedMajorUpdateAsync(retired, AsOf.AddMonths(-1), "Anniversary Update");

        var snoozed = await _harness.SeedGameAsync("Not Now", minutes: 300, lastPlayed: AsOf.AddYears(-2));
        var dismissed = await _harness.SeedGameAsync("Never Again", minutes: 300, lastPlayed: AsOf.AddYears(-2));

        var feed = await GetShelvesAsync(RecommendHarness.Request() with
        {
            SnoozedReleaseIds = new HashSet<long> { snoozed.ReleaseId },
            NotInterestedReleaseIds = new HashSet<long> { dismissed.ReleaseId },
        });

        Assert.False(OnAnyShelf(feed, retired));
        Assert.False(OnAnyShelf(feed, snoozed));
        Assert.False(OnAnyShelf(feed, dismissed));
    }

    [Fact]
    public async Task Empty_shelves_are_omitted_not_rendered_blank()
    {
        // Facetless shelfware only: no patch stories, nothing installed,
        // nothing sampled, no taste evidence — the answer is no shelves,
        // each absent rather than present-and-empty.
        await _harness.SeedGameAsync("Never Opened A");
        await _harness.SeedGameAsync("Never Opened B");

        var feed = await GetShelvesAsync();

        Assert.Empty(feed.Shelves);
        Assert.Equal(2, feed.CandidateCount);
    }

    [Fact]
    public async Task The_shelf_feed_is_deterministic_for_identical_requests()
    {
        var stale = await _harness.SeedGameAsync("Patched Comeback", minutes: 200, lastPlayed: AsOf.AddYears(-3));
        await _harness.SeedMajorUpdateAsync(stale, AsOf.AddMonths(-1), "v2.0 Overhaul");
        for (var i = 0; i < 12; i++)
        {
            await _harness.SeedGameAsync($"Sampled {i}", minutes: 40 + i, lastPlayed: AsOf.AddYears(-2));
        }

        var first = await GetShelvesAsync();
        var second = await GetShelvesAsync();

        Assert.Equal(
            first.Shelves.Select(s => (s.Id, string.Join(",", s.Items.Select(i => i.ReleaseId)))),
            second.Shelves.Select(s => (s.Id, string.Join(",", s.Items.Select(i => i.ReleaseId)))));
    }
}
