using System.Diagnostics;
using System.Net;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Http;
using Xunit;

namespace Winnow.Tests.Igdb;

/// <summary>
/// §4.4 rate limiting and §4.2 429 handling, both enforced in the HttpClient
/// handler pipeline. No call site in Winnow is allowed to sleep, so these tests
/// assert on the pipeline, not on the client.
/// </summary>
public class IgdbResilienceTests
{
    private static readonly string[] TwoAppIds = ["440", "570"];

    [Fact]
    public async Task Rate_limiter_caps_the_initial_burst_and_spaces_the_rest_at_4_per_second()
    {
        // The §4.4 figure, as shipped.
        var options = new IgdbOptions();
        Assert.Equal(4, options.RequestsPerSecond);

        using var limiter = new IgdbRateLimiter(options);
        var handler = new FakeHttpMessageHandler(
            (_, _) => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "[]"));
        using var http = new HttpClient(new IgdbRateLimitingHandler(limiter) { InnerHandler = handler });

        var stopwatch = Stopwatch.StartNew();
        var inFlight = Enumerable.Range(0, 8)
            .Select(_ => http.GetAsync("https://api.igdb.com/v4/games"))
            .ToArray();

        // Well inside one replenishment period: the bucket holds 4 permits, so
        // exactly 4 of the 8 can have reached the transport. Unlimited, all 8
        // would be through by now.
        await Task.Delay(200);
        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal(4, limiter.QueuedRequests);

        var responses = await Task.WhenAll(inFlight);
        stopwatch.Stop();

        Assert.Equal(8, handler.Requests.Count);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        // The remaining four had to wait for a refill.
        Assert.True(
            stopwatch.Elapsed >= TimeSpan.FromMilliseconds(750),
            $"8 requests at 4/s finished in {stopwatch.ElapsedMilliseconds} ms — the limiter did not space them.");

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Rate_limiter_is_shared_by_every_client_drawing_on_the_same_credential()
    {
        // Two typed clients, one budget: a per-client limiter would multiply
        // the 4 req/s ceiling by the number of clients and get the credential
        // throttled.
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder());

        Assert.Same(host.Resolve<IgdbRateLimiter>(), host.Resolve<IgdbRateLimiter>());
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Retry_after_on_429_is_honoured_rather_than_the_exponential_schedule()
    {
        var retryAfter = TimeSpan.FromSeconds(1);

        using var host = new IgdbTestHost(
            (request, priorForEndpoint) => request.Endpoint switch
            {
                "token" => FakeHttpMessageHandler.Json(HttpStatusCode.OK, IgdbFixtures.TokenResponse("t")),
                "external_games" when priorForEndpoint == 0 => FakeHttpMessageHandler.TooManyRequests(retryAfter),
                "external_games" => FakeHttpMessageHandler.Json(
                    HttpStatusCode.OK, IgdbFixtures.ExternalGames(request.Body)),
                _ => FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, "[]"),
            },
            configure: options =>
            {
                // 5 ms base backoff: if Retry-After were ignored the retry would
                // land almost immediately, and the elapsed-time assertion below
                // would fail. That is the whole point of the gap.
                options.RetryBaseDelay = TimeSpan.FromMilliseconds(5);
                options.MaxRetryDelay = TimeSpan.FromSeconds(10);
            });

        var stopwatch = Stopwatch.StartNew();
        var matches = await host.Client.ResolveBySteamAppIdsAsync(TwoAppIds);
        stopwatch.Stop();

        Assert.Equal(2, host.Handler.CountFor("external_games"));
        Assert.Equal(2, matches.Count);
        Assert.True(
            stopwatch.Elapsed >= TimeSpan.FromMilliseconds(900),
            $"retry landed after {stopwatch.ElapsedMilliseconds} ms, ignoring Retry-After: 1.");
    }

    [Fact]
    public async Task Retry_after_longer_than_the_ceiling_is_capped()
    {
        using var host = new IgdbTestHost(
            (request, priorForEndpoint) => request.Endpoint switch
            {
                "token" => FakeHttpMessageHandler.Json(HttpStatusCode.OK, IgdbFixtures.TokenResponse("t")),
                "external_games" when priorForEndpoint == 0
                    => FakeHttpMessageHandler.TooManyRequests(TimeSpan.FromHours(2)),
                "external_games" => FakeHttpMessageHandler.Json(
                    HttpStatusCode.OK, IgdbFixtures.ExternalGames(request.Body)),
                _ => FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, "[]"),
            },
            configure: options => options.MaxRetryDelay = TimeSpan.FromMilliseconds(50));

        var stopwatch = Stopwatch.StartNew();
        var matches = await host.Client.ResolveBySteamAppIdsAsync(TwoAppIds);
        stopwatch.Stop();

        // A two-hour Retry-After must not be able to wedge a background job.
        Assert.Equal(2, matches.Count);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), "MaxRetryDelay did not cap Retry-After.");
    }

    [Fact]
    public async Task Transient_server_errors_are_retried_with_backoff_then_given_up_on()
    {
        using var host = new IgdbTestHost(
            (request, _) => request.Endpoint switch
            {
                "token" => FakeHttpMessageHandler.Json(HttpStatusCode.OK, IgdbFixtures.TokenResponse("t")),
                _ => FakeHttpMessageHandler.Json(HttpStatusCode.ServiceUnavailable, "{}"),
            },
            configure: options =>
            {
                options.MaxRetryAttempts = 2;
                options.RetryBaseDelay = TimeSpan.FromMilliseconds(1);
            });

        var matches = await host.Client.ResolveBySteamAppIdsAsync(TwoAppIds);

        // One initial attempt plus two retries, then a degraded — not crashed —
        // result: enrichment failing is never fatal (§5.1).
        Assert.Equal(3, host.Handler.CountFor("external_games"));
        Assert.Empty(matches);
    }
}
