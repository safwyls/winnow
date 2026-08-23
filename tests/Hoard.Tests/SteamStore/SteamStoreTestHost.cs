using System.Net;
using Hoard.Enrich.Steam;
using Hoard.Enrich.Steam.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hoard.Tests.SteamStore;

/// <summary>A <see cref="TimeProvider"/> tests move by hand, for TTL assertions.</summary>
public sealed class StoreTestClock : TimeProvider
{
    public StoreTestClock(DateTimeOffset now) => Now = now;

    public DateTimeOffset Now { get; set; }

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>
/// A real service provider wired by
/// <see cref="ServiceCollectionExtensions.AddSteamStoreEnrichment(IServiceCollection)"/>,
/// with only the primary transport swapped for <see cref="FakeStoreHandler"/>.
///
/// <para>Going through the actual DI extension rather than newing up a
/// <see cref="SteamStoreClient"/> is the point: it is the registration — handler
/// order, singleton lifetimes, the shared rate limiter — that several of these
/// tests are asserting on.</para>
/// </summary>
public sealed class SteamStoreTestHost : IDisposable
{
    /// <summary>Endpoint keys as <see cref="RecordedStoreRequest.Endpoint"/> reports them.</summary>
    public const string GetItems = "IStoreBrowseService/GetItems";

    public const string GetTagList = "IStoreService/GetTagList";

    private readonly ServiceProvider _services;

    public SteamStoreTestHost(
        Func<RecordedStoreRequest, int, HttpResponseMessage> responder,
        Action<SteamStoreOptions>? configure = null,
        IStoreMetadataCache? cache = null,
        DateTimeOffset? now = null)
    {
        Handler = new FakeStoreHandler(responder);
        Clock = new StoreTestClock(now ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Cache = cache ?? new InMemoryStoreMetadataCache();

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));

        // Registered before AddSteamStoreEnrichment so its TryAdd calls defer to these.
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton(Cache);

        services.AddSteamStoreEnrichment(options =>
        {
            // Keep the backoff schedule and the rate limiter out of the way;
            // the tests that care about either override these deliberately.
            options.RetryBaseDelay = TimeSpan.FromMilliseconds(5);
            options.MaxRetryDelay = TimeSpan.FromMilliseconds(20);
            options.RequestsPerSecond = 1000;
            configure?.Invoke(options);
        });

        services.AddHttpClient<ISteamStoreClient, SteamStoreClient>()
            .ConfigurePrimaryHttpMessageHandler(() => Handler);

        _services = services.BuildServiceProvider();
    }

    public FakeStoreHandler Handler { get; }

    public StoreTestClock Clock { get; }

    public IStoreMetadataCache Cache { get; }

    public ISteamStoreClient Client => _services.GetRequiredService<ISteamStoreClient>();

    public T Resolve<T>()
        where T : notnull
        => _services.GetRequiredService<T>();

    /// <summary>The default responder: answers both endpoints from the generators.</summary>
    public static Func<RecordedStoreRequest, int, HttpResponseMessage> DefaultResponder(
        ISet<string>? nonStoreAppIds = null)
        => (request, _) => request.Endpoint switch
        {
            GetItems => FakeStoreHandler.Json(
                HttpStatusCode.OK, StoreFixtures.GetItemsFor(request, nonStoreAppIds)),
            GetTagList => FakeStoreHandler.Json(
                HttpStatusCode.OK, StoreFixtures.TagListFor([StoreFixtures.EldenRingAppId])),
            _ => FakeStoreHandler.Json(HttpStatusCode.NotFound, "{}"),
        };

    /// <summary>A responder that replays the captured live responses verbatim.</summary>
    public static Func<RecordedStoreRequest, int, HttpResponseMessage> CapturedResponder()
        => (request, _) => request.Endpoint switch
        {
            GetItems => FakeStoreHandler.Json(HttpStatusCode.OK, StoreFixtures.GetItemsResponse()),
            GetTagList => FakeStoreHandler.Json(HttpStatusCode.OK, StoreFixtures.TagListResponse()),
            _ => FakeStoreHandler.Json(HttpStatusCode.NotFound, "{}"),
        };

    public void Dispose() => _services.Dispose();
}
