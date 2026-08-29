using System.Net;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Http;
using Xunit;

namespace Winnow.Tests.SteamWeb;

/// <summary>
/// <see cref="SteamHistoryClient"/> against canned responses. Nothing here opens
/// a socket, per the enrichment charter's rule that HTTP clients are tested
/// against fixtures and never against a live API.
/// </summary>
public class SteamHistoryClientTests
{
    private const string ApiKey = "0123456789ABCDEF0123456789ABCDEF";

    private static SteamId Account
        => SteamId.FromAccountId(SteamWebFixtures.FixtureAccountId)!.Value;

    /// <summary>Answers each M5 endpoint from its fixture and 404s anything else.</summary>
    private static Func<RecordedSteamWebRequest, int, HttpResponseMessage> Fixtures(string? yearInReview = null)
        => (request, _) => request.Endpoint switch
        {
            SteamWebTestHost.ClientGetLastPlayedTimes
                => FakeSteamWebHandler.Json(HttpStatusCode.OK, SteamWebFixtures.LastPlayedTimes()),
            SteamWebTestHost.GetUserYearInReview
                => FakeSteamWebHandler.Json(
                    HttpStatusCode.OK, yearInReview ?? SteamWebFixtures.YearInReview2024()),
            _ => FakeSteamWebHandler.Json(HttpStatusCode.NotFound, "{}"),
        };

    /// <summary>
    /// §4.2's mandated shape for this endpoint, and the finding that was
    /// verified live: <b>no <c>steamid</c></b>. The key identifies the account,
    /// so sending one would be noise at best and a request for somebody else's
    /// data at worst.
    /// </summary>
    [Fact]
    public async Task The_last_played_request_sends_a_key_and_no_steamid()
    {
        using var host = new SteamWebTestHost(Fixtures(), apiKey: ApiKey);

        var result = await host.History.GetLastPlayedTimesAsync();

        Assert.True(result.Answered);
        var request = Assert.Single(host.Handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(SteamWebTestHost.ClientGetLastPlayedTimes, request.Endpoint);
        Assert.Equal(ApiKey, request.Parameter("key"));
        Assert.Equal("json", request.Parameter("format"));
        Assert.False(request.HasParameter("steamid"));
    }

    [Fact]
    public async Task The_year_in_review_request_carries_the_account_and_the_year()
    {
        using var host = new SteamWebTestHost(Fixtures(), apiKey: ApiKey);

        await host.History.GetYearInReviewAsync(Account, 2024);

        var request = Assert.Single(host.Handler.Requests);
        Assert.Equal(SteamWebTestHost.GetUserYearInReview, request.Endpoint);
        Assert.Equal(Account.Value.ToString(), request.Parameter("steamid"));
        Assert.Equal("2024", request.Parameter("year"));
        Assert.Equal(ApiKey, request.Parameter("key"));
    }

    /// <summary>
    /// §4.3: a descriptive User-Agent so Valve can attribute, and if necessary
    /// contact, this traffic. It is the shared one, not a second identity for
    /// the same application.
    /// </summary>
    [Fact]
    public async Task Every_history_request_identifies_winnow()
    {
        using var host = new SteamWebTestHost(Fixtures(), apiKey: ApiKey);

        await host.History.GetLastPlayedTimesAsync();
        await host.History.GetYearInReviewAsync(Account, 2024);

        Assert.All(
            host.Handler.Requests,
            r => Assert.StartsWith("Winnow/", r.UserAgent ?? string.Empty, StringComparison.Ordinal));
    }

    /// <summary>
    /// The rule the whole module is built around, extended to the two new URLs.
    /// The key travels in the query string, so the check is that no sink, at
    /// Trace with the framework's own request logging in play, ever saw it.
    /// </summary>
    [Fact]
    public async Task The_api_key_never_reaches_a_log()
    {
        using var host = new SteamWebTestHost(Fixtures(), apiKey: ApiKey);

        await host.History.GetLastPlayedTimesAsync();
        await host.History.GetYearInReviewAsync(Account, 2024);

        Assert.DoesNotContain(ApiKey, host.Logs.AllText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The redaction allowlist was written for <c>GetOwnedGames</c> and had to
    /// be verified against the new URLs rather than assumed to cover them. It
    /// does, because it is an allowlist: <c>year</c> was added to keep the line
    /// useful, and everything unlisted is redacted whether or not anyone
    /// remembered it was a secret.
    /// </summary>
    [Fact]
    public void Redaction_covers_the_history_urls()
    {
        var described = SteamWebRedaction.Describe(
            new Uri(
                "https://api.steampowered.com/ISaleFeatureService/GetUserYearInReview/v1/"
                + "?steamid=76561197971376839&year=2024&format=json&key=" + ApiKey));

        Assert.DoesNotContain(ApiKey, described, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("key=" + SteamWebRedaction.Placeholder, described, StringComparison.Ordinal);
        Assert.Contains("year=2024", described, StringComparison.Ordinal);
        Assert.Contains("steamid=76561197971376839", described, StringComparison.Ordinal);

        var lastPlayed = SteamWebRedaction.Describe(
            new Uri(
                "https://api.steampowered.com/IPlayerService/ClientGetLastPlayedTimes/v1/"
                + "?format=json&key=" + ApiKey));
        Assert.DoesNotContain(ApiKey, lastPlayed, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The distinction the completion markers are built on. An answered-empty
    /// year is done forever; a failure has to be retried, and confusing the two
    /// either loses a year of history permanently or refetches it every launch.
    /// </summary>
    [Fact]
    public async Task An_empty_year_answers_while_a_failure_does_not()
    {
        using var empty = new SteamWebTestHost(
            (request, _) => request.Endpoint == SteamWebTestHost.GetUserYearInReview
                ? FakeSteamWebHandler.Json(HttpStatusCode.OK, SteamWebFixtures.EmptyYearInReview)
                : FakeSteamWebHandler.Json(HttpStatusCode.NotFound, "{}"),
            apiKey: ApiKey);

        var answered = await empty.History.GetYearInReviewAsync(Account, 2023);
        Assert.True(answered.Answered);
        Assert.True(answered.AnsweredEmpty);
        Assert.Empty(answered.Games);

        using var broken = new SteamWebTestHost(
            (_, _) => FakeSteamWebHandler.Json(HttpStatusCode.Forbidden, "{}"), apiKey: ApiKey);

        var failed = await broken.History.GetYearInReviewAsync(Account, 2023);
        Assert.False(failed.Answered);
        Assert.False(failed.AnsweredEmpty);
    }

    /// <summary>
    /// The safety check that makes the whole feature safe to run on a machine
    /// with two Steam accounts: the key identifies whose history Steam returns,
    /// and the request's <c>steamid</c> does not have to match it.
    /// </summary>
    [Fact]
    public async Task A_response_for_a_different_account_is_flagged_rather_than_imported()
    {
        using var host = new SteamWebTestHost(
            Fixtures(SteamWebFixtures.ForeignYearInReview), apiKey: ApiKey);

        var review = await host.History.GetYearInReviewAsync(Account, 2024);

        Assert.True(review.Answered);
        Assert.True(review.AccountMismatch);
        Assert.Equal(22222222u, review.AccountId);
        Assert.False(review.AnsweredEmpty);
    }

    [Fact]
    public async Task A_matching_account_is_not_a_mismatch()
    {
        using var host = new SteamWebTestHost(Fixtures(), apiKey: ApiKey);

        var review = await host.History.GetYearInReviewAsync(Account, 2024);

        Assert.False(review.AccountMismatch);
        Assert.Equal(SteamWebFixtures.FixtureAccountId, review.AccountId);
        Assert.Equal(4, review.Games.Count);
    }

    /// <summary>
    /// §4.2: since June 2025 Steam throttles profile endpoints with 429 and a
    /// <c>Retry-After</c>. Honoured by the shared Polly pipeline, not by a sleep
    /// at the call site; the history client added no resilience code of its own
    /// and inherits this by being registered on the same handler chain.
    /// </summary>
    [Fact]
    public async Task A_429_is_retried_through_the_shared_polly_pipeline()
    {
        using var host = new SteamWebTestHost(
            (request, prior) => request.Endpoint == SteamWebTestHost.GetUserYearInReview && prior == 0
                ? FakeSteamWebHandler.TooManyRequests(TimeSpan.FromMilliseconds(5))
                : Fixtures()(request, prior),
            apiKey: ApiKey);

        var review = await host.History.GetYearInReviewAsync(Account, 2024);

        Assert.True(review.Answered);
        Assert.Equal(2, host.Handler.CountFor(SteamWebTestHost.GetUserYearInReview));
    }

    /// <summary>
    /// A 429 that outlasts the retry budget is a failure, not an empty year.
    /// Marking a throttled year complete would lose it for the life of the
    /// install.
    /// </summary>
    [Fact]
    public async Task A_year_throttled_past_the_retry_budget_is_unanswered()
    {
        using var host = new SteamWebTestHost(
            (_, _) => FakeSteamWebHandler.TooManyRequests(TimeSpan.FromMilliseconds(1)),
            apiKey: ApiKey,
            configure: o => o.MaxRetryAttempts = 1);

        var review = await host.History.GetYearInReviewAsync(Account, 2024);

        Assert.False(review.Answered);
        Assert.Equal(2, host.Handler.CountFor(SteamWebTestHost.GetUserYearInReview));
    }

    /// <summary>
    /// §4.2's caching rule. A relaunch inside the TTL costs no request at all,
    /// which is what makes a per-launch backfill acceptable traffic.
    /// </summary>
    [Fact]
    public async Task A_second_call_inside_the_ttl_makes_no_request()
    {
        using var host = new SteamWebTestHost(Fixtures(), apiKey: ApiKey);

        await host.History.GetLastPlayedTimesAsync();
        await host.History.GetYearInReviewAsync(Account, 2024);

        var cachedAnchors = await host.History.GetLastPlayedTimesAsync();
        var cachedYear = await host.History.GetYearInReviewAsync(Account, 2024);

        Assert.True(cachedAnchors.FromCache);
        Assert.True(cachedYear.FromCache);
        Assert.Equal(2, host.Handler.Requests.Count);

        // Different year, different entry. The cache key carries both the
        // account and the year, so 2025 is not served 2024's bytes.
        await host.History.GetYearInReviewAsync(Account, 2025);
        Assert.Equal(3, host.Handler.Requests.Count);
    }

    /// <summary>
    /// Past the TTL the client asks again. Pinned so a cache that silently never
    /// expires cannot freeze the current year at whatever it held in January.
    /// </summary>
    [Fact]
    public async Task A_call_past_the_ttl_refetches()
    {
        using var host = new SteamWebTestHost(Fixtures(), apiKey: ApiKey);

        await host.History.GetYearInReviewAsync(Account, 2024);
        host.Clock.Advance(TimeSpan.FromHours(7));

        var refetched = await host.History.GetYearInReviewAsync(Account, 2024);

        Assert.False(refetched.FromCache);
        Assert.Equal(2, host.Handler.CountFor(SteamWebTestHost.GetUserYearInReview));
    }

    /// <summary>
    /// No key is the ordinary state, not an error: the module declines and makes
    /// no request rather than sending an unauthenticated one.
    /// </summary>
    [Fact]
    public async Task An_unconfigured_client_makes_no_request()
    {
        using var host = new SteamWebTestHost(Fixtures(), apiKey: null);

        Assert.False(await host.History.IsConfiguredAsync());
        Assert.False((await host.History.GetLastPlayedTimesAsync()).Answered);
        Assert.False((await host.History.GetYearInReviewAsync(Account, 2024)).Answered);
        Assert.Empty(host.Handler.Requests);
    }

    /// <summary>
    /// History does not un-happen: a stale entry is a strictly better answer
    /// than none when today's request failed.
    /// </summary>
    [Fact]
    public async Task A_failed_refetch_falls_back_to_the_stale_entry()
    {
        var fail = false;
        using var host = new SteamWebTestHost(
            (request, prior) => fail
                ? FakeSteamWebHandler.Json(HttpStatusCode.ServiceUnavailable, "{}")
                : Fixtures()(request, prior),
            apiKey: ApiKey,
            configure: o => o.MaxRetryAttempts = 1);

        var fresh = await host.History.GetLastPlayedTimesAsync();
        Assert.True(fresh.Answered);
        Assert.False(fresh.FromCache);

        host.Clock.Advance(TimeSpan.FromHours(7));
        fail = true;

        var stale = await host.History.GetLastPlayedTimesAsync();
        Assert.True(stale.Answered);
        Assert.True(stale.FromCache);
        Assert.Equal(5, stale.Games.Count);
    }

    /// <summary>
    /// The parsed anchors, as the reconstruction takes them. Two of the five
    /// fixture entries carry a first-played date; the other three report
    /// <c>first_playtime: 0</c>, which is "not tracked".
    /// </summary>
    [Fact]
    public async Task The_anchor_map_projects_cumulative_minutes_per_appid()
    {
        using var host = new SteamWebTestHost(Fixtures(), apiKey: ApiKey);

        var anchors = await host.History.GetLastPlayedTimesAsync();

        Assert.Equal(SteamWebFixtures.EnshroudedAnchorMinutes, anchors.AnchorsByAppId["1203620"]);
        Assert.Equal(SteamWebFixtures.EnderalAnchorMinutes, anchors.AnchorsByAppId["933480"]);
        Assert.Equal(0, anchors.AnchorsByAppId["20"]);
        Assert.Equal(2, anchors.WithFirstPlayed);
    }
}
