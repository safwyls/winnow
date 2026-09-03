using Dapper;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Library history stats: exact aggregates over sessions and playtime snapshots.
/// The maturity tier is a claim about the whole library, so these figures are
/// counted, never scaled from a sample. <c>IsEstimate</c> is false on every path
/// through this repository; the fallback's estimated figure may gate behaviour
/// but must never be shown to the user as a total.
/// </summary>
public class LibraryHistoryStatsTests
{
    [Fact]
    public async Task An_empty_library_reports_nothing_rather_than_guessing()
    {
        using var db = new TempDatabase();

        var stats = await new LibraryHistoryStatsRepository(db.Factory).GetAsync();

        Assert.Equal(0, stats.SessionCount);
        Assert.Null(stats.FirstSessionAt);
        Assert.Null(stats.LastSessionAt);
        Assert.Equal(0, stats.OwnershipsWithSnapshotRises);
        Assert.False(stats.IsEstimate);
    }

    [Fact]
    public async Task Sessions_are_counted_and_bounded_by_their_own_extremes()
    {
        using var db = new TempDatabase();
        var (ownershipA, _) = Seed(db);

        using (var conn = db.Factory.Open())
        {
            conn.Execute("""
                INSERT INTO sessions (ownership_id, started_at, detection_method) VALUES
                    (@id, '2026-03-05 09:00:00', 'process_watch'),
                    (@id, '2026-01-02 21:30:00', 'process_watch'),
                    (@id, '2026-05-11 18:15:00', 'manual');
                """, new { id = ownershipA });
        }

        var stats = await new LibraryHistoryStatsRepository(db.Factory).GetAsync();

        Assert.Equal(3, stats.SessionCount);
        Assert.Equal(new DateTime(2026, 1, 2, 21, 30, 0, DateTimeKind.Utc), stats.FirstSessionAt);
        Assert.Equal(new DateTime(2026, 5, 11, 18, 15, 0, DateTimeKind.Utc), stats.LastSessionAt);
        Assert.False(stats.IsEstimate);
    }

    [Fact]
    public async Task Only_an_ownership_whose_snapshots_actually_rose_counts()
    {
        using var db = new TempDatabase();
        var (rising, flat) = Seed(db);

        using (var conn = db.Factory.Open())
        {
            // A series that moved: the evidence the recommender's episode signal
            // needs, and the whole point of keeping snapshots at all.
            conn.Execute("""
                INSERT INTO playtime_snapshots (ownership_id, playtime_minutes, observed_at) VALUES
                    (@rising, 120, '2026-01-01 00:00:00'),
                    (@rising, 340, '2026-02-01 00:00:00');
                """, new { rising });

            // Three readings of the same number is one fact observed three
            // times, not a history.
            conn.Execute("""
                INSERT INTO playtime_snapshots (ownership_id, playtime_minutes, observed_at) VALUES
                    (@flat, 90, '2026-01-01 00:00:00'),
                    (@flat, 90, '2026-02-01 00:00:00'),
                    (@flat, 90, '2026-03-01 00:00:00');
                """, new { flat });
        }

        var stats = await new LibraryHistoryStatsRepository(db.Factory).GetAsync();

        Assert.Equal(1, stats.OwnershipsWithSnapshotRises);
        Assert.Equal(0, stats.SessionCount);
    }

    /// <summary>Two releases, one ownership each, so the two can be told apart.</summary>
    private static (long First, long Second) Seed(TempDatabase db)
    {
        using var conn = db.Factory.Open();

        var workId = conn.ExecuteScalar<long>(
            "INSERT INTO works (name) VALUES ('Outer Wilds') RETURNING id;");

        var firstRelease = conn.ExecuteScalar<long>(
            "INSERT INTO releases (work_id, name) VALUES (@workId, 'Outer Wilds') RETURNING id;",
            new { workId });
        var secondRelease = conn.ExecuteScalar<long>(
            "INSERT INTO releases (work_id, name) VALUES (@workId, 'Echoes of the Eye') RETURNING id;",
            new { workId });

        var first = conn.ExecuteScalar<long>(
            "INSERT INTO ownerships (release_id, store) VALUES (@firstRelease, 'steam') RETURNING id;",
            new { firstRelease });
        var second = conn.ExecuteScalar<long>(
            "INSERT INTO ownerships (release_id, store) VALUES (@secondRelease, 'gog') RETURNING id;",
            new { secondRelease });

        return (first, second);
    }
}
