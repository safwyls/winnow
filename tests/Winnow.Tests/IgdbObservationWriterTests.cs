using Dapper;
using Microsoft.Data.Sqlite;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

public sealed class IgdbObservationWriterTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Callback_repositories_share_the_guard_transaction_and_failed_operations_cannot_be_committed(bool ambient, bool cancellation)
    {
        using var db = new TempDatabase();
        var workId = await new WorkRepository(db.Factory).InsertAsync(new() { Name = "Before", IgdbId = 111 });
        var writer = new IgdbObservationWriter(db.Factory);
        var mapping = (await writer.CaptureAsync(workId))!;
        var fields = new WorkFieldSourceRepository(db.Factory);
        var facets = new FacetRepository(db.Factory);
        using var cancel = new CancellationTokenSource();
        using (var outer = ambient ? db.Factory.Begin() : null)
        {
            var operation = writer.TryWriteAsync(mapping, async ct =>
            {
                await fields.SetFieldAsync(workId, WorkFields.Name, "Must roll back", ct);
                await facets.SetWorkFacetsAsync(workId, [new(FacetKinds.Genre, "Must roll back")], ct);
                if (cancellation) cancel.Cancel();
                else throw new InvalidOperationException("Injected persistence failure");
            }, cancel.Token);
            if (cancellation) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
            else await Assert.ThrowsAsync<InvalidOperationException>(() => operation);
            await new SettingsRepository(db.Factory).SetAsync("other", "saved");
            outer?.Commit();
        }
        Assert.Equal("Before", (await new WorkRepository(db.Factory).GetAsync(workId))!.Name);
        Assert.Equal("saved", await new SettingsRepository(db.Factory).GetAsync("other"));
        using var check = db.Factory.Open();
        Assert.Equal(0, check.ExecuteScalar<int>("SELECT COUNT(*) FROM work_field_sources;"));
        Assert.Equal(0, check.ExecuteScalar<int>("SELECT COUNT(*) FROM work_facets;"));
        Assert.Equal(0, check.ExecuteScalar<int>("SELECT COUNT(*) FROM facets WHERE name = 'Must roll back';"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_successful_callback_keeps_the_ambient_callers_commit_decision(bool commit)
    {
        using var db = new TempDatabase();
        var works = new WorkRepository(db.Factory);
        var workId = await works.InsertAsync(new() { Name = "Before", IgdbId = 111 });
        var writer = new IgdbObservationWriter(db.Factory);
        var mapping = (await writer.CaptureAsync(workId))!;
        using (var scope = db.Factory.Begin())
        {
            Assert.True(await writer.TryWriteAsync(mapping,
                ct => new WorkFieldSourceRepository(db.Factory).SetFieldAsync(workId, WorkFields.Name, "After", ct)));
            if (commit) scope.Commit();
        }
        Assert.Equal(commit ? "After" : "Before", (await works.GetAsync(workId))!.Name);
    }

    [Fact]
    public async Task The_guard_holds_the_write_boundary_until_all_repository_callbacks_finish()
    {
        using var db = new TempDatabase();
        var workId = await new WorkRepository(db.Factory).InsertAsync(new() { Name = "Before", IgdbId = 111 });
        var writer = new IgdbObservationWriter(db.Factory);
        var mapping = (await writer.CaptureAsync(workId))!;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writing = Task.Run(() => writer.TryWriteAsync(mapping, async ct =>
        {
            await new FacetRepository(db.Factory).SetWorkFacetsAsync(workId, [new(FacetKinds.Genre, "Old")], ct);
            entered.SetResult();
            await release.Task.WaitAsync(ct);
            await new WorkMaturityRepository(db.Factory).UpsertAsync(new() { WorkId = workId, Source = "igdb", Ratings = "esrb:ao", ObservedAt = DateTime.UtcNow }, ct);
        }));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var pinning = Task.Run(async () =>
        {
            secondStarted.SetResult();
            return await new WorkIgdbPinRepository(db.Factory).PinAsync(new() { WorkId = workId, IgdbId = 222, Name = "After" });
        });
        try
        {
            await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(pinning.IsCompleted);
        }
        finally { release.TrySetResult(); }
        Assert.True(await writing.WaitAsync(TimeSpan.FromSeconds(10)));
        await pinning.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(await new WorkMaturityRepository(db.Factory).GetForWorkAsync(workId));
        using var check = db.Factory.Open();
        Assert.Equal(0, check.ExecuteScalar<int>("SELECT COUNT(*) FROM work_facets;"));
    }

    [Theory]
    [InlineData("work_facets")]
    [InlineData("work_maturity")]
    [InlineData("work_images")]
    [InlineData("work_ratings")]
    public async Task Projection_invalidation_failure_rolls_back_the_mapping_and_all_prior_deletions(string table)
    {
        using var db = new TempDatabase();
        var workId = await new WorkRepository(db.Factory).InsertAsync(new() { Name = "Before", IgdbId = 111 });
        await new FacetRepository(db.Factory).SetWorkFacetsAsync(workId, [new(FacetKinds.Genre, "Old")]);
        using (var seed = db.Factory.Open())
        {
            seed.Execute("INSERT INTO work_maturity(work_id,source,ratings,observed_at) VALUES(@workId,'igdb','esrb:ao','2026-09-11'); INSERT INTO work_images(work_id,source,kind,image_ids,observed_at) VALUES(@workId,'igdb','artwork','old','2026-09-11'); INSERT INTO work_ratings(work_id,source,score,rating_count,observed_at) VALUES(@workId,'igdb_users',70,10,'2026-09-11');", new { workId });
            seed.Execute($"CREATE TRIGGER fail_invalidation BEFORE DELETE ON {table} BEGIN SELECT RAISE(ABORT,'injected invalidation failure'); END;");
        }
        await Assert.ThrowsAsync<SqliteException>(() => new WorkIgdbPinRepository(db.Factory).PinAsync(new() { WorkId = workId, IgdbId = 222, Name = "After" }));
        var work = (await new WorkRepository(db.Factory).GetAsync(workId))!;
        Assert.Equal(111, work.IgdbId);
        Assert.Equal(0, work.IgdbMappingRevision);
        using var check = db.Factory.Open();
        foreach (var retained in new[] { "work_facets", "work_maturity", "work_images", "work_ratings" })
            Assert.Equal(1, check.ExecuteScalar<int>($"SELECT COUNT(*) FROM {retained};"));
    }
}
