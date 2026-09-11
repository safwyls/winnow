using Winnow.Core.Domain;
using Dapper;
using Winnow.Core.Lifecycle;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

public sealed class LifecycleTests
{
    private static readonly DateTime Now = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
    private static LifecycleObservation Observation(LifecycleSignals signals, string source = "igdb", int days = 0)
        => new() { ReleaseId = 1, Source = source, ObservedAt = Now.AddDays(-days), Signals = signals };

    [Theory]
    [InlineData("cancelled", GameLifecycleStatus.Cancelled)]
    [InlineData("offline", GameLifecycleStatus.Offline)]
    [InlineData("delisted", GameLifecycleStatus.Delisted)]
    public void Explicit_status_overrides_activity(string status, GameLifecycleStatus expected)
    {
        var result = LifecycleClassifier.Classify([Observation(new() { IgdbStatus = status, CurrentPlayers = 10000 })], Now);
        Assert.Equal(expected, result.Status);
        Assert.True(result.IsDerelict);
    }

    [Fact]
    public void Stale_and_superseded_statuses_do_not_survive()
    {
        var offline = Observation(new() { IgdbStatus = "offline" }, days: 31);
        Assert.Equal(GameLifecycleStatus.Unknown, LifecycleClassifier.Classify([offline], Now).Status);
        Assert.Equal(GameLifecycleStatus.Unknown, LifecycleClassifier.Classify(
            [offline with { ObservedAt = Now.AddDays(-1) }, Observation(new() { IgdbStatus = "released" })], Now).Status);
    }

    [Fact]
    public void Store_absence_requires_historical_presence_from_same_source()
    {
        var missing = Observation(new() { StoreListed = false }, "steam");
        Assert.False(LifecycleClassifier.Classify([missing], Now).IsDerelict);
        Assert.Equal(GameLifecycleStatus.Delisted, LifecycleClassifier.Classify(
            [Observation(new() { StoreListed = true }, "steam", 4), missing], Now).Status);
        Assert.False(LifecycleClassifier.Classify(
            [Observation(new() { StoreListed = true }, "gog", 4), missing], Now).IsDerelict);
    }

    [Fact]
    public void Multiplayer_requires_sustained_samples_and_known_quiet_development()
    {
        var facts = new LifecycleSignals { IgdbStatus = "released", IsMultiplayer = true, HasSinglePlayer = false,
            CurrentPlayers = 0, RecentReviewCount = 0,
            LastDevelopmentAt = Now.AddYears(-2), LastCommunicationAt = Now.AddYears(-2) };
        var observations = new[] { Observation(facts, "steam", 20), Observation(facts, "steam", 10), Observation(facts, "steam") };
        Assert.Equal(GameLifecycleStatus.Dead, LifecycleClassifier.Classify(observations, Now).Status);
        Assert.Equal(GameLifecycleStatus.Inactive, LifecycleClassifier.Classify([observations[^1]], Now).Status);
        Assert.Equal(GameLifecycleStatus.Inactive, LifecycleClassifier.Classify(
            observations.Append(Observation(new() { HasSinglePlayer = true })), Now).Status);
        Assert.Equal(GameLifecycleStatus.Inactive, LifecycleClassifier.Classify(
            observations.Append(Observation(new() { LastDevelopmentAt = Now }, "official")), Now).Status);
        Assert.Equal(GameLifecycleStatus.Inactive, LifecycleClassifier.Classify(
            observations.Append(Observation(new() { IsUnfinished = true })), Now).Status);
    }

    [Fact]
    public void Abandonment_requires_unfinished_and_both_known_quiet_signals()
    {
        var quiet = new LifecycleSignals { IsUnfinished = true, LastDevelopmentAt = Now.AddYears(-3), LastCommunicationAt = Now.AddYears(-3) };
        Assert.Equal(GameLifecycleStatus.Abandoned, LifecycleClassifier.Classify([Observation(quiet)], Now).Status);
        Assert.False(LifecycleClassifier.Classify([Observation(quiet with { LastCommunicationAt = null })], Now).IsDerelict);
        Assert.False(LifecycleClassifier.Classify(
            [Observation(quiet), Observation(new() { LastCommunicationAt = Now }, "official")], Now).IsDerelict);
        Assert.False(LifecycleClassifier.Classify([Observation(quiet with { LastStoreChangeAt = Now })], Now).IsDerelict);
    }

    [Fact]
    public async Task Evidence_roundtrips_and_bucket_preserves_viable_sibling()
    {
        using var db = new TempDatabase();
        var work = await new WorkRepository(db.Factory).InsertAsync(new Work { Name = "Game", IgdbId = 100 });
        var releases = new ReleaseRepository(db.Factory);
        var release = await releases.InsertAsync(new Release { WorkId = work, Name = "Steam" });
        var ownerships = new OwnershipRepository(db.Factory);
        await ownerships.InsertAsync(new Ownership { ReleaseId = release, Store = "steam" });
        var repository = new LifecycleRepository(db.Factory);
        var observation = Observation(new() { IgdbStatus = "offline", OfficialShutdownAt = Now })
            with { ReleaseId = release, SourceId = "100", ObservedAt = DateTime.UtcNow, RawJson = "{\"status\":6}" };
        await repository.AppendAsync(observation);
        var stored = Assert.Single(await repository.GetForReleaseAsync(release));
        Assert.Equal(observation.Signals, stored.Signals);
        Assert.Equal(observation.RawJson, stored.RawJson);
        var query = new LibraryQueryRepository(db.Factory);
        var bucket = Assert.Single(await query.GetOwnershipBucketsAsync(BucketThresholds.Default));
        Assert.Equal(LibraryBuckets.Derelict, bucket.Game.Bucket);
        Assert.Equal(GameLifecycleStatus.Offline, bucket.Lifecycle!.Status);
        var sibling = await releases.InsertAsync(new Release { WorkId = work, Name = "GOG" });
        await ownerships.InsertAsync(new Ownership { ReleaseId = sibling, Store = "gog" });
        var rows = await query.GetOwnershipBucketsAsync(BucketThresholds.Default);
        Assert.All(rows, r => Assert.Equal(LibraryBuckets.NeverPlayed, r.Game.Bucket));
        Assert.Equal(LibraryBuckets.Derelict, rows.Single(r => r.ReleaseId == release).Bucket);
        var snapshot = await query.GetSnapshotAsync(BucketThresholds.Default);
        Assert.Equal(rows.Select(r => r.Bucket), snapshot.Buckets.Select(r => r.Bucket));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.AppendAsync(observation with
        { Signals = observation.Signals with { LastDevelopmentAt = DateTime.SpecifyKind(Now, DateTimeKind.Unspecified) } }));
        Assert.Single(await repository.GetAllAsync());
    }

    [Fact]
    public async Task Correcting_igdb_identity_excludes_old_evidence_without_deleting_it()
    {
        using var db = new TempDatabase();
        var work = await new WorkRepository(db.Factory).InsertAsync(new Work { Name = "Game", IgdbId = 100 });
        var release = await new ReleaseRepository(db.Factory).InsertAsync(new Release { WorkId = work, Name = "Game" });
        await new OwnershipRepository(db.Factory).InsertAsync(new Ownership { ReleaseId = release, Store = "steam" });
        var repository = new LifecycleRepository(db.Factory);
        await repository.AppendAsync(Observation(new() { IgdbStatus = "offline" }) with
        { ReleaseId = release, SourceId = "100", ObservedAt = DateTime.UtcNow });
        var query = new LibraryQueryRepository(db.Factory);
        Assert.Equal(LibraryBuckets.Derelict,
            Assert.Single(await query.GetOwnershipBucketsAsync(BucketThresholds.Default)).Game.Bucket);
        using (var connection = db.Factory.Open())
        {
            await connection.ExecuteAsync("UPDATE works SET igdb_id = 200 WHERE id = @work", new { work });
        }
        Assert.Equal(LibraryBuckets.NeverPlayed,
            Assert.Single(await query.GetOwnershipBucketsAsync(BucketThresholds.Default)).Game.Bucket);
        Assert.Empty(await repository.GetAllAsync());
        using var check = db.Factory.Open();
        Assert.Equal(1, check.ExecuteScalar<int>("SELECT COUNT(*) FROM lifecycle_observations;"));
    }
}
