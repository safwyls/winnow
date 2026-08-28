using System.Net;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Enrich.Updates;
using Winnow.Enrich.Updates.Storage;
using Xunit;

namespace Winnow.Tests.Updates;

/// <summary>
/// The whole feature, end to end: canned HTTP responses in, a
/// <c>stale_but_patched</c> bucket out — through the real poller, the real
/// migrations, and the real <see cref="LibraryQueryRepository"/> bucket query.
///
/// <para>This is the test that proves M2 does what M2 is for. Every other test
/// here checks one link; this one checks that the links join, and in particular
/// that the rows this module writes are the rows §4.5's correlation is looking
/// for. The correlation itself is deliberately NOT re-implemented here — it
/// lives in the bucket query at read time so the heuristic can be retuned
/// without re-fetching, and asserting against the real query is the only way to
/// know the two agree.</para>
///
/// <para>The negative case is pitfall 4, ranked fourth of nine in §9: "Treating
/// <c>depots.branches.public.timeupdated</c> as 'major update.' It fires on
/// trivial pushes." The spike measured that on four real games and a lone-push
/// heuristic would have badged Dota 2 and Elden Ring wrongly, two out of four.
/// A badge that lies is worse than no badge.</para>
/// </summary>
public class UpdateCorrelationEndToEndTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The shipped defaults: bounced &lt; 120 min, retired ≥ 6000, stale &gt; 6 months, window ±7 days.</summary>
    private static readonly BucketThresholds Thresholds = BucketThresholds.Default;

    private readonly TempDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Correlated_build_push_and_announcement_produce_stale_but_patched()
    {
        // Stardew Valley's real shape from the spike: the announcement landed
        // 2026-05-20, the build two days LATER on 2026-05-22 — which is why the
        // correlation window has to be symmetric and measured in days, not hours.
        var announcedAt = Now.AddDays(-26).UtcDateTime;
        var builtAt = Now.AddDays(-24).UtcDateTime;

        // Last played fourteen months ago: far enough back that the patch is
        // genuinely news to this player, which is what §6.1's StaleWindowMonths
        // requires on top of the correlation.
        var release = await SeedAsync(
            UpdateFixtures.StardewAppId, playtimeMinutes: 900, lastPlayed: Now.AddMonths(-14).UtcDateTime);

        using var host = Host(
            (request, _) => request.Host == UpdateHost.SteamNews
                ? FakeUpdateHandler.Json(
                    HttpStatusCode.OK,
                    UpdateFixtures.News(request.AppId, announcedAt, "gid-1615", "Stardew Valley 1.6.15 Patch"))
                : FakeUpdateHandler.Json(
                    HttpStatusCode.OK, UpdateFixtures.BuildInfo(request.AppId, builtAt)));

        var report = await host.Poller.PollDueBatchAsync();

        Assert.Equal(1, report.AnnouncementsRecorded);
        Assert.Equal(1, report.BuildPushesRecorded);

        var bucket = await BucketFor(release);

        // The differentiating feature, non-zero at last.
        Assert.Equal("stale_but_patched", bucket.Bucket);
    }

    [Fact]
    public async Task A_lone_build_push_does_not_produce_stale_but_patched()
    {
        // Dota 2's real shape, brought inside the cascade gate: a fresh depot
        // push with the newest patch note far enough behind it that the two
        // cannot be the same event. §4.5's noise claim, verified — the push is a
        // DRM bump, a localization file or a one-line hotfix, and announcing
        // "MAJOR UPDATE" on the strength of one is the most visible way this
        // feature can lie.
        //
        // Twenty-three days apart rather than Dota 2's real 53, because at 53
        // the poller never asks steamcmd.net at all (an announcement older than
        // CascadeMaxAnnouncementAgeDays cannot correlate with the app's LATEST
        // push, so the call is skipped) and there would be no build row to prove
        // anything about. That path has its own test; this one is about what the
        // bucket query does when both rows exist and do not pair.
        var announcedAt = Now.AddDays(-25).UtcDateTime;
        var builtAt = Now.AddDays(-2).UtcDateTime;

        var release = await SeedAsync(
            UpdateFixtures.DotaAppId, playtimeMinutes: 900, lastPlayed: Now.AddMonths(-14).UtcDateTime);

        using var host = Host(
            (request, _) => request.Host == UpdateHost.SteamNews
                ? FakeUpdateHandler.Json(
                    HttpStatusCode.OK, UpdateFixtures.News(request.AppId, announcedAt, "gid-old"))
                : FakeUpdateHandler.Json(
                    HttpStatusCode.OK, UpdateFixtures.BuildInfo(request.AppId, builtAt)));

        await host.Poller.PollDueBatchAsync();

        // Both raw signals ARE stored — §4.5 requires that, so the heuristic can
        // be retuned without re-fetching — and they are 23 days apart, well
        // outside the ±7-day window, so the read-time correlation correctly
        // refuses to pair them.
        var stored = await new UpdateEventRepository(_db.Factory).GetByReleaseAsync(release);
        Assert.Equal(2, stored.Count);

        var bucket = await BucketFor(release);
        Assert.NotEqual("stale_but_patched", bucket.Bucket);
    }

    [Fact]
    public async Task A_never_opened_game_is_never_polled_and_never_badged()
    {
        var release = await SeedAsync(UpdateFixtures.PortalAppId, playtimeMinutes: 0, lastPlayed: null);

        // No candidate override: the real eligibility query decides, and it
        // reads the same library the bucket query does.
        using var host = new UpdateSignalTestHost(
            (request, _) => UpdateSignalTestHost.Unarranged(request),
            configure: options => options.SweepPeriodDays = 1,
            database: _db,
            cache: new SqliteUpdateSignalCache(_db.Factory),
            now: Now);

        var report = await host.Poller.PollDueBatchAsync();

        Assert.Equal(0, report.Eligible);
        Assert.Empty(host.Handler.Requests);

        var bucket = await BucketFor(release);
        Assert.Equal("never_played", bucket.Bucket);
    }

    [Fact]
    public async Task A_correlated_pair_the_player_has_already_seen_is_not_stale()
    {
        // Both signals fire and correlate, but the player was there for it. The
        // badge is "patched since YOU last played", so the read-time comparison
        // against last-played is what makes an event a badge — not the event
        // itself. This is also why the poller records a baseline observation
        // rather than swallowing it: the filtering happens here, on evidence.
        var announcedAt = Now.AddDays(-20).UtcDateTime;
        var builtAt = Now.AddDays(-19).UtcDateTime;

        var release = await SeedAsync(
            UpdateFixtures.PortalAppId, playtimeMinutes: 900, lastPlayed: Now.AddDays(-5).UtcDateTime);

        using var host = Host(
            (request, _) => request.Host == UpdateHost.SteamNews
                ? FakeUpdateHandler.Json(
                    HttpStatusCode.OK, UpdateFixtures.News(request.AppId, announcedAt, "gid-1"))
                : FakeUpdateHandler.Json(
                    HttpStatusCode.OK, UpdateFixtures.BuildInfo(request.AppId, builtAt)));

        await host.Poller.PollDueBatchAsync();

        // 900 minutes with nothing to be behind on: past the refund line, short
        // of the retired floor, so "not stale" reads `bounced` (§6.1 — Bounced
        // off now spans that whole band). Any leak would still show up here as
        // `stale_but_patched`, which is the assertion that matters.
        var bucket = await BucketFor(release);
        Assert.Equal("bounced", bucket.Bucket);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private UpdateSignalTestHost Host(
        Func<RecordedUpdateRequest, int, HttpResponseMessage> responder)
        => new(
            responder,
            configure: options => options.SweepPeriodDays = 1,
            database: _db,
            cache: new SqliteUpdateSignalCache(_db.Factory),
            now: Now,
            candidates: null);

    private async Task<OwnershipBucket> BucketFor(long releaseId)
    {
        var buckets = await new LibraryQueryRepository(_db.Factory).GetOwnershipBucketsAsync(Thresholds);
        return Assert.Single(buckets, b => b.ReleaseId == releaseId);
    }

    private async Task<long> SeedAsync(string appId, long playtimeMinutes, DateTime? lastPlayed)
    {
        var works = new WorkRepository(_db.Factory);
        var releases = new ReleaseRepository(_db.Factory);
        var ownerships = new OwnershipRepository(_db.Factory);
        var plays = new PlayRecordRepository(_db.Factory);

        var workId = await works.InsertAsync(new Work { Name = "App " + appId });
        var releaseId = await releases.InsertAsync(new Release
        {
            WorkId = workId,
            Name = "App " + appId,
            Platform = "windows",
        });

        await releases.AddExternalIdAsync(new ExternalId
        {
            ReleaseId = releaseId,
            Provider = ExternalIdProviders.Steam,
            ProviderId = appId,
        });

        var ownershipId = await ownerships.InsertAsync(new Ownership
        {
            ReleaseId = releaseId,
            Store = "steam",
        });

        await plays.InsertAsync(new PlayRecord
        {
            OwnershipId = ownershipId,
            PlaytimeMinutes = playtimeMinutes,
            LastPlayedAt = lastPlayed,
            Source = "steam_local",
            ObservedAt = Now.UtcDateTime,
        });

        return releaseId;
    }
}
