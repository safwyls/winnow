using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// "I've seen this patch" (migration 0012): the user dismissing
/// design-system.md §5.2's unread dot until a genuinely newer update arrives.
///
/// <para>The behaviour under test is that the dismissal is a WATERMARK rather
/// than a flag, and therefore that a newer correlated push re-raises the badge
/// <b>with no write anywhere</b> — the same read-time-evaluation discipline
/// that lets a lapsed snooze re-admit its game for free. Every assertion below
/// that says "back in stale_but_patched" is also asserting that nothing had to
/// notice, reset or clear anything to put it there.</para>
///
/// <para>The bucket half of these tests goes through
/// <see cref="LibraryQueryRepository"/> on purpose. §5.2 makes the badge
/// identical to <c>stale_but_patched</c> membership, so that query is the only
/// place the acknowledgement is applied and the only place worth asserting
/// on — the tile badge, the rail count, the filter chip, the recommender's
/// bucket bonus and the feed's patched_while_away shelf all read it.</para>
/// </summary>
public class UpdateAcknowledgementTests : IDisposable
{
    /// <summary>
    /// The bucket query's parameters, matching <see cref="BucketQueryTests"/>:
    /// refund line 120, retired floor 3000, stale window 3 months, correlation
    /// window 7 days. Every seeded game below has 600 minutes on it, so the
    /// bucket it falls back to when the badge is suppressed is `bounced` — the
    /// playtime bucket it would occupy if update_events were empty.
    /// </summary>
    private static readonly BucketThresholds Thresholds = new(
        BouncedFloorMinutes: 120,
        RetiredFloorMinutes: 3000,
        StaleWindowMonths: 3,
        UpdateCorrelationWindowDays: 7);

    private static readonly DateTime LastPlayed = new(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Observed = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>The push that flags the seeded game, and the watermark a dismissal records.</summary>
    private static readonly DateTime FirstPush = new(2024, 8, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly TempDatabase _db = new();

    private readonly WorkRepository _works;
    private readonly ReleaseRepository _releases;
    private readonly OwnershipRepository _ownerships;
    private readonly PlayRecordRepository _plays;
    private readonly UpdateEventRepository _updates;
    private readonly UpdateAcknowledgementRepository _acks;
    private readonly LibraryQueryRepository _library;

    public UpdateAcknowledgementTests()
    {
        _works = new WorkRepository(_db.Factory);
        _releases = new ReleaseRepository(_db.Factory);
        _ownerships = new OwnershipRepository(_db.Factory);
        _plays = new PlayRecordRepository(_db.Factory);
        _updates = new UpdateEventRepository(_db.Factory);
        _acks = new UpdateAcknowledgementRepository(_db.Factory);
        _library = new LibraryQueryRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    // ── Storage semantics ───────────────────────────────────────────────────

    [Fact]
    public async Task Acknowledgement_round_trips_and_undo_is_a_revocation_not_a_deletion()
    {
        var (releaseId, _) = await SeedGameAsync("Riven", playtimeMinutes: 600);

        var id = await _acks.RecordAsync(new UpdateAcknowledgement
        {
            ReleaseId = releaseId,
            AcknowledgedThrough = FirstPush,
            CreatedAt = Now,
        });

        var standing = await _acks.GetStandingAsync(releaseId);
        Assert.NotNull(standing);
        Assert.Equal(id, standing.Id);
        Assert.Equal(releaseId, standing.ReleaseId);
        Assert.Equal(FirstPush, standing.AcknowledgedThrough);
        Assert.Equal(Now, standing.CreatedAt);
        Assert.Null(standing.RevokedAt);
        Assert.True(standing.IsStanding);

        var revoked = await _acks.RevokeAsync(releaseId, Now.AddDays(1));
        Assert.Equal(1, revoked);
        Assert.Null(await _acks.GetStandingAsync(releaseId));

        // Revoked, not erased: the row and its stamp survive, so what the user
        // told the system — and took back — stays inspectable (0011's rule).
        using var conn = _db.Factory.Open();
        Assert.Equal(1, conn.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM update_acknowledgements WHERE id = @id;", new { id }));
        Assert.Equal("2026-08-29 12:00:00", conn.ExecuteScalar<string>(
            "SELECT revoked_at FROM update_acknowledgements WHERE id = @id;", new { id }));
    }

    [Fact]
    public async Task Revoking_nothing_is_not_an_error()
    {
        var (releaseId, _) = await SeedGameAsync("Riven", playtimeMinutes: 600);

        // The badge may already have been re-raised by a newer push, leaving
        // nothing to take back. The user's undo is then a no-op that still
        // leaves them looking at the badge they wanted.
        Assert.Equal(0, await _acks.RevokeAsync(releaseId, Now));
        Assert.Null(await _acks.GetStandingAsync(releaseId));
    }

    [Fact]
    public async Task Repeated_dismissals_accumulate_and_the_highest_watermark_stands()
    {
        var (releaseId, _) = await SeedGameAsync("Riven", playtimeMinutes: 600);

        await _acks.RecordAsync(new UpdateAcknowledgement
        {
            ReleaseId = releaseId,
            AcknowledgedThrough = FirstPush,
            CreatedAt = Now.AddDays(-60),
        });
        var laterId = await _acks.RecordAsync(new UpdateAcknowledgement
        {
            ReleaseId = releaseId,
            AcknowledgedThrough = FirstPush.AddYears(1),
            CreatedAt = Now,
        });

        // Append, never upsert: the first row is still there, and the second
        // wins by carrying the greater watermark rather than by overwriting it.
        var standing = await _acks.GetStandingAsync(releaseId);
        Assert.Equal(laterId, standing?.Id);

        using var conn = _db.Factory.Open();
        Assert.Equal(2, conn.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM update_acknowledgements WHERE release_id = @releaseId;",
            new { releaseId }));

        // And undo takes back EVERY standing row. Revoking only the newest
        // would silently fall back to the older watermark, which still
        // suppresses part of the history the user just asked to see again.
        Assert.Equal(2, await _acks.RevokeAsync(releaseId, Now.AddDays(1)));
        Assert.Null(await _acks.GetStandingAsync(releaseId));
    }

    [Fact]
    public async Task Acknowledgements_are_scoped_to_one_release()
    {
        var (dismissed, _) = await SeedGameAsync("Riven", playtimeMinutes: 600);
        var (other, _) = await SeedGameAsync("Myst", playtimeMinutes: 600);

        await _acks.RecordAsync(new UpdateAcknowledgement
        {
            ReleaseId = dismissed,
            AcknowledgedThrough = FirstPush,
            CreatedAt = Now,
        });

        Assert.NotNull(await _acks.GetStandingAsync(dismissed));
        Assert.Null(await _acks.GetStandingAsync(other));
        Assert.Equal(0, await _acks.RevokeAsync(other, Now));
        Assert.NotNull(await _acks.GetStandingAsync(dismissed));
    }

    // ── The bucket query, which is the whole of the feature ─────────────────

    [Fact]
    public async Task A_patched_game_leaves_the_stale_bucket_once_acknowledged()
    {
        var (releaseId, ownershipId) = await SeedPatchedGameAsync("Riven");

        Assert.Equal(LibraryBuckets.StaleButPatched, await BucketAsync(ownershipId));

        await _acks.RecordAsync(new UpdateAcknowledgement
        {
            ReleaseId = releaseId,
            AcknowledgedThrough = FirstPush,
            CreatedAt = Now,
        });

        // And lands in the playtime bucket it would otherwise occupy. Not
        // hidden, not `active`, not a new bucket name: the dismissal removes one
        // reason to surface the game, and the §6.1 CASE falls through to the
        // next one exactly as it does for a game that was never patched.
        Assert.Equal(LibraryBuckets.Bounced, await BucketAsync(ownershipId));
    }

    /// <summary>
    /// The watermark is inclusive on its own instant: the dismissed push is the
    /// one whose <c>occurred_at</c> was recorded, so "at or before" must
    /// exclude it. An exclusive comparison would leave the badge lit
    /// immediately after the click — the feature failing in the most visible
    /// way it can.
    /// </summary>
    [Fact]
    public async Task The_dismissed_push_itself_is_excluded_at_the_boundary()
    {
        var (releaseId, ownershipId) = await SeedPatchedGameAsync("Riven");

        await _acks.RecordAsync(new UpdateAcknowledgement
        {
            ReleaseId = releaseId,
            AcknowledgedThrough = FirstPush,
            CreatedAt = Now,
        });
        Assert.Equal(LibraryBuckets.Bounced, await BucketAsync(ownershipId));

        // One second under the push is NOT enough — that push is still newer
        // than the watermark and still flags the release.
        await _acks.RevokeAsync(releaseId, Now);
        await _acks.RecordAsync(new UpdateAcknowledgement
        {
            ReleaseId = releaseId,
            AcknowledgedThrough = FirstPush.AddSeconds(-1),
            CreatedAt = Now,
        });
        Assert.Equal(LibraryBuckets.StaleButPatched, await BucketAsync(ownershipId));
    }

    [Fact]
    public async Task A_newer_correlated_push_re_raises_the_flag_with_no_further_write()
    {
        var (releaseId, ownershipId) = await SeedPatchedGameAsync("Riven");

        await _acks.RecordAsync(new UpdateAcknowledgement
        {
            ReleaseId = releaseId,
            AcknowledgedThrough = FirstPush,
            CreatedAt = Now,
        });
        Assert.Equal(LibraryBuckets.Bounced, await BucketAsync(ownershipId));

        // A genuinely newer major update: build push AND announcement inside the
        // 7-day correlation window, both after the watermark. This is the only
        // write — into update_events, by the poller, which knows nothing about
        // acknowledgements.
        await BuildPushAsync(releaseId, new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        await AnnouncementAsync(releaseId, new DateTime(2025, 3, 3, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(LibraryBuckets.StaleButPatched, await BucketAsync(ownershipId));

        // Nothing touched the acknowledgement to make that happen. It is still
        // there, still standing, still carrying its original watermark — the
        // badge came back because the comparison is made at READ time, which is
        // the entire reason this is an instant and not a boolean.
        var standing = await _acks.GetStandingAsync(releaseId);
        Assert.NotNull(standing);
        Assert.Equal(FirstPush, standing.AcknowledgedThrough);
        Assert.Null(standing.RevokedAt);

        using var conn = _db.Factory.Open();
        Assert.Equal(1, conn.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM update_acknowledgements WHERE release_id = @releaseId;",
            new { releaseId }));
    }

    /// <summary>
    /// §4.5 survives the feature: a lone depot push after the watermark is a DRM
    /// bump, a localization file or a one-line hotfix, and it may not re-raise a
    /// badge the user dismissed any more than it may raise one in the first
    /// place. If this ever passes, the acknowledgement filter has been applied
    /// after the correlation instead of before it.
    /// </summary>
    [Fact]
    public async Task An_uncorrelated_newer_push_does_not_re_raise_the_flag()
    {
        var (releaseId, ownershipId) = await SeedPatchedGameAsync("Riven");

        await _acks.RecordAsync(new UpdateAcknowledgement
        {
            ReleaseId = releaseId,
            AcknowledgedThrough = FirstPush,
            CreatedAt = Now,
        });
        Assert.Equal(LibraryBuckets.Bounced, await BucketAsync(ownershipId));

        // A build push with no announcement anywhere near it.
        await BuildPushAsync(releaseId, new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(LibraryBuckets.Bounced, await BucketAsync(ownershipId));

        // ...and an announcement 45 days later is a different event, not this
        // push's patch notes. Still not a major update, still no badge.
        await AnnouncementAsync(releaseId, new DateTime(2025, 4, 15, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(LibraryBuckets.Bounced, await BucketAsync(ownershipId));

        // The dismissed push must not be allowed to lend its own announcement to
        // the new one either — the acknowledgement drops it from the CTE before
        // the correlation EXISTS runs, so it is not in the query at all.
        Assert.Equal(LibraryBuckets.Bounced, await BucketAsync(ownershipId));
    }

    [Fact]
    public async Task Revoking_the_acknowledgement_restores_the_bucket()
    {
        var (releaseId, ownershipId) = await SeedPatchedGameAsync("Riven");

        await _acks.RecordAsync(new UpdateAcknowledgement
        {
            ReleaseId = releaseId,
            AcknowledgedThrough = FirstPush,
            CreatedAt = Now,
        });
        Assert.Equal(LibraryBuckets.Bounced, await BucketAsync(ownershipId));

        await _acks.RevokeAsync(releaseId, Now.AddMinutes(1));

        // "Standing" is revoked_at IS NULL, asked inside the query — so undo is
        // one stamp and the badge is back on the next read, with the update
        // events never having been touched.
        Assert.Equal(LibraryBuckets.StaleButPatched, await BucketAsync(ownershipId));
    }

    [Fact]
    public async Task An_acknowledgement_on_a_game_that_was_never_patched_changes_nothing()
    {
        // 600 minutes, no update events at all: `bounced` before and after.
        var (releaseId, ownershipId) = await SeedGameAsync("Myst", playtimeMinutes: 600);
        Assert.Equal(LibraryBuckets.Bounced, await BucketAsync(ownershipId));

        await _acks.RecordAsync(new UpdateAcknowledgement
        {
            ReleaseId = releaseId,
            AcknowledgedThrough = FirstPush,
            CreatedAt = Now,
        });

        Assert.Equal(LibraryBuckets.Bounced, await BucketAsync(ownershipId));
    }

    [Fact]
    public async Task One_release_dismissed_does_not_dismiss_another()
    {
        var (dismissedRelease, dismissedOwnership) = await SeedPatchedGameAsync("Riven");
        var (_, untouchedOwnership) = await SeedPatchedGameAsync("Myst");

        await _acks.RecordAsync(new UpdateAcknowledgement
        {
            ReleaseId = dismissedRelease,
            AcknowledgedThrough = FirstPush,
            CreatedAt = Now,
        });

        // The CTE groups by release_id; a missing row must read as "nothing
        // dismissed", never as a watermark of zero or of now. The LEFT JOIN's
        // NULL branch is what guarantees that, and this is the row that proves
        // it: every other game in a library has no acknowledgement at all.
        Assert.Equal(LibraryBuckets.Bounced, await BucketAsync(dismissedOwnership));
        Assert.Equal(LibraryBuckets.StaleButPatched, await BucketAsync(untouchedOwnership));
    }

    [Fact]
    public async Task A_retired_game_is_still_retired_after_a_dismissal()
    {
        // Precedence is unchanged: `retired` outranks `stale_but_patched`, so a
        // dismissal on a high-playtime game moves nothing. Guards against the
        // acknowledgement filter being mistaken for a bucket override.
        var (releaseId, ownershipId) = await SeedGameAsync("Skyrim", playtimeMinutes: 6000);
        await BuildPushAsync(releaseId, FirstPush);
        await AnnouncementAsync(releaseId, FirstPush.AddDays(2));
        Assert.Equal(LibraryBuckets.Retired, await BucketAsync(ownershipId));

        await _acks.RecordAsync(new UpdateAcknowledgement
        {
            ReleaseId = releaseId,
            AcknowledgedThrough = FirstPush,
            CreatedAt = Now,
        });

        Assert.Equal(LibraryBuckets.Retired, await BucketAsync(ownershipId));
    }

    // ── Fixture ─────────────────────────────────────────────────────────────

    private async Task<string> BucketAsync(long ownershipId)
    {
        var buckets = await _library.GetOwnershipBucketsAsync(Thresholds);
        return Assert.Single(buckets, b => b.OwnershipId == ownershipId).Bucket;
    }

    /// <summary>
    /// 600 minutes on the clock, last played 2024-01-15. Above the refund line
    /// and below the retired floor, so with no update events it is `bounced` —
    /// which makes `bounced` the honest "badge suppressed" answer for every
    /// dismissal test here.
    /// </summary>
    private async Task<(long ReleaseId, long OwnershipId)> SeedGameAsync(
        string name, long playtimeMinutes)
    {
        var workId = await _works.InsertAsync(new Work { Name = name, FirstReleaseYear = 2020 });
        var releaseId = await _releases.InsertAsync(new Release
        {
            WorkId = workId,
            Name = name,
            Platform = "windows",
        });
        var ownershipId = await _ownerships.InsertAsync(new Ownership
        {
            ReleaseId = releaseId,
            Store = "steam",
        });
        await _plays.InsertAsync(new PlayRecord
        {
            OwnershipId = ownershipId,
            PlaytimeMinutes = playtimeMinutes,
            LastPlayedAt = LastPlayed,
            Source = "steam_local",
            ObservedAt = Observed,
        });
        return (releaseId, ownershipId);
    }

    /// <summary>The same game plus one major update — build push and announcement two days apart.</summary>
    private async Task<(long ReleaseId, long OwnershipId)> SeedPatchedGameAsync(string name)
    {
        var seeded = await SeedGameAsync(name, playtimeMinutes: 600);
        await BuildPushAsync(seeded.ReleaseId, FirstPush);
        await AnnouncementAsync(seeded.ReleaseId, FirstPush.AddDays(2));
        return seeded;
    }

    private Task<long> BuildPushAsync(long releaseId, DateTime occurredAt)
        => _updates.InsertAsync(new UpdateEvent
        {
            ReleaseId = releaseId,
            Kind = UpdateEventKinds.BuildPush,
            BuildId = occurredAt.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            OccurredAt = occurredAt,
        });

    private Task<long> AnnouncementAsync(long releaseId, DateTime occurredAt)
        => _updates.InsertAsync(new UpdateEvent
        {
            ReleaseId = releaseId,
            Kind = UpdateEventKinds.Announcement,
            OccurredAt = occurredAt,
            Title = "Patch notes",
        });
}
