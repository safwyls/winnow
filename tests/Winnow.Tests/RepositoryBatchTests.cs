using Dapper;
using Microsoft.Data.Sqlite;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

public sealed class RepositoryBatchTests
{
    [Theory]
    [InlineData("work_facets", false)]
    [InlineData("work_facets", true)]
    [InlineData("release_facets", false)]
    [InlineData("release_facets", true)]
    [InlineData("list_items", false)]
    [InlineData("list_items", true)]
    [InlineData("feed_surfacings", false)]
    [InlineData("feed_surfacings", true)]
    public async Task A_failed_batch_leaves_no_partial_state_even_if_the_caller_commits(string table, bool ambient)
    {
        using var db = new TempDatabase();
        Seed(db);
        var before = Snapshot(db, table);
        using (var connection = db.Factory.Open())
        {
            var trigger = table switch
            {
                "work_facets" or "release_facets" => $"""
                    CREATE TRIGGER fail_batch BEFORE INSERT ON {table}
                    BEGIN SELECT RAISE(ABORT, 'injected failure after facet deletion'); END;
                    """,
                "list_items" => """
                    CREATE TRIGGER fail_batch BEFORE UPDATE ON list_items WHEN NEW.position = 1
                    BEGIN SELECT RAISE(ABORT, 'injected failure after first position'); END;
                    """,
                _ => """
                    CREATE TRIGGER fail_batch BEFORE INSERT ON feed_surfacings WHEN NEW.release_id = 2
                    BEGIN SELECT RAISE(ABORT, 'injected failure after first surfacing'); END;
                    """,
            };
            connection.Execute(trigger);
        }

        using (var outer = ambient ? db.Factory.Begin() : null)
        {
            if (ambient)
            {
                await new SettingsRepository(db.Factory).SetAsync("before_batch", "preserved");
            }

            await Assert.ThrowsAsync<SqliteException>(() => WriteBatch(db, table));
            // Catching a repository failure must not allow its partial writes to escape.
            outer?.Commit();
        }

        Assert.Equal(before, Snapshot(db, table));
        if (ambient)
        {
            Assert.Equal("preserved", await new SettingsRepository(db.Factory).GetAsync("before_batch"));
        }
    }

    [Theory]
    [InlineData("work_facets", false)]
    [InlineData("work_facets", true)]
    [InlineData("release_facets", false)]
    [InlineData("release_facets", true)]
    [InlineData("list_items", false)]
    [InlineData("list_items", true)]
    [InlineData("feed_surfacings", false)]
    [InlineData("feed_surfacings", true)]
    public async Task A_successful_batch_respects_the_outer_commit_decision(string table, bool commit)
    {
        using var db = new TempDatabase();
        Seed(db);
        var before = Snapshot(db, table);
        using (var outer = db.Factory.Begin())
        {
            await WriteBatch(db, table);
            if (commit)
            {
                outer.Commit();
            }
        }

        var after = Snapshot(db, table);
        if (commit)
        {
            Assert.NotEqual(before, after);
        }
        else
        {
            Assert.Equal(before, after);
        }
    }

    [Fact]
    public async Task Cancellation_during_surfacing_enumeration_rolls_back_the_first_write()
    {
        using var db = new TempDatabase();
        Seed(db);
        using var cancellation = new CancellationTokenSource();
        var rows = new CancellingSurfacings(cancellation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new FeedFeedbackRepository(db.Factory).RecordSurfacedAsync(rows, cancellation.Token));
        Assert.Empty(await new FeedFeedbackRepository(db.Factory).GetSurfacedSinceAsync(DateOnly.MinValue));
    }

    private static async Task WriteBatch(TempDatabase db, string table)
    {
        var facets = new[] { new FacetAssignment(FacetKinds.Genre, "New facet", Rank: 1) };
        switch (table)
        {
            case "work_facets":
                await new FacetRepository(db.Factory).SetWorkFacetsAsync(1, facets);
                break;
            case "release_facets":
                await new FacetRepository(db.Factory).SetReleaseFacetsAsync(1, facets);
                break;
            case "list_items":
                await new GameListRepository(db.Factory).ReorderAsync(1, [2, 1]);
                break;
            default:
                await new FeedFeedbackRepository(db.Factory).RecordSurfacedAsync([Surfacing(1), Surfacing(2)]);
                break;
        }
    }

    private static FeedSurfacing Surfacing(long releaseId) => new()
    {
        ReleaseId = releaseId,
        SurfacedOn = new DateOnly(2026, 9, 6),
        ShelfId = "ready_to_play",
    };

    private static void Seed(TempDatabase db)
    {
        using var connection = db.Factory.Open();
        connection.Execute("""
            INSERT INTO works (id, name) VALUES (1, 'Synthetic game');
            INSERT INTO releases (id, work_id, name) VALUES (1, 1, 'First'), (2, 1, 'Second');
            INSERT INTO lists (id, name, is_smart) VALUES (1, 'Synthetic list', 0);
            INSERT INTO list_items (list_id, release_id, position) VALUES (1, 1, 0), (1, 2, 1);
            INSERT INTO facets (id, kind, slug, name) VALUES (1000, 'genre', 'old-facet', 'Old facet');
            INSERT INTO work_facets (work_id, facet_id) VALUES (1, 1000);
            INSERT INTO release_facets (release_id, facet_id, rank) VALUES (1, 1000, 2);
            """);
    }

    private static string Snapshot(TempDatabase db, string table)
    {
        using var connection = db.Factory.Open();
        var query = table switch
        {
            "work_facets" => "SELECT work_id || ':' || facet_id FROM work_facets ORDER BY work_id, facet_id",
            "release_facets" => "SELECT release_id || ':' || facet_id || ':' || rank FROM release_facets ORDER BY release_id, facet_id",
            "list_items" => "SELECT release_id || ':' || position FROM list_items ORDER BY release_id",
            _ => "SELECT release_id || ':' || surfaced_on || ':' || shelf_id FROM feed_surfacings ORDER BY release_id, surfaced_on",
        };
        // Include vocabulary creation, which is part of replacing facet assignments.
        return string.Join(";", connection.Query<string>(query)) + "|" +
            string.Join(";", connection.Query<string>("SELECT id || ':' || slug FROM facets ORDER BY id"));
    }

    private sealed class CancellingSurfacings(CancellationTokenSource cancellation) : IReadOnlyList<FeedSurfacing>
    {
        public int Count => 2;
        public FeedSurfacing this[int index] => Surfacing(index + 1);
        public IEnumerator<FeedSurfacing> GetEnumerator()
        {
            yield return Surfacing(1);
            cancellation.Cancel();
            yield return Surfacing(2);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
