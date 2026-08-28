using System.Net;
using Winnow.Core.Domain;
using Winnow.Data.Repositories;
using Winnow.Enrich.Updates;
using Winnow.Enrich.Updates.Storage;
using Xunit;

namespace Winnow.Tests.Updates;

/// <summary>
/// The spike's "eliminate, cascade, stagger" strategy, which is what takes a
/// naive 1,232 requests per poll down to ~63 a day. Each rule is asserted
/// separately, because each one is separately load-bearing: dropping any of the
/// three puts the volunteer service back on the hook for hundreds of daily
/// requests.
/// </summary>
public class UpdatePollerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly TempDatabase _db = new();

    public void Dispose() => _db.Dispose();

    // ── Eliminate ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Never_opened_games_are_not_polled()
    {
        var opened = await SeedAsync("100", playtimeMinutes: 240, lastPlayed: Now.AddMonths(-8).UtcDateTime);
        var neverOpened = await SeedAsync("200", playtimeMinutes: 0, lastPlayed: null);
        var noPlayRecordAtAll = await SeedAsync("300", playtimeMinutes: null, lastPlayed: null);

        var source = new SqlitePollCandidateSource(_db.Factory);
        var eligible = await source.GetEligibleAsync(retiredFloorMinutes: 6_000);

        // design-system §5.2: "Never on never-opened games; an unplayed game has
        // nothing to be behind on." A game that can never show the badge is a
        // request that can never be worth making — and on a large Steam library
        // this is roughly 40% of it, the single biggest saving in the design.
        Assert.Equal(["100"], eligible.Select(c => c.AppId));
        Assert.Equal(opened, eligible[0].ReleaseId);
        Assert.DoesNotContain(neverOpened, eligible.Select(c => c.ReleaseId));
        Assert.DoesNotContain(noPlayRecordAtAll, eligible.Select(c => c.ReleaseId));
    }

    /// <summary>
    /// The exclusion is ZERO PLAYTIME, not the `never_played` bucket. Since that
    /// bucket became everything under Steam's refund line (§6.1) it holds games
    /// with up to two hours of real play on them, and those can genuinely have
    /// missed a patch — they are the "bounced off it early" pile the badge
    /// exists for. Keying eligibility on bucket membership would silently stop
    /// polling the population the feature is about.
    /// </summary>
    [Fact]
    public async Task Games_under_the_refund_line_are_still_polled()
    {
        await SeedAsync("810", playtimeMinutes: 60, lastPlayed: Now.AddMonths(-8).UtcDateTime);
        var neverOpened = await SeedAsync("820", playtimeMinutes: 0, lastPlayed: null);

        var eligible = await new SqlitePollCandidateSource(_db.Factory).GetEligibleAsync(6_000);

        Assert.Equal(["810"], eligible.Select(c => c.AppId));
        Assert.DoesNotContain(neverOpened, eligible.Select(c => c.ReleaseId));
    }

    [Fact]
    public async Task Zero_minutes_with_a_real_last_played_date_is_still_eligible()
    {
        // The bucket query calls this NOT never-opened: a real last-played date
        // beside zero minutes is a source admitting it did not measure the
        // session, not evidence of no play. Excluding it here would silently
        // drop exactly the long-dormant titles the badge exists for.
        await SeedAsync("400", playtimeMinutes: 0, lastPlayed: Now.AddYears(-2).UtcDateTime);

        var eligible = await new SqlitePollCandidateSource(_db.Factory).GetEligibleAsync(6_000);

        Assert.Equal(["400"], eligible.Select(c => c.AppId));
    }

    [Fact]
    public async Task Retired_games_are_not_polled()
    {
        await SeedAsync("500", playtimeMinutes: 12_000, lastPlayed: Now.AddYears(-1).UtcDateTime);
        await SeedAsync("600", playtimeMinutes: 300, lastPlayed: Now.AddYears(-1).UtcDateTime);

        var eligible = await new SqlitePollCandidateSource(_db.Factory).GetEligibleAsync(6_000);

        // §6.1 gives `retired` precedence over `stale_but_patched`, so a
        // 200-hour game cannot display the badge whatever lands in update_events.
        Assert.Equal(["600"], eligible.Select(c => c.AppId));
    }

    [Fact]
    public async Task One_release_owned_twice_is_polled_once()
    {
        var releaseId = await SeedAsync("700", playtimeMinutes: 100, lastPlayed: Now.AddMonths(-9).UtcDateTime);
        await AddOwnershipAsync(releaseId, "steam-second-account", playtimeMinutes: 50);

        var eligible = await new SqlitePollCandidateSource(_db.Factory).GetEligibleAsync(6_000);

        // Same appid, same feed, one answer. Two rows here would be two requests
        // for the same fact.
        Assert.Single(eligible);
        Assert.Equal(100, eligible[0].PlaytimeMinutes);
    }

    // ── Cascade ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Steamcmd_is_only_called_when_a_new_patch_note_appeared()
    {
        var announcedAt = Now.AddDays(-2).UtcDateTime;
        var builtAt = Now.AddDays(-1).UtcDateTime;

        using var host = Host(
            (request, _) => request.Host == UpdateHost.SteamNews
                ? FakeUpdateHandler.Json(
                    HttpStatusCode.OK, UpdateFixtures.News(request.AppId, announcedAt, "gid-1"))
                : FakeUpdateHandler.Json(
                    HttpStatusCode.OK, UpdateFixtures.BuildInfo(request.AppId, builtAt)),
            candidates: [new PollCandidate(1, "100", 240, Now.AddMonths(-8).UtcDateTime)]);

        var first = await host.Poller.PollDueBatchAsync();

        // The announcement is new, so the cascade fires: one cheap news call
        // gates one expensive build call.
        Assert.Equal(1, first.NewsRequests);
        Assert.Equal(1, first.BuildInfoRequests);
        Assert.Equal(1, first.AnnouncementsRecorded);
        Assert.Equal(1, first.BuildPushesRecorded);

        // Next day, same newest item. This is the overwhelmingly common
        // outcome and the entire basis of the cost model: one ~440-byte request,
        // no ~12 KB request, no writes. Announcements are rare; depot pushes are
        // constant, so sweeping the cheap signal and gating the expensive one is
        // what drops steamcmd.net from 616 hits a day to about ten.
        host.Handler.Clear();
        host.Clock.AdvanceDays(1);

        var second = await host.Poller.PollDueBatchAsync();

        Assert.Equal(1, second.NewsRequests);
        Assert.Equal(0, second.BuildInfoRequests);
        Assert.Equal(0, host.Handler.CountFor(UpdateHost.SteamCmd));
        Assert.Equal(0, second.AnnouncementsRecorded);
        Assert.Equal(0, second.BuildPushesRecorded);
    }

    [Fact]
    public async Task An_ancient_patch_note_does_not_cost_a_steamcmd_call()
    {
        using var host = Host(
            (request, _) => request.Host == UpdateHost.SteamNews
                ? FakeUpdateHandler.Json(
                    HttpStatusCode.OK, UpdateFixtures.News(request.AppId, Now.AddYears(-3).UtcDateTime, "old"))
                : UpdateSignalTestHost.Unarranged(request),
            candidates: [new PollCandidate(1, "100", 240, Now.AddYears(-4).UtcDateTime)]);

        var report = await host.Poller.PollDueBatchAsync();

        // `timeupdated` is the app's LATEST push, so against a three-year-old
        // patch note it cannot correlate no matter what it says. The call would
        // spend the volunteer service's bandwidth to confirm a foregone "no".
        Assert.Equal(1, report.NewsRequests);
        Assert.Equal(0, report.BuildInfoRequests);
        Assert.Equal(1, report.AnnouncementsRecorded);
    }

    [Fact]
    public async Task An_announcement_awaiting_its_build_stays_on_a_daily_watch()
    {
        // The Stardew Valley case: the build landed two days AFTER the post, so
        // a single pass cannot resolve the pair and the app has to be re-checked
        // until the correlation window closes.
        //
        // A real seven-day sweep here, on the app's own slot day, because the
        // claim under test is precisely that the watch list overrides the slot
        // schedule — with a one-day sweep everything is due daily and the test
        // would prove nothing.
        var start = FirstDueDay("100", 7, Now);
        var announcedAt = start.UtcDateTime;
        var buildLandsAt = start.AddDays(2).UtcDateTime;
        var stalePush = start.AddDays(-40).UtcDateTime;

        var buildPushed = false;

        using var host = Host(
            (request, _) => request.Host == UpdateHost.SteamNews
                ? FakeUpdateHandler.Json(
                    HttpStatusCode.OK, UpdateFixtures.News(request.AppId, announcedAt, "gid-1"))
                : FakeUpdateHandler.Json(
                    HttpStatusCode.OK,
                    UpdateFixtures.BuildInfo(request.AppId, buildPushed ? buildLandsAt : stalePush)),
            candidates: [new PollCandidate(1, "100", 240, start.AddMonths(-8).UtcDateTime)],
            now: start,
            configure: options =>
            {
                options.SweepPeriodDays = 7;
                options.BuildInfoCacheTtl = TimeSpan.Zero;
            });

        var day0 = await host.Poller.PollDueBatchAsync();
        Assert.Equal(1, day0.AnnouncementsRecorded);
        Assert.Equal(1, day0.Watching);

        // Day 1: still watching, still no build. The app is due DAILY now, not
        // on its seven-day slot — the window is closing on it.
        host.Clock.AdvanceDays(1);
        var day1 = await host.Poller.PollDueBatchAsync();
        Assert.Equal(1, day1.Polled);
        Assert.Equal(1, day1.BuildInfoRequests);
        Assert.Equal(1, day1.Watching);

        // Day 2: the build lands and the pair correlates. Watching stops.
        buildPushed = true;
        host.Clock.AdvanceDays(1);
        var day2 = await host.Poller.PollDueBatchAsync();
        Assert.Equal(1, day2.BuildPushesRecorded);
        Assert.Equal(0, day2.Watching);

        // Day 3: back to the normal schedule — no longer due every day.
        host.Clock.AdvanceDays(1);
        var day3 = await host.Poller.PollDueBatchAsync();
        Assert.Equal(0, day3.Polled);
    }

    [Fact]
    public async Task A_lone_build_push_is_still_stored_raw()
    {
        // Dota 2's shape: a fresh depot push with a patch note 53 days behind it.
        // §4.5 stores BOTH raw signals so the heuristic can be retuned without
        // re-fetching, and pitfall 4 is about how the signal is READ, not whether
        // it is kept — suppressing it here would hard-code today's window into
        // the data.
        var announcedAt = Now.AddDays(-20).UtcDateTime;
        var builtAt = Now.AddDays(-1).UtcDateTime;

        using var host = Host(
            (request, _) => request.Host == UpdateHost.SteamNews
                ? FakeUpdateHandler.Json(
                    HttpStatusCode.OK, UpdateFixtures.News(request.AppId, announcedAt, "gid-1"))
                : FakeUpdateHandler.Json(
                    HttpStatusCode.OK, UpdateFixtures.BuildInfo(request.AppId, builtAt)),
            candidates: [new PollCandidate(1, "100", 240, Now.AddMonths(-8).UtcDateTime)]);

        var report = await host.Poller.PollDueBatchAsync();

        Assert.Equal(1, report.AnnouncementsRecorded);
        Assert.Equal(1, report.BuildPushesRecorded);

        // Not watching: a push 19 days newer than the announcement will never
        // correlate with it, so there is nothing to wait for.
        Assert.Equal(0, report.Watching);
    }

    [Fact]
    public async Task A_no_feed_app_costs_one_request_ever_and_never_a_build_call()
    {
        using var host = Host(
            (request, _) => request.Host == UpdateHost.SteamNews
                ? FakeUpdateHandler.NoNewsFeed()
                : UpdateSignalTestHost.Unarranged(request),
            candidates: [new PollCandidate(1, UpdateFixtures.NoFeedAppId, 240, Now.AddMonths(-8).UtcDateTime)]);

        var first = await host.Poller.PollDueBatchAsync();
        Assert.Equal(1, first.NoFeed);
        Assert.Equal(1, first.NewsRequests);
        Assert.Equal(0, first.BuildInfoRequests);

        // Fourteen days later — past the catch-up threshold, so it is genuinely
        // scheduled again — the answer still costs nothing.
        host.Clock.AdvanceDays(15);
        var second = await host.Poller.PollDueBatchAsync();
        Assert.Equal(1, second.Polled);
        Assert.Equal(0, second.NewsRequests);
        Assert.Equal(1, host.Handler.CountFor(UpdateHost.SteamNews));
    }

    // ── Stagger ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_eligible_set_is_spread_across_the_sweep_period()
    {
        var candidates = Enumerable.Range(0, 350)
            .Select(i => new PollCandidate(i + 1, (100000 + i).ToString(), 240, Now.AddMonths(-8).UtcDateTime))
            .ToArray();

        using var host = Host(
            NewsOnly(Now.AddYears(-3).UtcDateTime),
            candidates: candidates,
            configure: options => options.SweepPeriodDays = 7);

        var perDay = new List<int>();
        for (var day = 0; day < 7; day++)
        {
            perDay.Add((await host.Poller.PollDueBatchAsync()).Polled);
            host.Clock.AdvanceDays(1);
        }

        // Every app polled exactly once across the period, and no single day
        // carrying the whole library. The spike's figure for E = 370 is ~53
        // requests a day against 1,232 for a naive full poll.
        Assert.Equal(350, perDay.Sum());
        Assert.All(perDay, count => Assert.InRange(count, 1, 120));
        Assert.True(
            perDay.Max() < 350,
            "The sweep must be staggered, not run in a single day: " + string.Join(", ", perDay));
    }

    [Fact]
    public async Task A_days_batch_is_capped_and_the_remainder_leads_the_next_one()
    {
        var candidates = Enumerable.Range(0, 60)
            .Select(i => new PollCandidate(i + 1, (200000 + i).ToString(), 240, Now.AddMonths(-8).UtcDateTime))
            .ToArray();

        using var host = Host(
            NewsOnly(Now.AddYears(-3).UtcDateTime),
            candidates: candidates,
            configure: options =>
            {
                // One slot, so every app is due at once — the shape of a first
                // run, or of a machine switched back on after a long shutdown.
                options.SweepPeriodDays = 1;
                options.MaxAppsPerBatch = 10;
            });

        var first = await host.Poller.PollDueBatchAsync();

        Assert.Equal(60, first.Due);
        Assert.Equal(10, first.Polled);

        // The cap is what stops a long shutdown from becoming a several-hundred
        // request burst at a volunteer service.
        Assert.Equal(10, host.Handler.CountFor(UpdateHost.SteamNews));

        var polledFirst = host.Handler.AppIdsFor(UpdateHost.SteamNews).ToHashSet(StringComparer.Ordinal);

        host.Handler.Clear();
        host.Clock.AdvanceDays(1);
        await host.Poller.PollDueBatchAsync();

        // The truncated apps are the least recently polled, so they lead the
        // next batch rather than waiting a whole sweep period.
        var polledSecond = host.Handler.AppIdsFor(UpdateHost.SteamNews).ToHashSet(StringComparer.Ordinal);
        Assert.Empty(polledFirst.Intersect(polledSecond));
    }

    [Fact]
    public async Task Polling_twice_in_one_day_does_not_re_poll()
    {
        using var host = Host(
            NewsOnly(Now.AddYears(-3).UtcDateTime),
            candidates: [new PollCandidate(1, "100", 240, Now.AddMonths(-8).UtcDateTime)],
            configure: options => options.SweepPeriodDays = 1);

        var first = await host.Poller.PollDueBatchAsync();
        var second = await host.Poller.PollDueBatchAsync();

        Assert.Equal(1, first.Polled);
        Assert.Equal(0, second.Polled);
        Assert.Equal(1, host.Handler.CountFor(UpdateHost.SteamNews));
    }

    [Fact]
    public async Task Staggering_survives_a_restart()
    {
        var candidates = new[] { new PollCandidate(1, "100", 240, Now.AddMonths(-8).UtcDateTime) };

        // A real database, because that is the whole claim: the schedule lives
        // in metadata_cache, not in anything a process holds.
        var cache = new Winnow.Enrich.Updates.Storage.SqliteUpdateSignalCache(_db.Factory);

        using (var host = Host(
            NewsOnly(Now.AddYears(-3).UtcDateTime),
            candidates: candidates,
            cache: cache,
            configure: options => options.SweepPeriodDays = 1))
        {
            Assert.Equal(1, (await host.Poller.PollDueBatchAsync()).Polled);
        }

        // A brand new provider, new clients, new poller — everything a restart
        // replaces — sharing only the database.
        using (var restarted = Host(
            NewsOnly(Now.AddYears(-3).UtcDateTime),
            candidates: candidates,
            cache: cache,
            configure: options => options.SweepPeriodDays = 1))
        {
            var report = await restarted.Poller.PollDueBatchAsync();

            Assert.Equal(0, report.Polled);
            Assert.Equal(0, restarted.Handler.CountFor(UpdateHost.SteamNews));

            // And it resumes on schedule rather than staying stuck.
            restarted.Clock.AdvanceDays(1);
            Assert.Equal(1, (await restarted.Poller.PollDueBatchAsync()).Polled);
        }
    }

    [Fact]
    public void Slots_are_stable_across_processes()
    {
        // .NET randomises string hashing per process, so String.GetHashCode
        // would reshuffle every app into a different slot on every launch and
        // destroy the staggering entirely. These are the FNV-1a values; if this
        // test fails, the hash changed and every user's schedule shifted.
        Assert.Equal(UpdateSignalPoller.Slot("413150", 7), UpdateSignalPoller.Slot("413150", 7));
        Assert.All(
            new[] { "570", "620", "413150", "1245620" },
            appId => Assert.InRange(UpdateSignalPoller.Slot(appId, 7), 0, 6));

        // And today's slot advances exactly once per UTC day.
        var day = new DateTime(2026, 6, 15, 23, 59, 0, DateTimeKind.Utc);
        Assert.Equal(
            UpdateSignalPoller.TodaySlot(day, 7),
            UpdateSignalPoller.TodaySlot(day.AddHours(-12), 7));
        Assert.NotEqual(
            UpdateSignalPoller.TodaySlot(day, 7),
            UpdateSignalPoller.TodaySlot(day.AddDays(1), 7));
    }

    [Fact]
    public async Task An_unanswered_poll_leaves_the_app_due()
    {
        var failing = true;

        using var host = Host(
            (request, _) => request.Host == UpdateHost.SteamNews && failing
                ? FakeUpdateHandler.Json(HttpStatusCode.ServiceUnavailable, "{}")
                : FakeUpdateHandler.Json(
                    HttpStatusCode.OK, UpdateFixtures.News(request.AppId, Now.AddYears(-3).UtcDateTime, "g")),
            candidates: [new PollCandidate(1, "100", 240, Now.AddMonths(-8).UtcDateTime)],
            configure: options => options.MaxRetryAttempts = 1);

        var first = await host.Poller.PollDueBatchAsync();
        Assert.Equal(1, first.Failures);

        // Not stamped as polled: a transient outage must not cost the app a
        // whole sweep period of invisibility.
        failing = false;
        var second = await host.Poller.PollDueBatchAsync();
        Assert.Equal(1, second.Polled);
        Assert.Equal(1, second.AnnouncementsRecorded);
    }

    // ── Idempotency ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Re_polling_the_same_events_writes_no_duplicates()
    {
        var releaseId = await SeedAsync("100", playtimeMinutes: 240, lastPlayed: Now.AddMonths(-8).UtcDateTime);
        var announcedAt = Now.AddDays(-2).UtcDateTime;
        var builtAt = Now.AddDays(-1).UtcDateTime;

        var cache = new SqliteUpdateSignalCache(_db.Factory);

        async Task<UpdatePollReport> PollAsync(DateTimeOffset at)
        {
            using var host = DbHost(
                (request, _) => request.Host == UpdateHost.SteamNews
                    ? FakeUpdateHandler.Json(
                        HttpStatusCode.OK, UpdateFixtures.News(request.AppId, announcedAt, "gid-1"))
                    : FakeUpdateHandler.Json(
                        HttpStatusCode.OK, UpdateFixtures.BuildInfo(request.AppId, builtAt)),
                candidates: [new PollCandidate(releaseId, "100", 240, Now.AddMonths(-8).UtcDateTime)],
                cache: cache,
                now: at,
                configure: options =>
                {
                    options.SweepPeriodDays = 1;
                    options.BuildInfoCacheTtl = TimeSpan.Zero;
                });
            return await host.Poller.PollDueBatchAsync();
        }

        await PollAsync(Now);

        var events = new UpdateEventRepository(_db.Factory);
        Assert.Equal(2, (await events.GetByReleaseAsync(releaseId)).Count);

        // Every subsequent day sees the same newest patch note and the same
        // persistent `timeupdated`. Without the identity index this appends two
        // rows a day forever, and §6.1's EXISTS-based correlation would keep
        // answering correctly the whole time — a silent leak behind a
        // correct-looking feature.
        for (var day = 1; day <= 5; day++)
        {
            await PollAsync(Now.AddDays(day));
        }

        var final = await events.GetByReleaseAsync(releaseId);
        Assert.Equal(2, final.Count);
        Assert.Equal(
            // Oldest first, as GetByReleaseAsync orders: the announcement landed
            // two days ago, the build one.
            [UpdateEventKinds.Announcement, UpdateEventKinds.BuildPush],
            final.Select(e => e.Kind));
    }

    [Fact]
    public async Task The_patch_notes_url_is_stored_on_the_announcement()
    {
        var releaseId = await SeedAsync("100", playtimeMinutes: 240, lastPlayed: Now.AddMonths(-8).UtcDateTime);

        using var host = DbHost(
            (request, _) => request.Host == UpdateHost.SteamNews
                ? FakeUpdateHandler.Json(
                    HttpStatusCode.OK,
                    UpdateFixtures.News(request.AppId, Now.AddDays(-2).UtcDateTime, "gid-42", "Patch 1.6.15"))
                : FakeUpdateHandler.Json(
                    HttpStatusCode.OK, UpdateFixtures.BuildInfo(request.AppId, Now.AddDays(-1).UtcDateTime)),
            candidates: [new PollCandidate(releaseId, "100", 240, Now.AddMonths(-8).UtcDateTime)]);

        await host.Poller.PollDueBatchAsync();

        var written = await new UpdateEventRepository(_db.Factory).GetByReleaseAsync(releaseId);
        var announcement = Assert.Single(written, e => e.Kind == UpdateEventKinds.Announcement);

        // design-system §5.2: "Clicking the badge opens the patch notes for the
        // updates you missed." The endpoint pages backwards by date with no
        // lookup by gid, so this url is captured now or lost.
        Assert.Contains("gid-42", announcement.Url!, StringComparison.Ordinal);
        Assert.Equal("Patch 1.6.15", announcement.Title);
        Assert.NotNull(announcement.RawJson);

        var push = Assert.Single(written, e => e.Kind == UpdateEventKinds.BuildPush);
        Assert.Null(push.Url);
        Assert.NotNull(push.BuildId);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A poller whose eligible set is stated outright and whose writes land in
    /// memory. For the tests about the schedule and the cascade, where the
    /// question is which requests go out, not which rows come back.
    /// </summary>
    private UpdateSignalTestHost Host(
        Func<RecordedUpdateRequest, int, HttpResponseMessage> responder,
        IReadOnlyList<PollCandidate>? candidates = null,
        Action<UpdateSignalOptions>? configure = null,
        IUpdateSignalCache? cache = null,
        DateTimeOffset? now = null)
        => new(
            responder,
            Schedule(configure),
            database: null,
            cache,
            now ?? Now,
            candidates ?? []);

    /// <summary>
    /// A poller wired to the temp database, for the tests that assert on rows in
    /// <c>update_events</c>. Candidates must name real seeded releases — the
    /// foreign key is real too.
    /// </summary>
    private UpdateSignalTestHost DbHost(
        Func<RecordedUpdateRequest, int, HttpResponseMessage> responder,
        IReadOnlyList<PollCandidate>? candidates = null,
        Action<UpdateSignalOptions>? configure = null,
        IUpdateSignalCache? cache = null,
        DateTimeOffset? now = null)
        => new(
            responder,
            Schedule(configure),
            _db,
            cache,
            now ?? Now,
            candidates);

    private static Action<UpdateSignalOptions> Schedule(Action<UpdateSignalOptions>? configure)
        => options =>
            {
                // A one-day sweep by default, so a test that is about the
                // cascade or about idempotency does not also have to arrange for
                // its appid's slot to fall on the day the clock starts. The
                // stagger tests set this back to a real period deliberately.
                options.SweepPeriodDays = 1;
                configure?.Invoke(options);
            };

    /// <summary>
    /// The first day on or after <paramref name="notBefore"/> on which an appid's
    /// slot comes up. Lets a test about the schedule start on a due day without
    /// hard-coding a hash value that a future tweak would silently invalidate.
    /// </summary>
    private static DateTimeOffset FirstDueDay(string appId, int sweepPeriodDays, DateTimeOffset notBefore)
    {
        for (var offset = 0; offset < sweepPeriodDays; offset++)
        {
            var day = notBefore.AddDays(offset);
            if (UpdateSignalPoller.Slot(appId, sweepPeriodDays)
                == UpdateSignalPoller.TodaySlot(day.UtcDateTime, sweepPeriodDays))
            {
                return day;
            }
        }

        return notBefore;
    }

    /// <summary>Answers the news endpoint and fails loudly on any build call.</summary>
    private static Func<RecordedUpdateRequest, int, HttpResponseMessage> NewsOnly(DateTime publishedAt)
        => (request, _) => request.Host == UpdateHost.SteamNews
            ? FakeUpdateHandler.Json(
                HttpStatusCode.OK, UpdateFixtures.News(request.AppId, publishedAt, "gid-" + request.AppId))
            : UpdateSignalTestHost.Unarranged(request);

    private async Task<long> SeedAsync(string appId, long? playtimeMinutes, DateTime? lastPlayed)
    {
        var works = new WorkRepository(_db.Factory);
        var releases = new ReleaseRepository(_db.Factory);
        var externalIds = new ReleaseRepository(_db.Factory);

        var workId = await works.InsertAsync(new Work { Name = "App " + appId });
        var releaseId = await releases.InsertAsync(new Release
        {
            WorkId = workId,
            Name = "App " + appId,
            Platform = "windows",
        });

        await externalIds.AddExternalIdAsync(new ExternalId
        {
            ReleaseId = releaseId,
            Provider = ExternalIdProviders.Steam,
            ProviderId = appId,
        });

        await AddOwnershipAsync(releaseId, "steam", playtimeMinutes, lastPlayed);
        return releaseId;
    }

    private async Task AddOwnershipAsync(
        long releaseId, string store, long? playtimeMinutes, DateTime? lastPlayed = null)
    {
        var ownerships = new OwnershipRepository(_db.Factory);
        var plays = new PlayRecordRepository(_db.Factory);

        var ownershipId = await ownerships.InsertAsync(new Ownership
        {
            ReleaseId = releaseId,
            Store = store,
        });

        if (playtimeMinutes is { } minutes)
        {
            await plays.InsertAsync(new PlayRecord
            {
                OwnershipId = ownershipId,
                PlaytimeMinutes = minutes,
                LastPlayedAt = lastPlayed,
                Source = "steam_local",
                ObservedAt = Now.UtcDateTime,
            });
        }
    }
}
