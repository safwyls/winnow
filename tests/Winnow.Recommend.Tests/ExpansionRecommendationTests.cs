using Winnow.Core.Identity;
using Xunit;

namespace Winnow.Recommend.Tests;

/// <summary>
/// An expansion link changes no recommendation.
///
/// <para>This is the test the design pass asked for by name. Folding an
/// unplayed pack into a played-out base game would delete what it called
/// probably the best recommendation the app can make in that situation: you
/// put two hundred hours into this and never opened the expansion. The link
/// model keeps it by construction — the engine reads the resolved work id off
/// the bucket rows, the bucket query resolves same-game links only, and
/// <c>ExpansionGrouping</c> has no resolver that could be handed to a scorer —
/// but the claim is load-bearing enough to be asserted rather than
/// reasoned about.</para>
/// </summary>
public class ExpansionRecommendationTests
{
    private static readonly DateTime Day1 = RecommendHarness.AsOf;

    [Fact]
    public async Task An_unplayed_pack_of_a_played_out_base_game_is_still_recommended()
    {
        using var harness = new RecommendHarness();

        // Played out: well past any retired floor, and long ago.
        var civ = await harness.SeedGameAsync(
            "Sid Meier's Civilization IV", minutes: 12_000, lastPlayed: Day1.AddYears(-3));

        // Never opened.
        var bts = await harness.SeedGameAsync(
            "Sid Meier's Civilization IV: Beyond the Sword", minutes: 0, lastPlayed: null);

        var before = await harness.Engine.GetFeedAsync(RecommendHarness.Request());
        Assert.Contains(before.Items, i => i.ReleaseId == bts.ReleaseId);

        await harness.Links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = civ.WorkId,
            ChildWorkIds = [bts.WorkId],
            Kind = IdentityLinkKinds.ExpansionOf,
        });

        var after = await harness.Engine.GetFeedAsync(RecommendHarness.Request());

        // Still there, and still its own candidate rather than folded into the
        // base game's.
        Assert.Contains(after.Items, i => i.ReleaseId == bts.ReleaseId);

        // And nothing else moved either: the same items, in the same order,
        // with the same scores. A same-game link legitimately changes this
        // feed; an expansion link may not.
        Assert.Equal(
            before.Items.Select(i => (i.ReleaseId, i.Score)),
            after.Items.Select(i => (i.ReleaseId, i.Score)));
    }

    /// <summary>
    /// The complement, stated separately because it is a different claim: the
    /// played-out base game is excluded by its own playtime, and grouping a
    /// pack under it does not drag the pack into that exclusion.
    /// </summary>
    [Fact]
    public async Task Grouping_does_not_lend_the_base_games_hours_to_the_pack()
    {
        using var harness = new RecommendHarness();

        var civ = await harness.SeedGameAsync(
            "Sid Meier's Civilization IV", minutes: 12_000, lastPlayed: Day1.AddYears(-3));
        var bts = await harness.SeedGameAsync(
            "Sid Meier's Civilization IV: Beyond the Sword", minutes: 0, lastPlayed: null);

        await harness.Links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = civ.WorkId,
            ChildWorkIds = [bts.WorkId],
            Kind = IdentityLinkKinds.ExpansionOf,
        });

        var feed = await harness.Engine.GetFeedAsync(RecommendHarness.Request());

        // The base game is retired and stays out; the pack is never-played and
        // stays in. Two products, two verdicts, from one grouping.
        Assert.DoesNotContain(feed.Items, i => i.ReleaseId == civ.ReleaseId);
        Assert.Contains(feed.Items, i => i.ReleaseId == bts.ReleaseId);
    }
}
