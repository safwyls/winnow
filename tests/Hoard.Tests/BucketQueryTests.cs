using Hoard.Core.Domain;
using Hoard.Core.Queries;
using Hoard.Data.Repositories;
using Xunit;

namespace Hoard.Tests;

/// <summary>
/// §6.1 derived-bucket query against seeded data. Thresholds here are the
/// query's parameters, proving buckets are derived, not stored:
/// bounced ceiling 120 min, retired floor 3000 min, stale window 3 months,
/// update correlation window 7 days.
/// </summary>
public class BucketQueryTests : IAsyncLifetime, IDisposable
{
    private static readonly BucketThresholds Thresholds = new(
        BouncedCeilingMinutes: 120,
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

        // 1. Never touched — no play record at all.
        await SeedAsync("never_no_record");

        // 2. Never touched — a record exists but playtime is zero.
        await SeedAsync("never_zero_playtime");
        await PlayAsync("never_zero_playtime", 0, null, Utc(2026, 8, 1));

        // 3. Bounced — sub-threshold playtime (45 < 120).
        await SeedAsync("bounced");
        await PlayAsync("bounced", 45, Utc(2024, 5, 10), Utc(2026, 8, 1));

        // 4. Boundary — playtime exactly at the bounced ceiling is NOT
        //    bounced (§6.1: 0 < playtime < threshold), and with no update
        //    events it is active.
        await SeedAsync("boundary_at_ceiling");
        await PlayAsync("boundary_at_ceiling", 120, Utc(2026, 7, 1), Utc(2026, 8, 1));

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
    public void Zero_playtime_is_never_touched_with_or_without_a_record()
    {
        Assert.Equal(LibraryBuckets.NeverTouched, BucketFor("never_no_record"));
        Assert.Equal(LibraryBuckets.NeverTouched, BucketFor("never_zero_playtime"));
    }

    [Fact]
    public void Sub_threshold_playtime_is_bounced()
        => Assert.Equal(LibraryBuckets.Bounced, BucketFor("bounced"));

    [Fact]
    public void Playtime_exactly_at_ceiling_is_not_bounced()
        => Assert.Equal(LibraryBuckets.Active, BucketFor("boundary_at_ceiling"));

    [Fact]
    public void Update_beyond_window_after_last_played_is_stale_but_patched()
        => Assert.Equal(LibraryBuckets.StaleButPatched, BucketFor("stale_but_patched"));

    [Fact]
    public void Update_within_window_is_not_stale()
        => Assert.Equal(LibraryBuckets.Active, BucketFor("active_recent_update"));

    [Fact]
    public void High_playtime_is_retired_even_when_stale_shaped()
        => Assert.Equal(LibraryBuckets.Retired, BucketFor("retired"));

    [Fact]
    public void Only_latest_play_record_determines_bucket()
        => Assert.Equal(LibraryBuckets.Active, BucketFor("superseded_record"));

    /// <summary>
    /// §4.5 and pitfall 4, the product's differentiating feature: a depot push
    /// fires on DRM bumps, localization files and one-line hotfixes. Alone it
    /// must never claim a game was patched since you played it.
    /// </summary>
    [Fact]
    public void A_lone_build_push_is_not_a_major_update()
        => Assert.Equal(LibraryBuckets.Active, BucketFor("lone_build_push"));

    [Fact]
    public void A_lone_announcement_is_not_a_major_update()
        => Assert.Equal(LibraryBuckets.Active, BucketFor("lone_announcement"));

    [Fact]
    public void Both_signals_outside_the_correlation_window_are_not_one_update()
        => Assert.Equal(LibraryBuckets.Active, BucketFor("uncorrelated_signals"));

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
        Assert.Equal(LibraryBuckets.Active, stale.Bucket);
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
        => Assert.Equal(LibraryBuckets.Active, BucketFor("null_last_played_no_update"));

    [Fact]
    public void Observations_in_the_same_second_break_the_tie_by_highest_id()
        => Assert.Equal(LibraryBuckets.Active, BucketFor("same_second_tie"));

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
    public void A_last_played_date_without_minutes_is_not_never_touched_or_bounced()
    {
        // Unknown minutes are not zero minutes and not few minutes.
        var bucket = BucketFor("date_without_minutes");
        Assert.NotEqual(LibraryBuckets.NeverTouched, bucket);
        Assert.NotEqual(LibraryBuckets.Bounced, bucket);
        Assert.Equal(LibraryBuckets.Active, bucket);
    }

    [Fact]
    public async Task Thresholds_are_parameters_not_stored_state()
    {
        // Same data, wider bounce ceiling: the 500-minute game now bounces.
        var retuned = await new LibraryQueryRepository(_db.Factory)
            .GetOwnershipBucketsAsync(Thresholds with { BouncedCeilingMinutes = 601 });

        var row = Assert.Single(retuned, b => b.OwnershipId == _ownershipsByCase["superseded_record"]);
        Assert.Equal(LibraryBuckets.Bounced, row.Bucket);
    }
}
