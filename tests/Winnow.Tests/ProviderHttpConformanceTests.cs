using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Polly.Timeout;
using Winnow.Enrich.GamesDb;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Auth;
using Winnow.Enrich.Steam;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Credentials;
using Winnow.Enrich.Stores;
using Winnow.Enrich.Updates;
using Winnow.Ingest.Epic.Web;
using Winnow.Ingest.Epic.Web.Auth;
using Xunit;

namespace Winnow.Tests;

public sealed class ProviderHttpConformanceTests
{
    public static IEnumerable<object[]> Clients()
    {
        foreach (var name in new[] { "IIgdbClient", "IIgdbLifecycleClient", TwitchTokenProvider.HttpClientName,
            "ISteamStoreClient", "ISteamLifecycleClient", "ISteamWebApiClient", "ISteamHistoryClient",
            SteamSessionRenewer.HttpClientName, "IGameIdentityGraph", "ISteamNewsClient", "IBuildInfoClient",
            "IEpicAccountClient", "IEpicCatalogClient", EpicTokenProvider.HttpClientName, "StorefrontClient" })
            yield return [name];
    }

    public static IEnumerable<object[]> RateLimitedClients()
        => Clients().Where(row => (string)row[0] != TwitchTokenProvider.HttpClientName);

    [Theory, MemberData(nameof(RateLimitedClients))]
    public async Task Every_retry_spends_a_permit_in_the_registered_provider_budget(string name)
    {
        var terminal = new Terminal((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new TrackedContent("{}"),
            Headers = { RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero) },
        }));
        var budgets = new List<TokenBucketRateLimiter>();
        using var host = Host(terminal, rate: 1, budgets: budgets);
        using var client = host.GetRequiredService<IHttpClientFactory>().CreateClient(name);
        using var response = await client.GetAsync("https://fixture.invalid/query");
        Assert.Equal(3, terminal.Requests.Count);
        // Refill boundaries and delayed continuations can put legitimate sends
        // close together. Cumulative acquisitions prove that retries spent permits
        // regardless of how the runner schedules those continuations.
        Assert.NotEmpty(budgets);
        var usedBudget = Assert.Single(budgets, budget => budget.GetStatistics()!.TotalSuccessfulLeases > 0);
        Assert.Equal(3, usedBudget.GetStatistics()!.TotalSuccessfulLeases);
    }

    [Theory, MemberData(nameof(Clients))]
    public async Task Registered_pipeline_replays_and_disposes_every_retry_preserving_request_contract(string name)
    {
        var terminal = new Terminal((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new TrackedContent("{}"),
            Headers = { RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero) },
        }));
        using var host = Host(terminal);
        using var client = host.GetRequiredService<IHttpClientFactory>().CreateClient(name);
        Assert.Equal(64, client.MaxResponseContentBufferSize);
        Assert.Equal(TimeSpan.FromSeconds(5), client.Timeout);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://fixture.invalid/query")
            { Content = new StringContent("read-only fixture query"), Version = HttpVersion.Version20 };
        request.Options.Set(new HttpRequestOptionsKey<string>("captured-context"), "fixture");
        request.Headers.Add("X-Fixture", "yes");
        using var response = await client.SendAsync(request);
        Assert.Equal(3, terminal.Requests.Count);
        Assert.All(terminal.Bodies, body => Assert.Equal("read-only fixture query", body));
        foreach (var attempt in terminal.Requests)
        {
            Assert.Equal(HttpVersion.Version20, attempt.Version);
            Assert.Equal("yes", Assert.Single(attempt.Headers.GetValues("X-Fixture")));
            Assert.True(attempt.Options.TryGetValue(new HttpRequestOptionsKey<string>("captured-context"), out var value));
            Assert.Equal("fixture", value);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => attempt.Content!.ReadAsStringAsync());
        }
        Assert.All(terminal.Responses.Take(2), result => Assert.True(((TrackedContent)result.Content).Disposed));
        Assert.False(((TrackedContent)response.Content).Disposed);
        response.Dispose();
        Assert.True(((TrackedContent)response.Content).Disposed);
        Assert.Equal("read-only fixture query", await request.Content.ReadAsStringAsync());
        var filter = host.GetRequiredService<CapturePipeline>();
        var handlers = filter.Handlers[name];
        Assert.Contains(handlers, type => type.Contains("ResilienceHandler") || type == "StorefrontHandler");
        var retry = Array.FindIndex(handlers, type => type.Contains("ResilienceHandler"));
        var limit = Array.FindIndex(handlers, type => type.Contains("RateLimitingHandler"));
        if (limit >= 0) Assert.True(retry >= 0 && retry < limit);
    }

    [Theory, MemberData(nameof(Clients))]
    public async Task Caller_cancellation_never_retries_or_becomes_timeout(string name)
    {
        using var caller = new CancellationTokenSource();
        var terminal = new Terminal(async (_, token) =>
        {
            caller.Cancel();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException();
        });
        using var host = Host(terminal);
        using var client = host.GetRequiredService<IHttpClientFactory>().CreateClient(name);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("https://fixture.invalid/query", caller.Token));
        Assert.Single(terminal.Requests);
    }

    [Theory, MemberData(nameof(Clients))]
    public async Task Attempt_timeouts_retry_and_exhaust_as_transport_failure(string name)
    {
        var terminal = new Terminal(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException();
        });
        using var host = Host(terminal, attemptTimeout: TimeSpan.FromMilliseconds(25));
        using var client = host.GetRequiredService<IHttpClientFactory>().CreateClient(name);
        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://fixture.invalid/query"));
        Assert.IsType<TimeoutRejectedException>(failure.InnerException);
        Assert.Equal(3, terminal.Requests.Count);
    }

    [Theory, MemberData(nameof(Clients))]
    public async Task Unknown_length_oversized_response_is_disposed_without_retry(string name)
    {
        var terminal = new Terminal((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new TrackedContent(new string('x', 65), knownLength: false) }));
        using var host = Host(terminal);
        using var client = host.GetRequiredService<IHttpClientFactory>().CreateClient(name);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => client.GetAsync("https://fixture.invalid/query"));
        Assert.Single(terminal.Requests);
        Assert.True(((TrackedContent)Assert.Single(terminal.Responses).Content).Disposed);
    }

    [Theory, MemberData(nameof(Clients))]
    public async Task Response_body_read_is_inside_attempt_timeout_and_its_response_is_disposed(string name)
    {
        var terminal = new Terminal((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new TrackedContent("{}", wait: true) }));
        using var host = Host(terminal, attemptTimeout: TimeSpan.FromMilliseconds(25));
        using var client = host.GetRequiredService<IHttpClientFactory>().CreateClient(name);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://fixture.invalid/query"));
        Assert.Equal(3, terminal.Responses.Count);
        Assert.All(terminal.Responses, response => Assert.True(((TrackedContent)response.Content).Disposed));
    }

    [Theory, MemberData(nameof(Clients))]
    public async Task Overall_budget_stops_a_request_without_cancelling_the_callers_token(string name)
    {
        using var caller = new CancellationTokenSource();
        var terminal = new Terminal(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException();
        });
        using var host = Host(terminal, overallTimeout: TimeSpan.FromMilliseconds(75));
        using var client = host.GetRequiredService<IHttpClientFactory>().CreateClient(name);
        var failure = await Record.ExceptionAsync(() => client.GetAsync("https://fixture.invalid/query", caller.Token));
        // The outer HttpClient and Polly budgets share a deadline; either may
        // report first. Neither converts a caller cancellation into a retry.
        Assert.True(failure is HttpRequestException or TaskCanceledException);
        Assert.False(caller.IsCancellationRequested);
        Assert.Single(terminal.Requests);
    }

    private static ServiceProvider Host(Terminal terminal, TimeSpan? attemptTimeout = null,
        TimeSpan? overallTimeout = null, int rate = 1000, List<TokenBucketRateLimiter>? budgets = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IIgdbTokenProvider, IgdbTokens>();
        services.AddSingleton<IEpicTokenProvider, EpicTokens>();
        services.AddIgdbEnrichment(Configure);
        services.AddSteamStoreEnrichment(Configure);
        services.AddSteamWebApi(Configure);
        services.AddGamesDbIdentityGraph(Configure);
        services.AddUpdateSignals(Configure);
        services.AddEpicWebApi(Configure);
        services.AddStorefrontEnrichment(Configure);
        services.AddSingleton(new CapturePipeline(terminal));
        services.AddSingleton<IHttpMessageHandlerBuilderFilter>(provider => provider.GetRequiredService<CapturePipeline>());
        var host = services.BuildServiceProvider();
        if (budgets is not null)
        {
            // Inspect the registered budgets without adding test hooks to provider
            // APIs. Include inherited fields for the shared update-signal budgets.
            foreach (var serviceType in services.Select(service => service.ServiceType).Distinct())
            {
                for (var type = serviceType; type is not null; type = type.BaseType)
                {
                    foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                                 .Where(field => field.FieldType == typeof(TokenBucketRateLimiter)))
                    {
                        budgets.Add((TokenBucketRateLimiter)field.GetValue(host.GetRequiredService(serviceType))!);
                    }
                }
            }
        }
        return host;

        void Configure(object options)
        {
            Set("MaxResponseBytes", 64L);
            Set("AttemptTimeout", attemptTimeout ?? TimeSpan.FromSeconds(3));
            Set("OverallTimeout", overallTimeout ?? TimeSpan.FromSeconds(5));
            Set("MaxRetryAttempts", 2);
            Set("RetryBaseDelay", TimeSpan.FromMilliseconds(1));
            Set("RequestsPerSecond", rate);
            Set("NewsRequestsPerSecond", rate);
            Set("BuildInfoRequestsPerSecond", rate);
            void Set(string property, object value) => options.GetType().GetProperty(property)?.SetValue(options, value);
        }
    }

    private sealed class CapturePipeline(Terminal terminal) : IHttpMessageHandlerBuilderFilter
    {
        public Dictionary<string, string[]> Handlers { get; } = [];
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
        {
            next(builder);
            Handlers[builder.Name!] = builder.AdditionalHandlers.Select(handler => handler.GetType().Name).ToArray();
            builder.PrimaryHandler = terminal;
        };
    }

    private sealed class Terminal(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string?> Bodies { get; } = [];
        public List<HttpResponseMessage> Responses { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(ct));
            var response = await respond(request, ct);
            Responses.Add(response);
            return response;
        }
    }

    private sealed class TrackedContent(string text, bool knownLength = true, bool wait = false) : HttpContent
    {
        private readonly byte[] _bytes = System.Text.Encoding.UTF8.GetBytes(text);
        public bool Disposed { get; private set; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => SerializeToStreamAsync(stream, context, default);
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken ct)
        {
            if (wait) await Task.Delay(Timeout.Infinite, ct);
            await stream.WriteAsync(_bytes, ct);
        }
        protected override bool TryComputeLength(out long length) { length = _bytes.Length; return knownLength; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class IgdbTokens : IIgdbTokenProvider
    {
        public Task<IgdbAccessToken?> GetAsync(CancellationToken ct = default)
            => Task.FromResult<IgdbAccessToken?>(new("fixture-client", "fixture-token", DateTimeOffset.UtcNow.AddHours(1)));
        public Task<IgdbAccessToken?> RefreshAsync(IgdbAccessToken? staleToken, CancellationToken ct = default) => GetAsync(ct);
    }

    private sealed class EpicTokens : IEpicTokenProvider
    {
        public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default) => ValueTask.FromResult(true);
        public ValueTask<bool> IsSignedInAsync(CancellationToken ct = default) => ValueTask.FromResult(true);
        public Task<EpicOAuthToken?> GetAsync(CancellationToken ct = default) => Task.FromResult<EpicOAuthToken?>(
            new("fixture-client", "fixture-token", "fixture-refresh", "fixture-account", "Fixture", DateTimeOffset.UtcNow.AddHours(1), null));
        public ValueTask<EpicSessionIdentity?> GetIdentityAsync(CancellationToken ct = default)
            => ValueTask.FromResult<EpicSessionIdentity?>(new("fixture-account", "fixture-client", 1));
        public Task<EpicOAuthToken?> RefreshAsync(EpicOAuthToken? staleToken, CancellationToken ct = default) => GetAsync(ct);
        public Task SignOutAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<EpicSignInResult> SignInWithAuthorizationCodeAsync(string authorizationCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<EpicSignInResult> SignInWithExchangeCodeAsync(string exchangeCode, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
