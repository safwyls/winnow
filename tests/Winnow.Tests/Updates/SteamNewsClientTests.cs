using System.Diagnostics;
using System.Net;
using Winnow.Enrich.Updates;
using Winnow.Enrich.Updates.Model;
using Xunit;

namespace Winnow.Tests.Updates;

/// <summary>
/// <c>ISteamNews/GetNewsForApp</c> against canned responses. No live calls.
///
/// <para>The first four tests are all about one status code. 403 from this
/// endpoint means "this appid has no news feed" — verified live for 460, 480,
/// 520 and 750, every one of them answering 403 with body <c>{}</c> while
/// known-good appids answered 200 in the same burst. A client that reads it as
/// throttling backs off, and if it also breaks a circuit, one delisted game
/// silently suppresses the badge for the entire library. That is the failure
/// these tests exist to make impossible.</para>
/// </summary>
public class SteamNewsClientTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Forbidden_is_a_per_appid_no_feed_not_a_rate_limit()
    {
        using var host = new UpdateSignalTestHost((request, _) =>
            request.AppId == UpdateFixtures.NoFeedAppId
                ? FakeUpdateHandler.NoNewsFeed()
                : FakeUpdateHandler.Json(HttpStatusCode.OK, UpdateFixtures.News(request.AppId, Now.AddDays(-2), "g1")));

        var result = await host.News.GetLatestPatchNoteAsync(UpdateFixtures.NoFeedAppId);

        Assert.Equal(NewsOutcome.NoFeed, result.Outcome);
        Assert.False(result.ServedFromCache);

        // ONE request. Not four. The retry policy's transient allow-list does
        // not contain 403, so nothing was tried again and nothing was waited on.
        Assert.Equal(1, host.Handler.CountFor(UpdateHost.SteamNews, UpdateFixtures.NoFeedAppId));
    }

    [Fact]
    public async Task Forbidden_never_backs_off()
    {
        using var host = new UpdateSignalTestHost(
            (_, _) => FakeUpdateHandler.NoNewsFeed(),
            options =>
            {
                // A backoff schedule long enough that even one retry would be
                // unmistakable in the elapsed time. If 403 ever joins the
                // transient list, three retries cost at least 15 seconds here.
                options.RetryBaseDelay = TimeSpan.FromSeconds(5);
                options.MaxRetryDelay = TimeSpan.FromSeconds(30);
            });

        var stopwatch = Stopwatch.StartNew();
        var result = await host.News.GetLatestPatchNoteAsync(UpdateFixtures.NoFeedAppId);
        stopwatch.Stop();

        Assert.Equal(NewsOutcome.NoFeed, result.Outcome);
        Assert.Equal(1, host.Handler.CountFor(UpdateHost.SteamNews));
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"403 must return immediately; it took {stopwatch.Elapsed}. A delisted game has no news "
            + "feed, and treating that as throttling stalls the poller for hours.");
    }

    [Fact]
    public async Task Forbidden_is_cached_so_the_next_sweep_costs_nothing()
    {
        using var host = new UpdateSignalTestHost(
            (_, _) => FakeUpdateHandler.NoNewsFeed(),
            now: new DateTimeOffset(Now));

        var first = await host.News.GetLatestPatchNoteAsync(UpdateFixtures.NoFeedAppId);
        var second = await host.News.GetLatestPatchNoteAsync(UpdateFixtures.NoFeedAppId);

        Assert.Equal(NewsOutcome.NoFeed, first.Outcome);
        Assert.Equal(NewsOutcome.NoFeed, second.Outcome);
        Assert.False(first.ServedFromCache);
        Assert.True(second.ServedFromCache);
        Assert.Equal(1, host.Handler.CountFor(UpdateHost.SteamNews));

        // Not permanent: "no feed" is a fact about a Steam page, and pages can
        // gain one. After the retry window it is asked again — once.
        host.Clock.Advance(TimeSpan.FromDays(91));
        var third = await host.News.GetLatestPatchNoteAsync(UpdateFixtures.NoFeedAppId);
        Assert.Equal(NewsOutcome.NoFeed, third.Outcome);
        Assert.Equal(2, host.Handler.CountFor(UpdateHost.SteamNews));
    }

    [Fact]
    public async Task One_appids_missing_feed_does_not_affect_any_other_appid()
    {
        using var host = new UpdateSignalTestHost((request, _) =>
            request.AppId == UpdateFixtures.NoFeedAppId
                ? FakeUpdateHandler.NoNewsFeed()
                : FakeUpdateHandler.Json(
                    HttpStatusCode.OK, UpdateFixtures.News(request.AppId, Now.AddDays(-3), "g-" + request.AppId)));

        // The 403 is deliberately first: a circuit breaker or a global backoff
        // would poison everything after it, which is precisely the failure mode
        // the spike warns about.
        await host.News.GetLatestPatchNoteAsync(UpdateFixtures.NoFeedAppId);

        var stardew = await host.News.GetLatestPatchNoteAsync(UpdateFixtures.StardewAppId);
        var portal = await host.News.GetLatestPatchNoteAsync(UpdateFixtures.PortalAppId);

        Assert.Equal(NewsOutcome.Ok, stardew.Outcome);
        Assert.Equal(NewsOutcome.Ok, portal.Outcome);
        Assert.Equal(3, host.Handler.CountFor(UpdateHost.SteamNews));
    }

    [Fact]
    public async Task Real_rate_limiting_is_honoured_with_retry_after()
    {
        var retryAfter = TimeSpan.FromMilliseconds(250);

        using var host = new UpdateSignalTestHost(
            (request, prior) => prior == 0
                ? FakeUpdateHandler.TooManyRequests(retryAfter)
                : FakeUpdateHandler.Json(
                    HttpStatusCode.OK, UpdateFixtures.News(request.AppId, Now.AddDays(-1), "g1")),
            options =>
            {
                // Well below the server's Retry-After, so a pass that ignored
                // the header would finish measurably sooner.
                options.RetryBaseDelay = TimeSpan.FromMilliseconds(1);
                options.MaxRetryDelay = TimeSpan.FromSeconds(5);
            });

        var stopwatch = Stopwatch.StartNew();
        var result = await host.News.GetLatestPatchNoteAsync(UpdateFixtures.StardewAppId);
        stopwatch.Stop();

        // 429 IS transient — this is the one status that means slow down, and
        // §4.2 requires the backoff from the first commit.
        Assert.Equal(NewsOutcome.Ok, result.Outcome);
        Assert.Equal(2, host.Handler.CountFor(UpdateHost.SteamNews));
        Assert.True(
            stopwatch.Elapsed >= retryAfter,
            $"Retry-After of {retryAfter} must be waited out; the pass took only {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task Empty_news_items_is_an_answer_not_a_failure()
    {
        using var host = new UpdateSignalTestHost(
            (_, _) => FakeUpdateHandler.Json(HttpStatusCode.OK, UpdateFixtures.NewsNoMatchesResponse()));

        var result = await host.News.GetLatestPatchNoteAsync("790");

        // Verified live for appid 790: the app has a feed, and nothing in it is
        // tagged patchnotes. Distinct from NoFeed, and distinct from a failure.
        Assert.Equal(NewsOutcome.NoItems, result.Outcome);
        Assert.Null(result.Item);
    }

    [Fact]
    public async Task Server_errors_degrade_to_unavailable_after_retries()
    {
        using var host = new UpdateSignalTestHost(
            (_, _) => FakeUpdateHandler.Json(HttpStatusCode.ServiceUnavailable, "{}"),
            options => options.MaxRetryAttempts = 2);

        var result = await host.News.GetLatestPatchNoteAsync(UpdateFixtures.StardewAppId);

        Assert.Equal(NewsOutcome.Unavailable, result.Outcome);

        // 5xx IS retried — unlike 403 — so the initial attempt plus two retries.
        Assert.Equal(3, host.Handler.CountFor(UpdateHost.SteamNews));
    }

    [Fact]
    public async Task Unrecognisable_body_degrades_to_unavailable()
    {
        using var host = new UpdateSignalTestHost(
            (_, _) => FakeUpdateHandler.Json(HttpStatusCode.OK, """{"something":"else"}"""));

        var result = await host.News.GetLatestPatchNoteAsync(UpdateFixtures.StardewAppId);

        // Soft-fail is the contract: a shape change is a degraded pass, never a
        // crashed one (§5.1).
        Assert.Equal(NewsOutcome.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task Request_uses_patchnotes_tag_no_key_and_identifies_itself()
    {
        using var host = new UpdateSignalTestHost(
            (request, _) => FakeUpdateHandler.Json(
                HttpStatusCode.OK, UpdateFixtures.News(request.AppId, Now.AddDays(-1), "g1")));

        await host.News.GetLatestPatchNoteAsync(UpdateFixtures.StardewAppId);

        var sent = Assert.Single(host.Handler.Requests);

        // tags=patchnotes, not feeds=steam_community_announcements: the spike
        // measured 34 real patches against 74 items that still included merch
        // promos and anniversary posts.
        Assert.Equal("patchnotes", sent.Tags);
        Assert.Null(sent.Feeds);

        // Keyless. Verified live, and the reason M2 ships with no settings
        // screen for API keys.
        Assert.Null(sent.ApiKey);

        Assert.Equal("1", sent.Query("count"));
        Assert.Equal("1", sent.Query("maxlength"));
        Assert.Contains("Winnow", sent.UserAgent, StringComparison.Ordinal);
        Assert.Contains("/v2/", sent.Uri.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Newest_item_wins_even_if_the_feed_is_returned_out_of_order()
    {
        const string outOfOrder = """
            {"appnews":{"appid":413150,"count":2,"newsitems":[
              {"gid":"old","title":"Older","url":"https://example.invalid/old","date":1700000000,"tags":["patchnotes"]},
              {"gid":"new","title":"Newer","url":"https://example.invalid/new","date":1734718461,"tags":["patchnotes"]}
            ]}}
            """;

        using var host = new UpdateSignalTestHost(
            (_, _) => FakeUpdateHandler.Json(HttpStatusCode.OK, outOfOrder));

        var result = await host.News.GetLatestPatchNoteAsync(UpdateFixtures.StardewAppId);

        // The high-water mark is only meaningful if "newest" is asserted rather
        // than assumed from position.
        Assert.Equal(NewsOutcome.Ok, result.Outcome);
        Assert.Equal("new", result.Item!.Gid);
    }
}
