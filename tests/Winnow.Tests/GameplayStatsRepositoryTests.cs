using System.Diagnostics;
using Dapper;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace Winnow.Tests;

public sealed class GameplayStatsRepositoryTests(ITestOutputHelper output)
{
    private static readonly DateTime Start = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private static GameplayStatsRequest Request(params GameplayOwnershipScope[] scope) => new()
    {
        Ownerships = scope, FromUtc = Start, UntilUtc = Start.AddDays(7), AsOfUtc = Start.AddDays(8),
        TimeBins = [new(Start, Start.AddDays(1)), new(Start.AddDays(1), Start.AddDays(7))],
    };

    private static void Session(TempDatabase db, long owner, DateTime start, DateTime? end,
        long? seconds, string method = "manual")
    {
        using var connection = db.Factory.Open();
        connection.Execute("""
            INSERT INTO sessions(ownership_id,started_at,ended_at,duration_s,detection_method)
            VALUES(@owner,@start,@end,@seconds,@method);
            """, new { owner, start, end, seconds, method });
    }

    [Fact]
    public async Task Hours_clip_overlaps_proportionally_while_lengths_count_sessions_started_in_period()
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        Session(db, 1, Start.AddHours(-1), Start.AddHours(1), 3600);
        Session(db, 1, Start.AddDays(7).AddHours(-1), Start.AddDays(7).AddHours(1), 7200);
        Session(db, 1, Start.AddHours(-2), Start, 7200);
        Session(db, 1, Start.AddDays(7), Start.AddDays(7).AddHours(1), 3600);
        var stats = await new GameplayStatsRepository(db.Factory).GetAsync(Request(new GameplayOwnershipScope(1, 1)));
        Assert.Equal(5400, stats.RecordedSeconds);
        Assert.Equal(2, stats.OverlappingSessionCount);
        Assert.Equal(1, stats.StartedSessionCount);
        Assert.Equal(7200, stats.MedianSessionSeconds);
        Assert.Equal(new double[] { 1800, 3600 }, stats.Periods.Select(p => p.RecordedSeconds));
        Assert.Equal(stats.RecordedSeconds, stats.Periods.Sum(p => p.RecordedSeconds));
        Assert.Equal(1, stats.SessionLengths[3].Count);
    }

    [Theory]
    [InlineData("gog")]
    [InlineData("plugin:xbox")]
    [InlineData("plugin:another-store")]
    public async Task Actual_store_filter_precedes_linked_game_folding_and_visibility_is_explicit(string store)
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 3);
        using (var connection = db.Factory.Open())
            connection.Execute("UPDATE ownerships SET store=@store WHERE id=2;", new { store });
        Session(db, 1, Start, Start.AddHours(1), 3600);
        Session(db, 2, Start, Start.AddHours(2), 7200);
        Session(db, 3, Start, Start.AddHours(8), 28800);
        var repository = new GameplayStatsRepository(db.Factory);
        var request = Request(new(1, 1), new(2, 1));
        var stats = await repository.GetAsync(request);
        Assert.Equal(10800, stats.RecordedSeconds);
        Assert.Equal(1, stats.GamesPlayedCount);
        Assert.Equal(new GameplayGameTotal(1, 10800), Assert.Single(stats.TopGames));
        Assert.Equal(new[] { new GameplayStoreTotal(store, 7200), new GameplayStoreTotal("steam", 3600) }, stats.Stores);
        var selected = await repository.GetAsync(request with { Store = store });
        Assert.Equal(7200, selected.RecordedSeconds);
        Assert.Equal(1, selected.OverlappingSessionCount);
        Assert.Equal(1, Assert.Single(selected.TopGames).ResolvedWorkId);
        Assert.Equal(0, (await repository.GetAsync(request with { Store = "epic" })).RecordedSeconds);
    }

    [Fact]
    public async Task Xbox_lifetime_playtime_and_last_played_do_not_become_recorded_sessions()
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        using (var connection = db.Factory.Open())
            connection.Execute("""
                UPDATE ownerships SET store='plugin:xbox' WHERE id=2;
                INSERT INTO play_records(ownership_id,playtime_minutes,last_played_at,source,observed_at)
                VALUES(2,7200,@Start,'plugin:xbox',@Start);
                """, new { Start });
        Session(db, 1, Start, Start.AddHours(1), 3600);
        var stats = await new GameplayStatsRepository(db.Factory).GetAsync(Request(new(1, 1), new(2, 2))
            with { Store = "plugin:xbox" });
        Assert.Equal(0, stats.RecordedSeconds);
        Assert.Equal(0, stats.GamesPlayedCount);
        Assert.Equal(0, stats.StartedSessionCount);
        Assert.Empty(stats.TopGames);
    }

    [Fact]
    public async Task Duplicate_evidence_is_removed_per_ownership_without_deduplicating_concurrent_games()
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        Session(db, 1, Start, Start.AddHours(1), 3600);
        Session(db, 1, Start, Start.AddHours(1), 3600, "import");
        Session(db, 2, Start, Start.AddHours(1), 3600);
        var stats = await new GameplayStatsRepository(db.Factory).GetAsync(Request(new(1, 1), new(2, 2), new(1, 1)));
        Assert.Equal(7200, stats.RecordedSeconds);
        Assert.Equal(2, stats.StartedSessionCount);
        Assert.Equal(2, stats.GamesPlayedCount);
    }

    [Fact]
    public async Task Open_invalid_and_future_completions_are_excluded_and_counted_separately()
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        Session(db, 1, Start, null, null);
        Session(db, 1, Start, Start.AddHours(1), null);
        Session(db, 1, Start, Start.AddHours(1), 0);
        Session(db, 1, Start, Start.AddHours(-1), 30);
        Session(db, 1, Start, Start.AddHours(1), 3602);
        Session(db, 1, Start, Start.AddDays(9), 3600);
        Session(db, 1, Start, Start.AddHours(1), 3601);
        var stats = await new GameplayStatsRepository(db.Factory).GetAsync(Request(new GameplayOwnershipScope(1, 1)));
        Assert.Equal(3601, stats.RecordedSeconds);
        Assert.Equal(1, stats.OverlappingSessionCount);
        Assert.Equal(6, stats.ExcludedSessionCount);
    }

    [Fact]
    public async Task Length_boundaries_and_even_median_use_full_durations_of_started_sessions()
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        foreach (var seconds in new[] { 1799, 1800, 3599, 3600, 7199, 7200 })
            Session(db, 1, Start, Start.AddSeconds(seconds), seconds);
        var stats = await new GameplayStatsRepository(db.Factory).GetAsync(Request(new GameplayOwnershipScope(1, 1)));
        Assert.Equal(new[] { 1, 2, 2, 1 }, stats.SessionLengths.Select(b => b.Count));
        Assert.Equal(3599.5, stats.MedianSessionSeconds);
        Assert.Equal(6, stats.StartedSessionCount);
    }

    [Fact]
    public async Task Fractional_timestamps_preserve_distinct_evidence_clipping_and_as_of_boundary()
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        Session(db, 1, Start.AddHours(-1).AddMilliseconds(100), Start.AddHours(1).AddMilliseconds(100), 7200);
        Session(db, 1, Start.AddHours(-1).AddMilliseconds(500), Start.AddHours(1).AddMilliseconds(500), 7200);
        Session(db, 1, Start.AddHours(-1).AddMilliseconds(100), Start.AddHours(1).AddMilliseconds(100), 7200, "import");
        var repository = new GameplayStatsRepository(db.Factory);
        var request = Request(new GameplayOwnershipScope(1, 1));
        var stats = await repository.GetAsync(request);
        Assert.Equal(2, stats.OverlappingSessionCount);
        Assert.Equal(7200.6, stats.RecordedSeconds, 3);
        var earlier = await repository.GetAsync(request with { AsOfUtc = Start.AddHours(1).AddMilliseconds(200) });
        Assert.Equal(1, earlier.OverlappingSessionCount);
        Assert.Equal(3600.1, earlier.RecordedSeconds, 3);
    }

    [Fact]
    public async Task Local_day_bins_preserve_the_extra_hour_at_the_DST_fall_back()
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        var first = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 10, 31), zone);
        var middle = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, 1), zone);
        var last = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, 2), zone);
        Session(db, 1, first, last, (long)(last - first).TotalSeconds);
        var stats = await new GameplayStatsRepository(db.Factory).GetAsync(Request(new GameplayOwnershipScope(1, 1)) with
        {
            FromUtc = first, UntilUtc = last, AsOfUtc = last,
            TimeBins = [new(first, middle), new(middle, last)],
        });
        Assert.Equal(new double[] { 86400, 90000 }, stats.Periods.Select(p => p.RecordedSeconds));
    }

    [Fact]
    public async Task Empty_history_returns_complete_zero_bins_and_no_median()
    {
        using var db = new TempDatabase();
        var stats = await new GameplayStatsRepository(db.Factory).GetAsync(Request());
        Assert.Equal(0, stats.RecordedSeconds);
        Assert.Equal(2, stats.Periods.Count);
        Assert.All(stats.Periods, b => Assert.Equal(0, b.RecordedSeconds));
        Assert.Equal(4, stats.SessionLengths.Count);
        Assert.Null(stats.MedianSessionSeconds);
        Assert.Empty(stats.TopGames);
        Assert.Empty(stats.Stores);
    }

    [Fact]
    public async Task Invalid_scope_and_noncontiguous_bins_are_refused_and_cancellation_is_observed()
    {
        using var db = new TempDatabase();
        var repository = new GameplayStatsRepository(db.Factory);
        await Assert.ThrowsAsync<ArgumentException>(() => repository.GetAsync(Request(new(1, 1), new(1, 2))));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.GetAsync(Request() with
        {
            TimeBins = [new(Start.AddHours(1), Start.AddDays(7))],
        }));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.GetAsync(Request() with { FromUtc = Start.ToLocalTime() }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.GetAsync(Request(), new CancellationToken(true)));
    }

    [Fact]
    public async Task Reads_enlist_in_the_callers_transaction_without_committing_it()
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        var repository = new GameplayStatsRepository(db.Factory);
        using (var scope = db.Factory.Begin())
        {
            using var lease = db.Factory.Lease();
            await lease.Connection.ExecuteAsync("""
                INSERT INTO sessions(ownership_id,started_at,ended_at,duration_s,detection_method)
                VALUES(1,@Start,@End,3600,'manual');
                """, new { Start, End = Start.AddHours(1) }, transaction: lease.Transaction);
            Assert.Equal(3600, (await repository.GetAsync(Request(new GameplayOwnershipScope(1, 1)))).RecordedSeconds);
        }
        Assert.Equal(0, (await repository.GetAsync(Request(new GameplayOwnershipScope(1, 1)))).RecordedSeconds);
    }

    [Fact]
    public async Task Large_synthetic_history_returns_bounded_aggregates_and_records_query_timings()
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 10_000);
        using (var connection = db.Factory.Open())
            connection.Execute("""
                WITH RECURSIVE seq(id) AS (SELECT 1 UNION ALL SELECT id+1 FROM seq WHERE id<100000)
                INSERT INTO sessions(ownership_id,started_at,ended_at,duration_s,detection_method)
                SELECT (id-1)%10000+1, datetime(@Start, '+' || (id*60) || ' seconds'),
                       datetime(@Start, '+' || (id*60+600) || ' seconds'), 600, 'manual' FROM seq;
                UPDATE ownerships SET store='gog' WHERE id%2=0;
                """, new { Start });
        var repository = new GameplayStatsRepository(db.Factory);
        var ownerships = Enumerable.Range(1, 10_000).Select(id => new GameplayOwnershipScope(id, (id + 1) / 2)).ToArray();
        foreach (var days in new[] { 30, 90 })
        {
            var until = Start.AddDays(days);
            var bins = new List<GameplayTimeBin>();
            for (var cursor = Start; cursor < until; cursor = cursor.AddDays(7))
                bins.Add(new(cursor, cursor.AddDays(7) < until ? cursor.AddDays(7) : until));
            var request = Request(ownerships) with { UntilUtc = until, AsOfUtc = Start.AddDays(91), TimeBins = bins };
            for (var run = 0; run < 3; run++)
            {
                var timer = Stopwatch.StartNew();
                var stats = await repository.GetAsync(request);
                timer.Stop();
                Assert.Equal(10, stats.TopGames.Count);
                Assert.Equal(bins.Count, stats.Periods.Count);
                Assert.Equal(2, stats.Stores.Count);
                Assert.Equal(4, stats.SessionLengths.Count);
                Assert.Equal(stats.RecordedSeconds, stats.Periods.Sum(p => p.RecordedSeconds), 5);
                Assert.Equal(days == 90 ? 100_000 : 43_199, stats.StartedSessionCount);
                output.WriteLine($"10,000 ownerships / 100,000 sessions; {days} days; run {run + 1}; "
                    + $"{timer.Elapsed.TotalMilliseconds:F1} ms; sessions {stats.OverlappingSessionCount}; "
                    + $"rows periods={stats.Periods.Count}, games={stats.TopGames.Count}, stores={stats.Stores.Count}, lengths={stats.SessionLengths.Count}");
            }
        }
    }
}
