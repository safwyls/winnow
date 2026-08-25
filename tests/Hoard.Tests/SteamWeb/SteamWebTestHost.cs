using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using Hoard.Core.Repositories;
using Hoard.Enrich.SteamWeb;
using Hoard.Enrich.SteamWeb.Credentials;
using Hoard.Enrich.SteamWeb.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hoard.Tests.SteamWeb;

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

    private readonly ServiceProvider _services;

    public SteamWebTestHost(
        Func<RecordedSteamWebRequest, int, HttpResponseMessage> responder,
        string? apiKey = "test-api-key",
        Action<SteamWebOptions>? configure = null,
        ISteamWebMetadataCache? cache = null,
        ISettingsRepository? settings = null,
        DateTimeOffset? now = null)
    {
        Handler = new FakeSteamWebHandler(responder);
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

        _services = services.BuildServiceProvider();
    }

    public FakeSteamWebHandler Handler { get; }

    public SteamWebTestClock Clock { get; }

    public ISteamWebMetadataCache Cache { get; }

    public ISettingsRepository Settings { get; }

    public CapturingLoggerProvider Logs { get; }

    public ISteamWebApiClient Client => _services.GetRequiredService<ISteamWebApiClient>();

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
