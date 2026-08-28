using Winnow.Core.Domain;
using Xunit;

namespace Winnow.Recommend.Tests;

/// <summary>
/// The feedback loop end to end, minus the UI: verdicts stored, sets loaded,
/// feeds changed — and every change reversible by a revocation or a lapse,
/// never by editing history. The rotation test is the load-bearing one: it
/// proves the cross-day memory rotates the feed where the day-seeded jitter
/// alone (seed pinned) provably does not.
/// </summary>
public class FeedbackLoopTests
{
    private static readonly DateTime Day1 = RecommendHarness.AsOf;
    private static readonly DateTime Day2 = RecommendHarness.AsOf.AddDays(1);

    private static async Task<RecommendationRequest> RequestWithFeedbackAsync(
        RecommendHarness harness, DateTime asOf, int maxResults = 20)
    {
        var sets = await FeedbackSets.LoadAsync(
            harness.Feedback, asOf, RecommendationTuning.Default);
        return sets.Apply(RecommendHarness.Request(maxResults) with { AsOfUtc = asOf });
    }

    [Fact]
    public async Task Dismissal_excludes_the_game_and_undo_restores_it()
    {
        using var harness = new RecommendHarness();
        var kept = await harness.SeedGameAsync("Kept", minutes: 300, lastPlayed: Day1.AddYears(-3));
        var dismissed = await harness.SeedGameAsync("Dismissed", minutes: 300, lastPlayed: Day1.AddYears(-3));

        await harness.Feedback.RecordVerdictAsync(new FeedVerdict
        {
            ReleaseId = dismissed.ReleaseId,
            Kind = FeedVerdictKinds.NotInterested,
            CreatedAt = Day1,
        });

        var feed = await harness.Engine.GetFeedAsync(await RequestWithFeedbackAsync(harness, Day1));
        Assert.Contains(feed.Items, i => i.ReleaseId == kept.ReleaseId);
        Assert.DoesNotContain(feed.Items, i => i.ReleaseId == dismissed.ReleaseId);

        // The undo: a revocation stamp, not a deletion — and the very next
        // feed readmits the game with no other write anywhere.
        await harness.Feedback.RevokeVerdictsAsync(
            dismissed.ReleaseId, FeedVerdictKinds.NotInterested, Day1.AddHours(1));

        var restored = await harness.Engine.GetFeedAsync(await RequestWithFeedbackAsync(harness, Day1));
        Assert.Contains(restored.Items, i => i.ReleaseId == dismissed.ReleaseId);
    }

    [Fact]
    public async Task Snooze_excludes_until_it_lapses_then_the_game_returns_by_itself()
    {
        using var harness = new RecommendHarness();
        await harness.SeedGameAsync("Kept", minutes: 300, lastPlayed: Day1.AddYears(-3));
        var snoozed = await harness.SeedGameAsync("Snoozed", minutes: 300, lastPlayed: Day1.AddYears(-3));

        await harness.Feedback.RecordVerdictAsync(new FeedVerdict
        {
            ReleaseId = snoozed.ReleaseId,
            Kind = FeedVerdictKinds.Snoozed,
            CreatedAt = Day1,
            ExpiresAt = Day1 + FeedVerdictKinds.DefaultSnooze,
        });

        var during = await harness.Engine.GetFeedAsync(await RequestWithFeedbackAsync(harness, Day1));
        Assert.DoesNotContain(during.Items, i => i.ReleaseId == snoozed.ReleaseId);

        // "Not now" is not "never": past the expiry the game is back, and
        // nothing wrote anything to bring it back — lapse is a read-time fact.
        var after = await harness.Engine.GetFeedAsync(
            await RequestWithFeedbackAsync(harness, Day1 + FeedVerdictKinds.DefaultSnooze + TimeSpan.FromDays(1)));
        Assert.Contains(after.Items, i => i.ReleaseId == snoozed.ReleaseId);
    }

    [Fact]
    public async Task A_verdict_on_one_release_excludes_the_whole_work()
    {
        using var harness = new RecommendHarness();
        await harness.SeedGameAsync("Kept", minutes: 300, lastPlayed: Day1.AddYears(-3));

        // The post-merge shape: one work, two releases (Steam and GOG copies
        // confirmed as the same game). The user dismisses the card they see —
        // which carries ONE release id — and the other copy must not
        // resurface the same game tomorrow.
        var steam = await harness.SeedGameAsync("Twice Bought", minutes: 300, lastPlayed: Day1.AddYears(-3));
        var gogReleaseId = await harness.Releases.InsertAsync(new Release
        {
            WorkId = steam.WorkId,
            Name = "Twice Bought",
            Platform = "windows",
        });
        var gogOwnershipId = await harness.Ownerships.InsertAsync(new Ownership
        {
            ReleaseId = gogReleaseId,
            Store = "gog",
        });
        await harness.PlayRecords.InsertAsync(new PlayRecord
        {
            OwnershipId = gogOwnershipId,
            PlaytimeMinutes = 0,
            Source = "gog_local",
            ObservedAt = Day1.AddDays(-1),
        });

        await harness.Feedback.RecordVerdictAsync(new FeedVerdict
        {
            ReleaseId = steam.ReleaseId,
            Kind = FeedVerdictKinds.NotInterested,
            CreatedAt = Day1,
        });

        var feed = await harness.Engine.GetFeedAsync(await RequestWithFeedbackAsync(harness, Day1));
        Assert.DoesNotContain(feed.Items, i => i.WorkId == steam.WorkId);
    }

    [Fact]
    public async Task Todays_surfacings_never_penalise_todays_own_feed()
    {
        using var harness = new RecommendHarness();
        for (var i = 0; i < 8; i++)
        {
            await harness.SeedGameAsync($"Game {(char)('A' + i)}", installed: true);
        }

        var request = await RequestWithFeedbackAsync(harness, Day1);
        var first = await harness.Engine.GetShelvesAsync(request with { MaxPerShelf = 6 });
        await harness.Feedback.RecordSurfacedAsync(FeedbackSets.SurfacingsOf(first, Day1));

        // The afternoon refresh: the sets are reloaded, and this morning's
        // picks are in the log — but the log's same-day rows must not reach
        // the recently-surfaced set, or the refresh would deal the new hand
        // the day-seeded shuffle exists to prevent.
        var reloaded = await FeedbackSets.LoadAsync(
            harness.Feedback, Day1.AddHours(5), RecommendationTuning.Default);
        Assert.Empty(reloaded.RecentlySurfacedReleaseIds);

        var second = await harness.Engine.GetShelvesAsync(
            reloaded.Apply(RecommendHarness.Request() with { AsOfUtc = Day1.AddHours(5), MaxPerShelf = 6 }));
        Assert.Equal(
            first.Shelves.Single().Items.Select(i => i.ReleaseId),
            second.Shelves.Single().Items.Select(i => i.ReleaseId));
    }

    [Fact]
    public async Task Rotation_comes_from_the_surfacing_memory_not_from_jitter()
    {
        using var harness = new RecommendHarness();

        // Twelve indistinguishable candidates for a six-slot shelf: installed,
        // never opened, no facets — every score identical but for jitter.
        // Names are single distinct words so the franchise grouper cannot
        // collapse any of them into one family.
        string[] names =
        [
            "Aurora", "Basilisk", "Cinder", "Dredge", "Ember", "Foxglove",
            "Gossamer", "Harrow", "Islet", "Juniper", "Kestrel", "Lantern",
        ];
        var byRelease = new Dictionary<long, string>();
        foreach (var name in names)
        {
            var game = await harness.SeedGameAsync(name, installed: true);
            byRelease[game.ReleaseId] = name;
        }

        var day1Feed = await harness.Engine.GetShelvesAsync(
            (await RequestWithFeedbackAsync(harness, Day1)) with { MaxPerShelf = 6 });
        var day1Items = day1Feed.Shelves.Single().Items.Select(i => i.ReleaseId).ToHashSet();
        Assert.Equal(6, day1Items.Count);

        await harness.Feedback.RecordSurfacedAsync(FeedbackSets.SurfacingsOf(day1Feed, Day1));

        // ── Control: day 2 with the seed pinned and NO memory ──────────────
        // The harness pins ShuffleSeed, so the jitter cannot rotate anything:
        // the same six come back. This is the M8 gap made visible — before
        // this change, cross-day rotation depended entirely on the seed
        // happening to change.
        var day2Amnesiac = await harness.Engine.GetShelvesAsync(
            RecommendHarness.Request() with { AsOfUtc = Day2, MaxPerShelf = 6 });
        Assert.Equal(
            day1Items,
            day2Amnesiac.Shelves.Single().Items.Select(i => i.ReleaseId).ToHashSet());

        // ── Day 2 with the memory: yesterday's six all rotate out ──────────
        // Same pinned seed, so the ONLY difference is the recently-surfaced
        // set loaded from the log. Among near-ties the -0.20 penalty is
        // decisive, so the six unshown games take the shelf: full rotation,
        // and in two days the whole pool has had its turn.
        var day2Feed = await harness.Engine.GetShelvesAsync(
            (await RequestWithFeedbackAsync(harness, Day2)) with { MaxPerShelf = 6 });
        var day2Items = day2Feed.Shelves.Single().Items.Select(i => i.ReleaseId).ToHashSet();

        Assert.Equal(6, day2Items.Count);
        Assert.Empty(day1Items.Intersect(day2Items));
        Assert.Equal(byRelease.Keys.ToHashSet(), day1Items.Union(day2Items).ToHashSet());
    }

    [Fact]
    public async Task A_launch_off_the_feed_lets_the_game_testify_to_taste()
    {
        using var harness = new RecommendHarness();

        // No committed games anywhere: the taste profile starts empty, which
        // isolates the endorsement as the only possible source of testimony.
        var sampled = await harness.SeedGameAsync(
            "Sampled Off The Feed", minutes: 40, lastPlayed: Day1.AddDays(-1));
        await harness.SeedGenreAsync(sampled, "Roguelike");

        var kindred = await harness.SeedGameAsync("Kindred Sealed");
        await harness.SeedGenreAsync(kindred, "Roguelike");

        var unrelated = await harness.SeedGameAsync("Unrelated Sealed");
        await harness.SeedGenreAsync(unrelated, "Farming");

        // The feed surfaced the sampled game yesterday, and the user answered
        // by clicking Play inside Winnow that evening: the endorsement.
        await harness.Feedback.RecordSurfacedAsync(
        [
            new FeedSurfacing
            {
                ReleaseId = sampled.ReleaseId,
                SurfacedOn = DateOnly.FromDateTime(Day1.AddDays(-1)),
                ShelfId = ShelfIds.OnYourTaste,
            },
        ]);
        await harness.SeedSessionAsync(
            sampled, Day1.AddDays(-1).AddHours(8), durationMinutes: 40,
            attributedBy: SessionAttributions.Launch);

        // Control first: without the feedback sets, 40 sub-refund minutes are
        // silent (§6.1: under the refund line the game was never really
        // played) and NOTHING in the library carries a taste signal.
        var deafFeed = await harness.Engine.GetFeedAsync(RecommendHarness.Request() with { AsOfUtc = Day1 });
        Assert.All(deafFeed.Items, i =>
            Assert.DoesNotContain(i.Signals, s => s.Signal == SignalNames.TasteAffinity));

        var sets = await FeedbackSets.LoadAsync(harness.Feedback, Day1, RecommendationTuning.Default);
        Assert.Contains(sampled.ReleaseId, sets.EndorsedReleaseIds);

        // With the endorsement, the 40 minutes testify: the sealed roguelike
        // now carries a taste signal the sealed farming game does not, and
        // outranks it — the feed learned from behaviour, no thumbs-up asked.
        var feed = await harness.Engine.GetFeedAsync(
            sets.Apply(RecommendHarness.Request() with { AsOfUtc = Day1 }));

        var kindredItem = feed.Items.Single(i => i.ReleaseId == kindred.ReleaseId);
        var unrelatedItem = feed.Items.Single(i => i.ReleaseId == unrelated.ReleaseId);
        Assert.Contains(kindredItem.Signals, s => s.Signal == SignalNames.TasteAffinity);
        Assert.DoesNotContain(unrelatedItem.Signals, s => s.Signal == SignalNames.TasteAffinity);
        Assert.True(kindredItem.Score > unrelatedItem.Score);
    }
}
