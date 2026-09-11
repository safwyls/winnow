using Dapper;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

public sealed class ActivityRepositoryTests
{
    private static readonly DateTime Start = new(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(ActivitySection.Sessions)]
    [InlineData(ActivitySection.Journal)]
    public async Task Paging_preserves_same_time_rows_and_exact_notes_with_visibility_and_period_bounds(ActivitySection section)
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        using (var connection = db.Factory.Open()) connection.Execute("""
            WITH RECURSIVE seq(id) AS (SELECT 1 UNION ALL SELECT id+1 FROM seq WHERE id<125)
            INSERT INTO sessions(id,ownership_id,started_at,detection_method)
            SELECT id,1,@Start,'manual' FROM seq;
            INSERT INTO session_notes(session_id,note,rating) SELECT id,NULL,4 FROM sessions;
            UPDATE sessions SET attributed_by='launch',monitor_key='sitting-' || id;
            INSERT INTO sessions(id,ownership_id,started_at,detection_method)
            VALUES(126,2,@Start,'manual'),(127,1,@End,'manual'),(128,1,@Before,'manual');
            INSERT INTO session_notes(session_id,note,rating) VALUES(126,'Hidden game',5),(127,'Next week',5),(128,'Last week',5);
            """, new { Start, End = Start.AddDays(7), Before = Start.AddTicks(-1) });
        var tracking = new LibraryReadTrackingFactory(db.Factory);
        var repository = new ActivityRepository(tracking);
        var ids = new List<long>();
        ActivityCursor? cursor = null;
        do
        {
            var page = await repository.GetPageAsync([1], Start, Start.AddDays(7), section, cursor);
            Assert.InRange(page.Rows.Count, 1, 50);
            Assert.All(page.Rows, row =>
            {
                Assert.Equal(1, row.OwnershipId);
                Assert.Equal(4, row.Note?.Rating);
                Assert.Null(row.Session!.EndedAt);
                Assert.Equal("launch", row.Session.AttributedBy);
                Assert.Equal($"sitting-{row.Session.Id}", row.Session.MonitorKey);
            });
            ids.AddRange(page.Rows.Select(row => row.Session!.Id));
            cursor = page.Next;
        } while (cursor is not null);
        Assert.Equal(Enumerable.Range(1, 125).Reverse().Select(n => (long)n), ids);
        Assert.Equal(3, tracking.Leases);
    }

    [Fact]
    public async Task Update_pages_deduplicate_shared_release_and_do_not_leak_hidden_ownerships_or_raw_payloads()
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        using (var connection = db.Factory.Open()) connection.Execute("""
            INSERT INTO ownerships(id,release_id,store,installed) VALUES(3,1,'gog',0);
            INSERT INTO update_events(id,release_id,kind,occurred_at,title,raw_json)
            VALUES(1,1,'announcement',@Start,'Visible','{"large":"not needed"}'),
                  (2,2,'announcement',@Start,'Hidden','{}');
            """, new { Start });
        var repository = new ActivityRepository(db.Factory);
        var page = await repository.GetPageAsync([1, 3], Start, Start.AddDays(7), ActivitySection.Updates);
        var row = Assert.Single(page.Rows);
        Assert.Equal("Visible", row.Update!.Title);
        Assert.Null(row.Update.RawJson);
        Assert.Equal(1, row.OwnershipId);
        Assert.Null(page.Next);
        Assert.Equal(3, Assert.Single((await repository.GetPageAsync([3], Start, Start.AddDays(7), ActivitySection.Updates)).Rows).OwnershipId);
        Assert.Empty((await repository.GetPageAsync([], Start, Start.AddDays(7), ActivitySection.Updates)).Rows);
    }

    [Fact]
    public async Task Journal_filters_before_paging_and_large_visibility_sets_do_not_hit_SQLite_parameter_limits()
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        using (var connection = db.Factory.Open()) connection.Execute("""
            WITH RECURSIVE seq(id) AS (SELECT 1 UNION ALL SELECT id+1 FROM seq WHERE id<100)
            INSERT INTO sessions(id,ownership_id,started_at,detection_method) SELECT id,1,@Start,'manual' FROM seq;
            INSERT INTO session_notes(session_id,note,rating) VALUES(1,'Oldest note',NULL),(2,'  ',NULL),(3,NULL,5);
            """, new { Start });
        var repository = new ActivityRepository(db.Factory);
        var page = await repository.GetPageAsync(Enumerable.Range(1, 40_000).Select(n => (long)n).ToArray(),
            Start, Start.AddDays(7), ActivitySection.Journal, pageSize: 1);
        Assert.Equal(3, Assert.Single(page.Rows).Session!.Id);
        var next = await repository.GetPageAsync([1], Start, Start.AddDays(7), ActivitySection.Journal, page.Next, pageSize: 1);
        Assert.Equal("Oldest note", Assert.Single(next.Rows).Note!.Note);
        Assert.Null(next.Next);
    }
}
