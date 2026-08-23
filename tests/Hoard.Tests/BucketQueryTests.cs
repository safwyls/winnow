using Hoard.Core.Domain;
using Hoard.Core.Queries;
using Hoard.Data.Repositories;
using Xunit;

namespace Hoard.Tests;

/// <summary>
/// §6.1 derived-bucket query against seeded data. Thresholds here are the
/// query's parameters, proving buckets are derived, not stored:
/// bounced ceiling 120 min, retired floor 3000 min, stale window 3 months.
/// </summary>
public class BucketQueryTests : IAsyncLifetime, IDisposable
{
    private static readonly BucketThresholds Thresholds = new(
        BouncedCeilingMinutes: 120,
        RetiredFloorMinutes: 3000,
        StaleWindowMonths: 3);

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
        //    2024-01-15, update event 2024-08-01: >3 months after.
        var staleRelease = await SeedAsync("stale_but_patched");
        await PlayAsync("stale_but_patched", 600, Utc(2024, 1, 15), Utc(2026, 8, 1));
        await updates.InsertAsync(new UpdateEvent
        {
            ReleaseId = staleRelease,
            Kind = UpdateEventKinds.BuildPush,
            BuildId = "42",
            OccurredAt = Utc(2024, 8, 1),
        });

        // 6. Not stale — update landed within the window (2 months after
        //    last played < 3-month window), so still active.
        var freshRelease = await SeedAsync("active_recent_update");
        await PlayAsync("active_recent_update", 600, Utc(2026, 4, 1), Utc(2026, 8, 1));
        await updates.InsertAsync(new UpdateEvent
        {
            ReleaseId = freshRelease,
            Kind = UpdateEventKinds.BuildPush,
            BuildId = "43",
            OccurredAt = Utc(2026, 6, 1),
        });

        // 7. Retired — high playtime (6000 >= 3000). Also has a stale-shaped
        //    update to prove retired outranks stale.
        var retiredRelease = await SeedAsync("retired");
        await PlayAsync("retired", 6000, Utc(2023, 1, 1), Utc(2026, 8, 1));
        await updates.InsertAsync(new UpdateEvent
        {
            ReleaseId = retiredRelease,
            Kind = UpdateEventKinds.Announcement,
            OccurredAt = Utc(2025, 1, 1),
            Title = "Big patch",
        });

        // 8. Stale precedence check — only the LATEST play record counts:
        //    an old bounced-level record is superseded by a newer one.
        await SeedAsync("superseded_record");
        await PlayAsync("superseded_record", 30, Utc(2025, 1, 1), Utc(2025, 1, 2));
        await PlayAsync("superseded_record", 500, Utc(2026, 7, 1), Utc(2026, 7, 2));

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
