using System.Net;
using Hoard.Enrich.Igdb;
using Hoard.Enrich.Igdb.Auth;
using Hoard.Enrich.Igdb.Credentials;
using Hoard.Enrich.Igdb.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hoard.Tests.Igdb;

/// <summary>A <see cref="TimeProvider"/> tests move by hand, for TTL assertions.</summary>
public sealed class IgdbTestClock : TimeProvider
{
    public IgdbTestClock(DateTimeOffset now) => Now = now;

    public DateTimeOffset Now { get; set; }

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>
/// A real service provider wired by <see cref="ServiceCollectionExtensions.AddIgdbEnrichment"/>,
/// with only the primary transport swapped for <see cref="FakeHttpMessageHandler"/>.
///
/// <para>Going through the actual DI extension rather than newing up an
/// <see cref="IgdbClient"/> is the point: it is the registration — handler
/// order, singleton lifetimes, the shared rate limiter — that these tests are
/// mostly asserting on.</para>
/// </summary>
public sealed class IgdbTestHost : IDisposable
{
    private readonly ServiceProvider _services;

    public IgdbTestHost(
        Func<RecordedRequest, int, HttpResponseMessage> responder,
        string? clientId = "test-client",
        string? clientSecret = "test-secret",
        Action<IgdbOptions>? configure = null,
        IMetadataCache? cache = null,
        ISettingsStore? settings = null,
        DateTimeOffset? now = null)
    {
        Handler = new FakeHttpMessageHandler(responder);
        Clock = new IgdbTestClock(now ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Cache = cache ?? new InMemoryMetadataCache();
        Settings = settings ?? new InMemorySettingsStore();

        if (clientId is not null)
        {
            Settings.SetAsync(SettingsTableCredentialSource.ClientIdKey, clientId).GetAwaiter().GetResult();
        }

        if (clientSecret is not null)
        {
            Settings.SetAsync(SettingsTableCredentialSource.ClientSecretKey, clientSecret).GetAwaiter().GetResult();
        }

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));

        // Registered before AddIgdbEnrichment so its TryAdd calls defer to these.
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton(Cache);
        services.AddSingleton(Settings);

        services.AddIgdbEnrichment(options =>
        {
            // Keep the backoff schedule short; the Retry-After test overrides
            // this deliberately because honouring the header is the assertion.
            options.RetryBaseDelay = TimeSpan.FromMilliseconds(5);
            options.MaxRetryDelay = TimeSpan.FromSeconds(5);
            configure?.Invoke(options);
        });

        services.AddHttpClient(TwitchTokenProvider.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => Handler);
        services.AddHttpClient<IIgdbClient, IgdbClient>()
            .ConfigurePrimaryHttpMessageHandler(() => Handler);

        _services = services.BuildServiceProvider();
    }

    public FakeHttpMessageHandler Handler { get; }

    public IgdbTestClock Clock { get; }

    public IMetadataCache Cache { get; }

    public ISettingsStore Settings { get; }

    public IIgdbClient Client => _services.GetRequiredService<IIgdbClient>();

    public TwitchTokenProvider TokenProvider
        => (TwitchTokenProvider)_services.GetRequiredService<IIgdbTokenProvider>();

    public T Resolve<T>()
        where T : notnull
        => _services.GetRequiredService<T>();

    /// <summary>
    /// The default responder: mints a token for Twitch and answers both IGDB
    /// endpoints from the fixtures.
    /// </summary>
    public static Func<RecordedRequest, int, HttpResponseMessage> DefaultResponder(
        ISet<string>? unknownAppIds = null)
        => (request, _) => request.Endpoint switch
        {
            "token" => FakeHttpMessageHandler.Json(
                HttpStatusCode.OK, IgdbFixtures.TokenResponse("token-" + Guid.NewGuid().ToString("N"))),
            "external_games" => FakeHttpMessageHandler.Json(
                HttpStatusCode.OK, IgdbFixtures.ExternalGames(request.Body, unknownAppIds)),
            "games" => FakeHttpMessageHandler.Json(HttpStatusCode.OK, IgdbFixtures.Games(request.Body)),
            _ => FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, "[]"),
        };

    public void Dispose() => _services.Dispose();
}
