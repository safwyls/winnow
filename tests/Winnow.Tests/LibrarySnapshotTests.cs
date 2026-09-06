using System.Diagnostics;
using Dapper;
using Microsoft.Data.Sqlite;
using Winnow.Core.Repositories;
using Winnow.Core.Queries;
using Winnow.Data;
using Winnow.Data.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace Winnow.Tests;

public sealed class LibrarySnapshotTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Bulk_snapshot_matches_existing_reads_with_one_lease_and_preserves_list_order()
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 20);
        var observed = new LibraryReadTrackingFactory(db.Factory);
        var snapshot = await new LibraryQueryRepository(observed).GetSnapshotAsync(BucketThresholds.Default);
        Assert.Equal(1, observed.Leases);
        Assert.Equivalent(await new LibraryQueryRepository(db.Factory).GetOwnershipBucketsAsync(BucketThresholds.Default), snapshot.Buckets, strict: true);
        Assert.Equal(await new WorkRepository(db.Factory).GetAllAsync(), snapshot.Works);
        Assert.Equal(await new OwnershipRepository(db.Factory).GetAllAsync(), snapshot.Ownerships);
        Assert.Equal(20, snapshot.Releases.Count);
        Assert.Equal(20, snapshot.ExternalIds.Count);
        Assert.Equal([20L, 1L], snapshot.ListItems.Select(item => item.ReleaseId));
    }

    [Fact]
    public async Task Cached_library_read_measurement_compares_legacy_N_plus_one_with_bulk()
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 1000);
        // The app's pool keeps WAL handles alive between reads. Keep one idle
        // handle here so unpooled test leases do not measure WAL teardown too.
        using var keeper = db.Factory.Open();
        var observed = new LibraryReadTrackingFactory(db.Factory);
        var queries = new LibraryQueryRepository(observed);
        var works = new WorkRepository(observed);
        var ownerships = new OwnershipRepository(observed);
        var releases = new ReleaseRepository(observed);
        await queries.GetSnapshotAsync(BucketThresholds.Default);
        var legacy = Stopwatch.StartNew();
        await queries.GetOwnershipBucketsAsync(BucketThresholds.Default);
        await ownerships.GetAllAsync();
        foreach (var work in await works.GetAllAsync())
            foreach (var release in await releases.GetByWorkAsync(work.Id))
                await releases.GetExternalIdsAsync(release.Id);
        legacy.Stop();
        var legacyLeases = observed.Leases - 1;
        var bulk = Stopwatch.StartNew();
        var snapshot = await queries.GetSnapshotAsync(BucketThresholds.Default);
        bulk.Stop();
        Assert.Equal(1000, snapshot.Buckets.Count);
        Assert.Equal(2003, legacyLeases);
        Assert.Equal(2005, observed.Leases);
        output.WriteLine($"1000-game cached data read: legacy {legacy.Elapsed.TotalMilliseconds:F1} ms / {legacyLeases} leases; bulk {bulk.Elapsed.TotalMilliseconds:F1} ms / 1 lease.");
    }

}
