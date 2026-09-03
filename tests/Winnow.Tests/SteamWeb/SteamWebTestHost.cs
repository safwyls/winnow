using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using Winnow.Core.Repositories;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Credentials;
using Winnow.Enrich.SteamWeb.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Winnow.Tests.SteamWeb;

/// <summary>A <see cref="TimeProvider"/> tests move by hand, for TTL assertions.</summary>
public sealed class SteamWebTestClock : TimeProvider
{
    public SteamWebTestClock(DateTimeOffset now) => Now = now;

    public DateTimeOffset Now { get; set; }

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>In-memory <see cref="ISettingsRepository"/> — the §6 key/value table without a database.</summary>
public sealed class InMemorySettingsRepository : ISettingsRepository
{
    private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);

    public Task<string?> GetAsync(string key, CancellationToken ct = default)
        => Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);

    public Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        _values[key] = value;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Captures every log line any component writes, so a test can assert on what
/// did — and above all did not — reach a log.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    /// <summary>Every rendered message, including the values of its structured arguments.</summary>
    public IReadOnlyList<string> Lines => _lines.ToArray();

    /// <summary>Everything written, as one blob — what a grep over the log file would see.</summary>
    public string AllText => string.Join('\n', _lines);

    public ILogger CreateLogger(string categoryName) => new Sink(categoryName, _lines);

    public void Dispose()
    {
    }

    private sealed class Sink : ILogger
    {
        private readonly string _category;
        private readonly ConcurrentQueue<string> _lines;

        public Sink(string category, ConcurrentQueue<string> lines)
        {
            _category = category;
            _lines = lines;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var builder = new StringBuilder()
                .Append(_category).Append(' ')
                .Append(logLevel.ToString()).Append(' ')
                .Append(formatter(state, exception));

            // Structured values are what a JSON sink would emit even when they
            // never appear in the rendered message, so they count as "logged".
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                foreach (var pair in values)
                {
                    builder.Append(" | ").Append(pair.Key).Append('=')
                        .Append(Convert.ToString(pair.Value, CultureInfo.InvariantCulture));
                }
            }

            if (exception is not null)
            {
                builder.Append(" | exception=").Append(exception);
            }

            _lines.Enqueue(builder.ToString());
        }
    }
}

/// <summary>
/// A real service provider wired by
/// <see cref="ServiceCollectionExtensions.AddSteamWebApi(IServiceCollection)"/>,
/// with only the primary transport swapped for
/// <see cref="FakeSteamWebHandler"/>.
///
/// <para>Going through the actual DI extension rather than newing up a
/// <see cref="SteamWebApiClient"/> is the point: it is the registration —
/// handler order, singleton lifetimes, the shared rate limiter, and above all
/// the removal of the framework's URI-printing loggers — that several of these
/// tests are asserting on.</para>
/// </summary>
public sealed class SteamWebTestHost : IDisposable
{
    /// <summary>Endpoint key as <see cref="RecordedSteamWebRequest.Endpoint"/> reports it.</summary>
    public const string GetOwnedGames = "IPlayerService/GetOwnedGames";

    /// <summary>M5's per-month longitudinal source.</summary>
    public const string GetUserYearInReview = "ISaleFeatureService/GetUserYearInReview";

    /// <summary>M5's cumulative anchor and first-played source.</summary>
    public const string ClientGetLastPlayedTimes = "IPlayerService/ClientGetLastPlayedTimes";

    private readonly ServiceProvider _services;

    /// <param name="renewalResponder">
    /// Canned answers for S6's renewal exchange. The default refuses every
    /// request with a 503, which is offline-safe and classified as transient, so
    /// a test that renews by accident degrades rather than reaching the network
    /// — and <see cref="RenewalHandler"/> records the attempt either way, which is
    /// what lets a test assert that NO renewal happened.
    /// </param>
    /// <param name="renewer">
    /// Replaces the real <see cref="SteamSessionRenewer"/> outright, for tests
    /// about what the provider does with an outcome rather than about how the
    /// outcome is obtained. Registered before <c>AddSteamWebApi</c>, so its
    /// <c>TryAdd</c> defers.
    /// </param>
    public SteamWebTestHost(
        Func<RecordedSteamWebRequest, int, HttpResponseMessage> responder,
        string? apiKey = "test-api-key",
        Action<SteamWebOptions>? configure = null,
        ISteamWebMetadataCache? cache = null,
        ISettingsRepository? settings = null,
        DateTimeOffset? now = null,
        Func<RecordedRenewalRequest, int, HttpResponseMessage>? renewalResponder = null,
        ISteamSessionRenewer? renewer = null)
    {
        Handler = new FakeSteamWebHandler(responder);
        RenewalHandler = new FakeSteamRenewalHandler(
            renewalResponder
            ?? ((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            }));
        Clock = new SteamWebTestClock(now ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Cache = cache ?? new InMemorySteamWebMetadataCache();
        Settings = settings ?? new InMemorySettingsRepository();
        Logs = new CapturingLoggerProvider();

        if (apiKey is not null)
        {
            Settings.SetAsync(SettingsTableApiKeySource.ApiKeySetting, apiKey).GetAwaiter().GetResult();
        }

        var services = new ServiceCollection();

        // Trace, not Warning: the point of several of these tests is that even
        // the most verbose sink never sees the key.
        services.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(Logs);
        });

        // Registered before AddSteamWebApi so its TryAdd calls defer to these.
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton(Cache);
        services.AddSingleton(Settings);

        if (renewer is not null)
        {
            services.AddSingleton(renewer);
        }

        services.AddSteamWebApi(options =>
        {
            // Keep the backoff schedule and the rate limiter out of the way; the
            // tests that care about either override these deliberately.
            options.RetryBaseDelay = TimeSpan.FromMilliseconds(5);
            options.MaxRetryDelay = TimeSpan.FromMilliseconds(20);
            options.RequestsPerSecond = 1000;
            configure?.Invoke(options);
        });

        services.AddHttpClient<ISteamWebApiClient, SteamWebApiClient>()
            .ConfigurePrimaryHttpMessageHandler(() => Handler);

        // The same substitution for the history client. One handler instance for
        // both, so a test can assert on the combined request sequence — which is
        // the point: the two clients share a rate limiter and are meant to be
        // countable as one stream of traffic to one host.
        services.AddHttpClient<ISteamHistoryClient, SteamHistoryClient>()
            .ConfigurePrimaryHttpMessageHandler(() => Handler);

        // S6's renewal client, on its own fake transport: it talks to two
        // different hosts and must be countable separately from the API traffic.
        // Registered last, so this primary handler replaces the real one.
        services.AddHttpClient(SteamSessionRenewer.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => RenewalHandler);

        _services = services.BuildServiceProvider();
    }

    public FakeSteamWebHandler Handler { get; }

    /// <summary>Every request S6's renewal exchange made, canned.</summary>
    public FakeSteamRenewalHandler RenewalHandler { get; }

    public SteamWebTestClock Clock { get; }

    public ISteamWebMetadataCache Cache { get; }

    public ISettingsRepository Settings { get; }

    public CapturingLoggerProvider Logs { get; }

    public ISteamWebApiClient Client => _services.GetRequiredService<ISteamWebApiClient>();

    /// <summary>M5's history client, on the same fake transport and the same limiter.</summary>
    public ISteamHistoryClient History => _services.GetRequiredService<ISteamHistoryClient>();

    public T Resolve<T>()
        where T : notnull
        => _services.GetRequiredService<T>();

    /// <summary>The default responder: answers the one endpoint from the captured fixture.</summary>
    public static Func<RecordedSteamWebRequest, int, HttpResponseMessage> DefaultResponder()
        => Always(SteamWebFixtures.CapturedResponse());

    /// <summary>A responder that answers every <c>GetOwnedGames</c> call with the given body.</summary>
    public static Func<RecordedSteamWebRequest, int, HttpResponseMessage> Always(string body)
        => (request, _) => request.Endpoint == GetOwnedGames
            ? FakeSteamWebHandler.Json(HttpStatusCode.OK, body)
            : FakeSteamWebHandler.Json(HttpStatusCode.NotFound, "{}");

    public void Dispose() => _services.Dispose();
}
