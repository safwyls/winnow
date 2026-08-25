using Hoard.Core.Domain;
using Hoard.Core.Queries;
using Hoard.Data.Repositories;
using Xunit;

namespace Hoard.Tests;

/// <summary>
/// §6.1 derived-bucket query against seeded data. Thresholds here are the
/// query's parameters, proving buckets are derived, not stored:
/// bounced floor 120 min (the refund line), retired floor 3000 min, stale
/// window 3 months, update correlation window 7 days.
/// </summary>
public class BucketQueryTests : IAsyncLifetime, IDisposable
{
    private static readonly BucketThresholds Thresholds = new(
        BouncedFloorMinutes: 120,
        RetiredFloorMinutes: 3000,
        StaleWindowMonths: 3,
        UpdateCorrelationWindowDays: 7);

    private readonly TempDatabase _db = new();
    private readonly Dictionary<string, long> _ownershipsByCase = [];
    private IReadOnlyList<OwnershipBucket> _buckets = [];

    public void Dispose() => _db.Dispose();

    public Task DisposeAsync() => Task.CompletedTask;

    public async Task InitializeAsync()
    {
        var works = new WorkRepository(_db.Factory);
        var releases = new ReleaseRepository(_db.Factory);
        var ownerships = new OwnershipRepository(_db.Factory);
        var plays = new PlayRecordRepository(_db.Factory);
        var updates = new UpdateEventRepository(_db.Factory);

        var workId = await works.InsertAsync(new Work { Name = "Fixture", FirstReleaseYear = 2020 });

        async Task<long> SeedAsync(string name)
        {
            var releaseId = await releases.InsertAsync(new Release
            {
                WorkId = workId,
                Name = name,
                Platform = "windows",
            });
            var ownershipId = await ownerships.InsertAsync(new Ownership
            {
                ReleaseId = releaseId,
                Store = "steam",
            });
            _ownershipsByCase[name] = ownershipId;
            return releaseId;
        }

        Task<long> PlayAsync(string caseName, long minutes, DateTime? lastPlayed, DateTime observed)
            => plays.InsertAsync(new PlayRecord
            {
                OwnershipId = _ownershipsByCase[caseName],
                PlaytimeMinutes = minutes,
                LastPlayedAt = lastPlayed,
                Source = "steam_local",
                ObservedAt = observed,
            });

        Task<long> BuildPushAsync(long releaseId, DateTime occurredAt)
            => updates.InsertAsync(new UpdateEvent
            {
                ReleaseId = releaseId,
                Kind = UpdateEventKinds.BuildPush,
                BuildId = "42",
                OccurredAt = occurredAt,
            });

        Task<long> AnnouncementAsync(long releaseId, DateTime occurredAt)
            => updates.InsertAsync(new UpdateEvent
            {
                ReleaseId = releaseId,
                Kind = UpdateEventKinds.Announcement,
                OccurredAt = occurredAt,
                Title = "Patch notes",
            });

        static DateTime Utc(int y, int mo, int d) => new(y, mo, d, 0, 0, 0, DateTimeKind.Utc);

        // 1. Never played — no play record at all.
        await SeedAsync("never_no_record");

        // 2. Never played — a record exists but playtime is zero.
        await SeedAsync("never_zero_playtime");
        await PlayAsync("never_zero_playtime", 0, null, Utc(2026, 8, 1));

        // 3-7. The refund line, minute by minute. 120 is a BOUNDARY, not a
        //    ceiling: below it the purchase was still refundable so the game was
        //    never really played; at or above it the user committed. None of
        //    these carry update events, so staleness cannot confound the answer.
        foreach (var minutes in new long[] { 1, 119, 120, 121 })
        {
            await SeedAsync($"refund_{minutes}");
            await PlayAsync($"refund_{minutes}", minutes, Utc(2024, 5, 10), Utc(2026, 8, 1));
        }

        // 8-10. The retired floor, either side. 3000 is inclusive, so 2999 is
        //    still the top of Bounced — which now runs all the way up to it.
        foreach (var minutes in new long[] { 2999, 3000, 3001 })
        {
            await SeedAsync($"retired_{minutes}");
            await PlayAsync($"retired_{minutes}", minutes, Utc(2024, 5, 10), Utc(2026, 8, 1));
        }

        // 11. A game an hour into the refund window that WAS patched since. The
        //     bug this guards: if the refund line were tested before staleness,
        //     this row would read `never_played`, lose its badge, and the poll
        //     request that discovered the patch would have been spent for
        //     nothing (design-system §5.2).
        var bouncedStale = await SeedAsync("under_refund_line_but_patched");
        await PlayAsync("under_refund_line_but_patched", 60, Utc(2024, 1, 15), Utc(2026, 8, 1));
        await BuildPushAsync(bouncedStale, Utc(2024, 8, 1));
        await AnnouncementAsync(bouncedStale, Utc(2024, 8, 3));

        // 12. The mirror image, and the line staleness may NOT cross: a game
        //     with no minutes and no date has nothing to be behind on, so an
        //     update landing on it is still not a badge.
        var neverOpenedPatched = await SeedAsync("never_opened_but_patched");
        await BuildPushAsync(neverOpenedPatched, Utc(2024, 8, 1));
        await AnnouncementAsync(neverOpenedPatched, Utc(2024, 8, 3));

        // 5. Stale but patched — meaningful playtime (600), last played
        //    2024-01-15, and a MAJOR update: a build push on 2024-08-01 with an
        //    announcement two days later, >3 months after last play. Both
        //    signals are required (§4.5); see case 9 for one alone.
        var staleRelease = await SeedAsync("stale_but_patched");
        await PlayAsync("stale_but_patched", 600, Utc(2024, 1, 15), Utc(2026, 8, 1));
        await BuildPushAsync(staleRelease, Utc(2024, 8, 1));
        await AnnouncementAsync(staleRelease, Utc(2024, 8, 3));

        // 6. Not stale — update landed within the window (2 months after
        //    last played < 3-month window), so still active.
        var freshRelease = await SeedAsync("active_recent_update");
        await PlayAsync("active_recent_update", 600, Utc(2026, 4, 1), Utc(2026, 8, 1));
        await BuildPushAsync(freshRelease, Utc(2026, 6, 1));
        await AnnouncementAsync(freshRelease, Utc(2026, 6, 1));

        // 7. Retired — high playtime (6000 >= 3000). Also has a stale-shaped
        //    major update to prove retired outranks stale.
        var retiredRelease = await SeedAsync("retired");
        await PlayAsync("retired", 6000, Utc(2023, 1, 1), Utc(2026, 8, 1));
        await BuildPushAsync(retiredRelease, Utc(2025, 1, 1));
        await AnnouncementAsync(retiredRelease, Utc(2025, 1, 1));

        // 8. Stale precedence check — only the LATEST play record counts:
        //    an old bounced-level record is superseded by a newer one.
        await SeedAsync("superseded_record");
        await PlayAsync("superseded_record", 30, Utc(2025, 1, 1), Utc(2025, 1, 2));
        await PlayAsync("superseded_record", 500, Utc(2026, 7, 1), Utc(2026, 7, 2));

        // 9. §4.5 / pitfall 4: a LONE build push is not a major update. Same
        //    shape as case 5 in every other respect — a depot push long after
        //    last play — so if this reads stale, the noisy signal is leaking.
        var lonePushRelease = await SeedAsync("lone_build_push");
        await PlayAsync("lone_build_push", 600, Utc(2024, 1, 15), Utc(2026, 8, 1));
        await BuildPushAsync(lonePushRelease, Utc(2024, 8, 1));

        // 10. A lone announcement is not one either — marketing without a build.
        var loneNewsRelease = await SeedAsync("lone_announcement");
        await PlayAsync("lone_announcement", 600, Utc(2024, 1, 15), Utc(2026, 8, 1));
        await AnnouncementAsync(loneNewsRelease, Utc(2024, 8, 1));

        // 11. Both signals, but 45 days apart: unrelated events, not one update.
        var uncorrelatedRelease = await SeedAsync("uncorrelated_signals");
        await PlayAsync("uncorrelated_signals", 600, Utc(2024, 1, 15), Utc(2026, 8, 1));
        await BuildPushAsync(uncorrelatedRelease, Utc(2024, 8, 1));
        await AnnouncementAsync(uncorrelatedRelease, Utc(2024, 9, 15));

        // 12. Boundary: exactly the 7-day correlation window still correlates.
        var edgeRelease = await SeedAsync("correlation_window_edge");
        await PlayAsync("correlation_window_edge", 600, Utc(2024, 1, 15), Utc(2026, 8, 1));
        await BuildPushAsync(edgeRelease, Utc(2024, 8, 1));
        await AnnouncementAsync(edgeRelease, Utc(2024, 8, 8));

        // 13. The 86400 sentinel pile: real playtime, last-played unknown
        //     because it predates Steam's timestamps. Maximally dormant, so a
        //     major update makes it stale — it is exactly what the bucket is for.
        var ancientRelease = await SeedAsync("null_last_played_patched");
        await PlayAsync("null_last_played_patched", 600, null, Utc(2026, 8, 1));
        await BuildPushAsync(ancientRelease, Utc(2024, 8, 1));
        await AnnouncementAsync(ancientRelease, Utc(2024, 8, 3));

        // 14. Same unknown date, no update at all: dormant is not stale.
        await SeedAsync("null_last_played_no_update");
        await PlayAsync("null_last_played_no_update", 600, null, Utc(2026, 8, 1));

        // 15. Two observations in the SAME second — the timestamp handler stores
        //     whole seconds, so this tie is routine. The later insert (higher id)
        //     must win, agreeing with PlayRecordRepository.GetLatestAsync.
        await SeedAsync("same_second_tie");
        await PlayAsync("same_second_tie", 30, Utc(2026, 7, 1), Utc(2026, 8, 1));
        await PlayAsync("same_second_tie", 500, Utc(2026, 7, 1), Utc(2026, 8, 1));

        // 16. A machine with appmanifests but no readable userdata: the manifest
        //     proves the game was launched, nothing knows for how long. Zero
        //     minutes here means "unknown", so it is neither never-touched nor
        //     bounced.
        await SeedAsync("date_without_minutes");
        await PlayAsync("date_without_minutes", 0, Utc(2026, 7, 1), Utc(2026, 8, 1));

        _buckets = await new LibraryQueryRepository(_db.Factory)
            .GetOwnershipBucketsAsync(Thresholds);
    }

    private string BucketFor(string caseName) =>
        Assert.Single(_buckets, b => b.OwnershipId == _ownershipsByCase[caseName]).Bucket;

    [Fact]
    public void Query_returns_exactly_one_row_per_ownership()
        => Assert.Equal(_ownershipsByCase.Count, _buckets.Count);

    [Fact]
    public void Zero_playtime_is_never_played_with_or_without_a_record()
    {
        Assert.Equal(LibraryBuckets.NeverPlayed, BucketFor("never_no_record"));
        Assert.Equal(LibraryBuckets.NeverPlayed, BucketFor("never_zero_playtime"));
    }

    /// <summary>
    /// The refund line, exhaustively. 0/1/119 are Never played — under Steam's
    /// two-hour window the game could still have been handed back, so it was
    /// never really played — and 120/121 are Bounced off: committed past the
    /// point of no return and abandoned anyway.
    /// </summary>
    [Theory]
    [InlineData(1, "never_played")]
    [InlineData(119, "never_played")]
    [InlineData(120, "bounced")]
    [InlineData(121, "bounced")]
    public void The_refund_line_separates_never_played_from_bounced(long minutes, string expected)
        => Assert.Equal(expected, BucketFor($"refund_{minutes}"));

    /// <summary>
    /// The retired floor is inclusive and is now Bounced off's real ceiling —
    /// there is no `active` band between them any more.
    /// </summary>
    [Theory]
    [InlineData(2999, "bounced")]
    [InlineData(3000, "retired")]
    [InlineData(3001, "retired")]
    public void The_retired_floor_is_the_ceiling_of_bounced(long minutes, string expected)
        => Assert.Equal(expected, BucketFor($"retired_{minutes}"));

    /// <summary>
    /// design-system §5.2: the badge is `Patched since` membership, and the only
    /// game with "nothing to be behind on" is one that was never opened. An hour
    /// of play is play — so staleness is tested above the refund line, and this
    /// row keeps its badge instead of being absorbed by `never_played`.
    /// </summary>
    [Fact]
    public void A_game_under_the_refund_line_can_still_be_patched_since()
        => Assert.Equal(LibraryBuckets.StaleButPatched, BucketFor("under_refund_line_but_patched"));

    /// <summary>The line staleness may not cross, and the reason case 1 is tested first.</summary>
    [Fact]
    public void A_never_opened_game_is_not_patched_since_however_much_shipped()
        => Assert.Equal(LibraryBuckets.NeverPlayed, BucketFor("never_opened_but_patched"));

    [Fact]
    public void Update_beyond_window_after_last_played_is_stale_but_patched()
        => Assert.Equal(LibraryBuckets.StaleButPatched, BucketFor("stale_but_patched"));

    // The nine cases below all sit between the refund line and the retired
    // floor, so "not stale" now reads `bounced` rather than `active`: Bounced
    // off spans that whole band and `active` is only the unknown-minutes
    // residue. What each one asserts is unchanged — a leaking signal would show
    // up as `stale_but_patched` exactly as before.

    [Fact]
    public void Update_within_window_is_not_stale()
        => Assert.Equal(LibraryBuckets.Bounced, BucketFor("active_recent_update"));

    [Fact]
    public void High_playtime_is_retired_even_when_stale_shaped()
        => Assert.Equal(LibraryBuckets.Retired, BucketFor("retired"));

    [Fact]
    public void Only_latest_play_record_determines_bucket()
        => Assert.Equal(LibraryBuckets.Bounced, BucketFor("superseded_record"));

    /// <summary>
    /// §4.5 and pitfall 4, the product's differentiating feature: a depot push
    /// fires on DRM bumps, localization files and one-line hotfixes. Alone it
    /// must never claim a game was patched since you played it.
    /// </summary>
    [Fact]
    public void A_lone_build_push_is_not_a_major_update()
        => Assert.Equal(LibraryBuckets.Bounced, BucketFor("lone_build_push"));

    [Fact]
    public void A_lone_announcement_is_not_a_major_update()
        => Assert.Equal(LibraryBuckets.Bounced, BucketFor("lone_announcement"));

    [Fact]
    public void Both_signals_outside_the_correlation_window_are_not_one_update()
        => Assert.Equal(LibraryBuckets.Bounced, BucketFor("uncorrelated_signals"));

    [Fact]
    public void Signals_exactly_at_the_correlation_window_still_correlate()
        => Assert.Equal(LibraryBuckets.StaleButPatched, BucketFor("correlation_window_edge"));

    [Fact]
    public async Task Correlation_window_is_a_parameter_too()
    {
        // Widen the window past the 45-day gap and the uncorrelated pair becomes
        // one update: retuning the heuristic never touches stored data (§4.5).
        var retuned = await new LibraryQueryRepository(_db.Factory)
            .GetOwnershipBucketsAsync(Thresholds with { UpdateCorrelationWindowDays = 60 });

        var row = Assert.Single(retuned, b => b.OwnershipId == _ownershipsByCase["uncorrelated_signals"]);
        Assert.Equal(LibraryBuckets.StaleButPatched, row.Bucket);

        // Narrowing below the 2-day gap un-correlates the genuine pair.
        var narrowed = await new LibraryQueryRepository(_db.Factory)
            .GetOwnershipBucketsAsync(Thresholds with { UpdateCorrelationWindowDays = 1 });

        var stale = Assert.Single(narrowed, b => b.OwnershipId == _ownershipsByCase["stale_but_patched"]);
        Assert.Equal(LibraryBuckets.Bounced, stale.Bucket);
    }

    /// <summary>
    /// The 86400 sentinel means "played before Steam tracked timestamps" — the
    /// ancient pile. Classifying unknown-but-certainly-old as active excluded it
    /// from the one bucket built to resurface it.
    /// </summary>
    [Fact]
    public void Unknown_last_played_with_playtime_is_eligible_for_stale()
        => Assert.Equal(LibraryBuckets.StaleButPatched, BucketFor("null_last_played_patched"));

    [Fact]
    public void Unknown_last_played_without_an_update_is_not_stale()
        => Assert.Equal(LibraryBuckets.Bounced, BucketFor("null_last_played_no_update"));

    [Fact]
    public void Observations_in_the_same_second_break_the_tie_by_highest_id()
        => Assert.Equal(LibraryBuckets.Bounced, BucketFor("same_second_tie"));

    [Fact]
    public async Task Bucket_query_and_play_record_repository_agree_on_the_latest_record()
    {
        // The same tie, resolved by both readers. They disagreed before: the
        // query used a bare column beside MAX(), which SQLite may satisfy from
        // any row of the tie.
        var ownershipId = _ownershipsByCase["same_second_tie"];
        var latest = await new PlayRecordRepository(_db.Factory).GetLatestAsync(ownershipId);

        Assert.NotNull(latest);
        Assert.Equal(500, latest.PlaytimeMinutes);
        Assert.Equal(latest.PlaytimeMinutes,
            Assert.Single(_buckets, b => b.OwnershipId == ownershipId).PlaytimeMinutes);
    }

    [Fact]
    public void A_last_played_date_without_minutes_is_not_never_played_or_bounced()
    {
        // Unknown minutes are not zero minutes and not few minutes — so this row
        // is the one thing left in `active`, the residue bucket.
        var bucket = BucketFor("date_without_minutes");
        Assert.NotEqual(LibraryBuckets.NeverPlayed, bucket);
        Assert.NotEqual(LibraryBuckets.Bounced, bucket);
        Assert.Equal(LibraryBuckets.Active, bucket);
    }

    [Fact]
    public async Task Thresholds_are_parameters_not_stored_state()
    {
        var repository = new LibraryQueryRepository(_db.Factory);

        // Raise the refund line past 500 and the 500-minute game stops counting
        // as played at all.
        var raised = await repository
            .GetOwnershipBucketsAsync(Thresholds with { BouncedFloorMinutes = 601 });

        Assert.Equal(
            LibraryBuckets.NeverPlayed,
            Assert.Single(raised, b => b.OwnershipId == _ownershipsByCase["superseded_record"]).Bucket);

        // Lower it below 119 and the 119-minute game crosses the other way.
        // Same rows, same stored data, opposite answers.
        var lowered = await repository
            .GetOwnershipBucketsAsync(Thresholds with { BouncedFloorMinutes = 60 });

        Assert.Equal(
            LibraryBuckets.Bounced,
            Assert.Single(lowered, b => b.OwnershipId == _ownershipsByCase["refund_119"]).Bucket);
    }
}
