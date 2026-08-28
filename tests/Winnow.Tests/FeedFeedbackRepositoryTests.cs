using Dapper;
using Winnow.Core.Domain;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The feedback loop's storage semantics: verdicts are append-and-revoke with
/// "active" computed at read time; surfacings dedupe per (release, day); and
/// the launch-endorsement join counts only sessions Winnow itself launched,
/// inside the window — <c>attributed_by</c> is three-valued and neither NULL
/// nor 'inferred' may ever be read as an endorsement.
/// </summary>
public class FeedFeedbackRepositoryTests
{
    private static readonly DateTime Now = new(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);

    private static (long ReleaseId, long OwnershipId) SeedGame(TempDatabase db, string name)
    {
        using var conn = db.Factory.Open();
        var workId = conn.ExecuteScalar<long>(
            "INSERT INTO works (name) VALUES (@name) RETURNING id;", new { name });
        var releaseId = conn.ExecuteScalar<long>(
            "INSERT INTO releases (work_id, name) VALUES (@workId, @name) RETURNING id;",
            new { workId, name });
        var ownershipId = conn.ExecuteScalar<long>(
            "INSERT INTO ownerships (release_id, store) VALUES (@releaseId, 'steam') RETURNING id;",
            new { releaseId });
        return (releaseId, ownershipId);
    }

    private static void SeedSession(
        TempDatabase db, long ownershipId, DateTime startedAt, string? attributedBy)
    {
        using var conn = db.Factory.Open();
        conn.Execute("""
            INSERT INTO sessions (ownership_id, started_at, ended_at, duration_s, detection_method, attributed_by)
            VALUES (@ownershipId, @startedAt, @endedAt, 2400, 'process_watch', @attributedBy);
            """,
            new { ownershipId, startedAt, endedAt = startedAt.AddMinutes(40), attributedBy });
    }

    [Fact]
    public async Task Verdicts_round_trip_and_active_is_computed_at_read_time()
    {
        using var db = new TempDatabase();
        var repo = new FeedFeedbackRepository(db.Factory);
        var (releaseId, _) = SeedGame(db, "Riven");

        var id = await repo.RecordVerdictAsync(new FeedVerdict
        {
            ReleaseId = releaseId,
            Kind = FeedVerdictKinds.NotInterested,
            CreatedAt = Now,
        });

        var active = await repo.GetActiveVerdictsAsync(Now.AddDays(1));
        var verdict = Assert.Single(active);
        Assert.Equal(id, verdict.Id);
        Assert.Equal(releaseId, verdict.ReleaseId);
        Assert.Equal(FeedVerdictKinds.NotInterested, verdict.Kind);
        Assert.Null(verdict.ExpiresAt);
        Assert.Null(verdict.RevokedAt);

        // Durable: still active years later. "Not interested" has no expiry.
        Assert.Single(await repo.GetActiveVerdictsAsync(Now.AddYears(3)));
    }

    [Fact]
    public async Task Snooze_lapses_by_itself_with_no_write()
    {
        using var db = new TempDatabase();
        var repo = new FeedFeedbackRepository(db.Factory);
        var (releaseId, _) = SeedGame(db, "Riven");

        await repo.RecordVerdictAsync(new FeedVerdict
        {
            ReleaseId = releaseId,
            Kind = FeedVerdictKinds.Snoozed,
            CreatedAt = Now,
            ExpiresAt = Now.AddDays(7),
        });

        // Binding while unexpired, gone once the clock passes the expiry —
        // and nothing wrote anything in between: lapse is a read-time fact.
        Assert.Single(await repo.GetActiveVerdictsAsync(Now.AddDays(6)));
        Assert.Empty(await repo.GetActiveVerdictsAsync(Now.AddDays(8)));

        // The lapsed row is still there for inspection, un-revoked: it lapsed,
        // the user never took it back.
        var all = Assert.Single(await repo.GetAllVerdictsAsync());
        Assert.Null(all.RevokedAt);
    }

    [Fact]
    public async Task Revoke_stamps_active_rows_only_and_keeps_history()
    {
        using var db = new TempDatabase();
        var repo = new FeedFeedbackRepository(db.Factory);
        var (releaseId, _) = SeedGame(db, "Riven");

        await repo.RecordVerdictAsync(new FeedVerdict
        {
            ReleaseId = releaseId,
            Kind = FeedVerdictKinds.NotInterested,
            CreatedAt = Now,
        });

        // Undo: one row revoked, none active, history intact and inspectable.
        Assert.Equal(1, await repo.RevokeVerdictsAsync(
            releaseId, FeedVerdictKinds.NotInterested, Now.AddDays(1)));
        Assert.Empty(await repo.GetActiveVerdictsAsync(Now.AddDays(2)));

        var history = Assert.Single(await repo.GetAllVerdictsAsync());
        Assert.NotNull(history.RevokedAt);

        // Undoing again finds nothing active — not an error, zero rows.
        Assert.Equal(0, await repo.RevokeVerdictsAsync(
            releaseId, FeedVerdictKinds.NotInterested, Now.AddDays(2)));

        // Dismiss again after the undo: a NEW row, so the record shows
        // dismissed → undone → dismissed rather than a mutated single fact.
        await repo.RecordVerdictAsync(new FeedVerdict
        {
            ReleaseId = releaseId,
            Kind = FeedVerdictKinds.NotInterested,
            CreatedAt = Now.AddDays(3),
        });
        Assert.Single(await repo.GetActiveVerdictsAsync(Now.AddDays(4)));
        Assert.Equal(2, (await repo.GetAllVerdictsAsync()).Count);
    }

    [Fact]
    public async Task Revoke_does_not_stamp_a_snooze_that_already_lapsed()
    {
        using var db = new TempDatabase();
        var repo = new FeedFeedbackRepository(db.Factory);
        var (releaseId, _) = SeedGame(db, "Riven");

        await repo.RecordVerdictAsync(new FeedVerdict
        {
            ReleaseId = releaseId,
            Kind = FeedVerdictKinds.Snoozed,
            CreatedAt = Now,
            ExpiresAt = Now.AddDays(7),
        });

        // "Undo" arriving after the lapse: stamping it would claim the user
        // undid something that had already undone itself.
        Assert.Equal(0, await repo.RevokeVerdictsAsync(
            releaseId, FeedVerdictKinds.Snoozed, Now.AddDays(10)));
        Assert.Null(Assert.Single(await repo.GetAllVerdictsAsync()).RevokedAt);
    }

    [Fact]
    public async Task Surfacings_dedupe_per_day_and_filter_by_since()
    {
        using var db = new TempDatabase();
        var repo = new FeedFeedbackRepository(db.Factory);
        var (releaseA, _) = SeedGame(db, "Riven");
        var (releaseB, _) = SeedGame(db, "Obduction");

        var day1 = new DateOnly(2026, 8, 25);
        var day2 = new DateOnly(2026, 8, 26);

        await repo.RecordSurfacedAsync(
        [
            new FeedSurfacing { ReleaseId = releaseA, SurfacedOn = day1, ShelfId = "on_your_taste" },
            new FeedSurfacing { ReleaseId = releaseB, SurfacedOn = day1, ShelfId = "ready_to_play" },
        ]);

        // A same-day refresh re-records: a no-op, not a duplicate and not an
        // error — the first record of the day wins.
        await repo.RecordSurfacedAsync(
        [
            new FeedSurfacing { ReleaseId = releaseA, SurfacedOn = day1, ShelfId = "ready_to_play" },
        ]);
        await repo.RecordSurfacedAsync(
        [
            new FeedSurfacing { ReleaseId = releaseA, SurfacedOn = day2, ShelfId = "on_your_taste" },
        ]);

        var all = await repo.GetSurfacedSinceAsync(day1);
        Assert.Equal(3, all.Count);
        Assert.Equal("on_your_taste",
            all.Single(s => s.ReleaseId == releaseA && s.SurfacedOn == day1).ShelfId);

        // The since filter is inclusive of its own day.
        var tail = await repo.GetSurfacedSinceAsync(day2);
        var only = Assert.Single(tail);
        Assert.Equal(releaseA, only.ReleaseId);
        Assert.Equal(day2, only.SurfacedOn);
    }

    [Fact]
    public async Task Endorsements_count_only_winnow_launched_sessions_inside_the_window()
    {
        using var db = new TempDatabase();
        var repo = new FeedFeedbackRepository(db.Factory);
        var launched = SeedGame(db, "Launched off the feed");
        var inferred = SeedGame(db, "Started from Steam");
        var unrecorded = SeedGame(db, "Pre-M3b session");
        var late = SeedGame(db, "Launched long after");

        var surfacedOn = new DateOnly(2026, 8, 25);
        await repo.RecordSurfacedAsync(
        [
            new FeedSurfacing { ReleaseId = launched.ReleaseId, SurfacedOn = surfacedOn, ShelfId = "on_your_taste" },
            new FeedSurfacing { ReleaseId = inferred.ReleaseId, SurfacedOn = surfacedOn, ShelfId = "on_your_taste" },
            new FeedSurfacing { ReleaseId = unrecorded.ReleaseId, SurfacedOn = surfacedOn, ShelfId = "on_your_taste" },
            new FeedSurfacing { ReleaseId = late.ReleaseId, SurfacedOn = surfacedOn, ShelfId = "on_your_taste" },
        ]);

        var sameEvening = new DateTime(2026, 8, 25, 20, 0, 0, DateTimeKind.Utc);
        SeedSession(db, launched.OwnershipId, sameEvening, SessionAttributions.Launch);

        // The game was on the feed, but the user started it from Steam — the
        // watcher inferred the join. Seeing the card and acting elsewhere is
        // not the feed being answered, and must not be counted as if it were.
        SeedSession(db, inferred.OwnershipId, sameEvening, SessionAttributions.Inferred);

        // NULL is "not recorded", never "not launched here" — and never
        // "launched here" either. Three-valued, folded into neither answer.
        SeedSession(db, unrecorded.OwnershipId, sameEvening, null);

        // Winnow-launched, but ten days after the surfacing: by then the launch
        // is the user's own idea, and crediting the feed would be the feed
        // grading its own homework.
        SeedSession(db, late.OwnershipId, sameEvening.AddDays(10), SessionAttributions.Launch);

        var endorsements = await repo.GetEndorsementsAsync(windowDays: 3);
        var endorsement = Assert.Single(endorsements);
        Assert.Equal(launched.ReleaseId, endorsement.ReleaseId);
        Assert.Equal(surfacedOn, endorsement.SurfacedOn);
        Assert.Equal("on_your_taste", endorsement.ShelfId);
    }

    [Fact]
    public async Task A_session_inside_two_surfacing_windows_is_one_endorsement_for_the_latest()
    {
        using var db = new TempDatabase();
        var repo = new FeedFeedbackRepository(db.Factory);
        var game = SeedGame(db, "Riven");

        await repo.RecordSurfacedAsync(
        [
            new FeedSurfacing { ReleaseId = game.ReleaseId, SurfacedOn = new DateOnly(2026, 8, 24), ShelfId = "on_your_taste" },
            new FeedSurfacing { ReleaseId = game.ReleaseId, SurfacedOn = new DateOnly(2026, 8, 26), ShelfId = "ready_to_play" },
        ]);

        SeedSession(db, game.OwnershipId,
            new DateTime(2026, 8, 26, 20, 0, 0, DateTimeKind.Utc), SessionAttributions.Launch);

        // One session, one endorsement — credited to the nearest surfacing,
        // never double-counted because it happened to sit in two windows.
        var endorsement = Assert.Single(await repo.GetEndorsementsAsync(windowDays: 3));
        Assert.Equal(new DateOnly(2026, 8, 26), endorsement.SurfacedOn);
        Assert.Equal("ready_to_play", endorsement.ShelfId);
    }
}
