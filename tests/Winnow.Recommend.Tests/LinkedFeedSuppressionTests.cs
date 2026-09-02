using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Xunit;

namespace Winnow.Recommend.Tests;

/// <summary>
/// Dismissing one store entry of a linked game suppresses the other.
/// <c>feed_verdicts</c> is keyed by release, and without this the feed
/// would offer the same game twice under two store badges — the user says
/// "not interested" to Prey on Steam and Prey on Epic arrives tomorrow.
/// Nothing is written to achieve it: the stored fact stays the release the
/// user clicked, and the widening to the resolved work is a query
/// recomputed per request, which is the derived/truth split the whole
/// design rests on. The engine mentions no link table: it reads the
/// resolved work id off the rows the bucket query already returned, so the
/// feed and the grid cannot disagree about what one game is.
/// </summary>
public class LinkedFeedSuppressionTests
{
    private static readonly DateTime Day1 = RecommendHarness.AsOf;

    private static async Task<RecommendationRequest> RequestWithFeedbackAsync(
        RecommendHarness harness, DateTime asOf)
    {
        var sets = await FeedbackSets.LoadAsync(
            harness.Feedback, asOf, RecommendationTuning.Default);
        return sets.Apply(RecommendHarness.Request() with { AsOfUtc = asOf });
    }

    [Fact]
    public async Task Dismissing_one_store_entry_of_a_linked_game_suppresses_the_other()
    {
        using var harness = new RecommendHarness();
        var kept = await harness.SeedGameAsync("Kept", minutes: 300, lastPlayed: Day1.AddYears(-3));
        var steam = await harness.SeedGameAsync("Prey", minutes: 300, lastPlayed: Day1.AddYears(-3));
        var epic = await harness.SeedGameAsync(
            "Prey", minutes: 200, lastPlayed: Day1.AddYears(-3), store: "epic");

        // Unlinked, they are two games, and dismissing one leaves the other.
        await harness.Feedback.RecordVerdictAsync(new FeedVerdict
        {
            ReleaseId = steam.ReleaseId,
            Kind = FeedVerdictKinds.NotInterested,
            CreatedAt = Day1,
        });

        var beforeLink = await harness.Engine.GetFeedAsync(
            await RequestWithFeedbackAsync(harness, Day1));
        Assert.DoesNotContain(beforeLink.Items, i => i.ReleaseId == steam.ReleaseId);
        Assert.Contains(beforeLink.Items, i => i.ReleaseId == epic.ReleaseId);

        // Linked, they are one game, and the one dismissal covers both.
        await harness.Links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = steam.WorkId,
            ChildWorkIds = [epic.WorkId],
        });

        var afterLink = await harness.Engine.GetFeedAsync(
            await RequestWithFeedbackAsync(harness, Day1));
        Assert.DoesNotContain(afterLink.Items, i => i.ReleaseId == steam.ReleaseId);
        Assert.DoesNotContain(afterLink.Items, i => i.ReleaseId == epic.ReleaseId);
        Assert.Contains(afterLink.Items, i => i.ReleaseId == kept.ReleaseId);
    }

    /// <summary>
    /// The dismissal was made on the Steam entry, but the suppression is
    /// about the game, so dismissing the CHILD suppresses the parent just as
    /// much. The relation is not directional in the feed.
    /// </summary>
    [Fact]
    public async Task Dismissing_the_linked_child_suppresses_the_parent_too()
    {
        using var harness = new RecommendHarness();
        var steam = await harness.SeedGameAsync("Prey", minutes: 300, lastPlayed: Day1.AddYears(-3));
        var epic = await harness.SeedGameAsync(
            "Prey", minutes: 200, lastPlayed: Day1.AddYears(-3), store: "epic");

        await harness.Links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = steam.WorkId,
            ChildWorkIds = [epic.WorkId],
        });

        await harness.Feedback.RecordVerdictAsync(new FeedVerdict
        {
            ReleaseId = epic.ReleaseId,
            Kind = FeedVerdictKinds.NotInterested,
            CreatedAt = Day1,
        });

        var feed = await harness.Engine.GetFeedAsync(await RequestWithFeedbackAsync(harness, Day1));
        Assert.DoesNotContain(feed.Items, i => i.ReleaseId == steam.ReleaseId);
        Assert.DoesNotContain(feed.Items, i => i.ReleaseId == epic.ReleaseId);
    }

    /// <summary>
    /// Retracting the link restores the pre-link behaviour on the very next
    /// feed, with no write anywhere. The stored verdict never moved.
    /// </summary>
    [Fact]
    public async Task Retracting_the_link_readmits_the_other_entry_with_no_write()
    {
        using var harness = new RecommendHarness();
        var steam = await harness.SeedGameAsync("Prey", minutes: 300, lastPlayed: Day1.AddYears(-3));
        var epic = await harness.SeedGameAsync(
            "Prey", minutes: 200, lastPlayed: Day1.AddYears(-3), store: "epic");

        await harness.Links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = steam.WorkId,
            ChildWorkIds = [epic.WorkId],
        });

        await harness.Feedback.RecordVerdictAsync(new FeedVerdict
        {
            ReleaseId = steam.ReleaseId,
            Kind = FeedVerdictKinds.NotInterested,
            CreatedAt = Day1,
        });

        Assert.DoesNotContain(
            (await harness.Engine.GetFeedAsync(await RequestWithFeedbackAsync(harness, Day1))).Items,
            i => i.ReleaseId == epic.ReleaseId);

        await harness.Links.RetractLinkAsync(epic.WorkId);

        var restored = await harness.Engine.GetFeedAsync(
            await RequestWithFeedbackAsync(harness, Day1));
        Assert.Contains(restored.Items, i => i.ReleaseId == epic.ReleaseId);
        Assert.DoesNotContain(restored.Items, i => i.ReleaseId == steam.ReleaseId);
    }
}
