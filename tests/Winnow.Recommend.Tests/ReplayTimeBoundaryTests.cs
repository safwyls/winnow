using Winnow.Core.Domain;
using Winnow.Core.Lifecycle;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Recommend.Tests;

public sealed class ReplayTimeBoundaryTests
{
    [Fact]
    public async Task Future_sessions_snapshot_rises_and_feedback_cannot_change_an_earlier_feed()
    {
        using var harness = new RecommendHarness();
        var game = await harness.SeedGameAsync("Past evidence", 300, RecommendHarness.AsOf.AddYears(-2));
        var request = RecommendHarness.Request();
        var before = await harness.Engine.GetFeedAsync(request);
        for (var day = 1; day < 7; day++)
        {
            await harness.SeedSessionAsync(game, RecommendHarness.AsOf.AddDays(day), attributedBy: "launch");
            await harness.SeedSnapshotAsync(game, 300 + day * 30, RecommendHarness.AsOf.AddDays(day));
        }
        await harness.Feedback.RecordSurfacedAsync([new FeedSurfacing
            { ReleaseId = game.ReleaseId, SurfacedOn = DateOnly.FromDateTime(RecommendHarness.AsOf), ShelfId = "fixture" }]);
        await harness.Feedback.RecordVerdictAsync(new FeedVerdict
            { ReleaseId = game.ReleaseId, Kind = FeedVerdictKinds.NotInterested, CreatedAt = RecommendHarness.AsOf.AddDays(1) });
        var sets = await FeedbackSets.LoadAsync(harness.Feedback, RecommendHarness.AsOf, request.Tuning);
        Assert.Empty(sets.EndorsedReleaseIds);
        Assert.Empty(sets.NotInterestedReleaseIds);
        var after = await harness.Engine.GetFeedAsync(sets.Apply(request));
        Assert.Equal(before.Tier, after.Tier);
        Assert.Equal(Assert.Single(before.Items).Score, Assert.Single(after.Items).Score);
    }

    [Fact]
    public async Task Lifecycle_classification_uses_the_request_instant_and_ignores_future_observations()
    {
        using var harness = new RecommendHarness();
        var game = await harness.SeedGameAsync("Dated lifecycle");
        await harness.Works.ApplyEnrichmentAsync(new WorkEnrichment(game.WorkId, IgdbId: 90001));
        await harness.Lifecycle.AppendAsync(new LifecycleObservation
        {
            ReleaseId = game.ReleaseId, Source = "igdb", SourceId = "90001",
            ObservedAt = RecommendHarness.AsOf.AddDays(1), Signals = new LifecycleSignals { IgdbStatus = "offline" },
        });
        var before = await harness.Engine.GetShelvesAsync(RecommendHarness.Request());
        Assert.DoesNotContain(before.Shelves, item => item.Id == ShelfIds.Derelict);
        var after = await harness.Engine.GetShelvesAsync(RecommendHarness.Request() with { AsOfUtc = RecommendHarness.AsOf.AddDays(2) });
        Assert.Contains(after.Shelves, item => item.Id == ShelfIds.Derelict);
    }

    [Fact]
    public async Task Future_patch_pairs_and_newer_play_records_cannot_change_past_buckets()
    {
        using var harness = new RecommendHarness();
        var game = await harness.SeedGameAsync("Played before", 300, RecommendHarness.AsOf.AddYears(-3));
        await harness.SeedMajorUpdateAsync(game, RecommendHarness.AsOf.AddDays(1), "Future patch");
        await harness.PlayRecords.InsertAsync(new PlayRecord
        {
            OwnershipId = game.OwnershipId, PlaytimeMinutes = 90000, Source = "later",
            ObservedAt = RecommendHarness.AsOf.AddDays(2), LastPlayedAt = RecommendHarness.AsOf.AddDays(2),
        });
        var feed = await harness.Engine.GetFeedAsync(RecommendHarness.Request());
        var item = Assert.Single(feed.Items);
        Assert.Equal(LibraryBuckets.Bounced, item.Bucket);
        Assert.DoesNotContain(item.Signals, signal => signal.Signal == SignalNames.PatchAfterDormancy);
    }

    [Fact]
    public async Task Exact_tier_aggregate_honors_the_same_history_boundary()
    {
        using var database = new TempDatabase();
        var work = await new WorkRepository(database.Factory).InsertAsync(new Work { Name = "History" });
        var release = await new ReleaseRepository(database.Factory).InsertAsync(new Release { WorkId = work, Name = "History" });
        var owner = await new OwnershipRepository(database.Factory).InsertAsync(new Ownership { ReleaseId = release, Store = "steam" });
        var snapshots = new PlaytimeSnapshotRepository(database.Factory);
        await snapshots.InsertAsync(new PlaytimeSnapshot { OwnershipId = owner, PlaytimeMinutes = 10, ObservedAt = RecommendHarness.AsOf.AddDays(-1) });
        await snapshots.InsertAsync(new PlaytimeSnapshot { OwnershipId = owner, PlaytimeMinutes = 20, ObservedAt = RecommendHarness.AsOf.AddDays(1) });
        await new SessionRepository(database.Factory).InsertAsync(new Session
            { OwnershipId = owner, StartedAt = RecommendHarness.AsOf.AddDays(1), DetectionMethod = "manual" });
        var repository = new LibraryHistoryStatsRepository(database.Factory);
        var before = await repository.GetAsync(RecommendHarness.AsOf);
        Assert.Equal(0, before.SessionCount);
        Assert.Equal(0, before.OwnershipsWithSnapshotRises);
        var after = await repository.GetAsync(RecommendHarness.AsOf.AddDays(2));
        Assert.Equal(1, after.SessionCount);
        Assert.Equal(1, after.OwnershipsWithSnapshotRises);
    }
}
