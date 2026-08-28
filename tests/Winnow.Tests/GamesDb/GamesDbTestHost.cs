using System.Net;
using System.Text;
using Winnow.Enrich.GamesDb;
using Winnow.Enrich.GamesDb.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Winnow.Tests.GamesDb;

/// <summary>One outbound gamesdb request, captured before it would have hit the wire.</summary>
public sealed record RecordedGamesDbRequest(HttpMethod Method, Uri Uri, string? UserAgent)
{
    /// <summary>The platform segment: <c>platforms/{platform}/external_releases/{id}</c>.</summary>
    public string Platform => Uri.Segments.Length >= 3 ? Uri.Segments[2].Trim('/') : string.Empty;

    /// <summary>The id segment, still URL-encoded as it went out.</summary>
    public string ExternalId => Uri.Segments[^1].Trim('/');
}

/// <summary>
/// The only transport these tests use. Nothing in this file opens a socket:
/// every response is canned, per the enrichment charter's rule that HTTP clients
/// are tested against fixtures and never against live APIs.
/// </summary>
public sealed class FakeGamesDbHandler : HttpMessageHandler
{
    private readonly Func<RecordedGamesDbRequest, int, HttpResponseMessage> _responder;
    private readonly Lock _lock = new();
    private readonly List<RecordedGamesDbRequest> _requests = [];

    /// <param name="responder">
    /// Given the request and the zero-based count of prior requests for the same
    /// (platform, id), returns the canned response. The counter is what lets a
    /// test say "fail the first attempt, succeed the second".
    /// </param>
    public FakeGamesDbHandler(Func<RecordedGamesDbRequest, int, HttpResponseMessage> responder)
        => _responder = responder;

    /// <summary>Every request seen, in order.</summary>
    public IReadOnlyList<RecordedGamesDbRequest> Requests
    {
        get
        {
            lock (_lock)
            {
                return _requests.ToArray();
            }
        }
    }

    public int CountFor(string platform, string externalId)
        => Requests.Count(r => r.Platform == platform && r.ExternalId == externalId);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var recorded = new RecordedGamesDbRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.TryGetValues("User-Agent", out var agents) ? string.Join(' ', agents) : null);

        int prior;
        lock (_lock)
        {
            prior = _requests.Count(r =>
                r.Platform == recorded.Platform && r.ExternalId == recorded.ExternalId);
            _requests.Add(recorded);
        }

        return Task.FromResult(_responder(recorded, prior));
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>The shape that must never be cached: an outage, not an answer.</summary>
    public static HttpResponseMessage ServiceUnavailable()
        => Json(HttpStatusCode.ServiceUnavailable, "{}");

    /// <summary>The shape that MUST be cached: "no release under this id".</summary>
    public static HttpResponseMessage NotFound() => Json(HttpStatusCode.NotFound, string.Empty);
}

/// <summary>A <see cref="TimeProvider"/> tests move by hand, for TTL assertions.</summary>
public sealed class GamesDbTestClock : TimeProvider
{
    public GamesDbTestClock(DateTimeOffset now) => Now = now;

    public DateTimeOffset Now { get; set; }

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>
/// A real service provider wired by <see cref="ServiceCollectionExtensions.AddGamesDbIdentityGraph"/>,
/// with only the primary transport swapped.
///
/// <para>Going through the actual DI extension rather than newing up a
/// <see cref="GamesDbClient"/> is the point: handler order and the shared rate
/// limiter are part of what these tests assert on.</para>
/// </summary>
public sealed class GamesDbTestHost : IDisposable
{
    private readonly ServiceProvider _services;

    public GamesDbTestHost(
        Func<RecordedGamesDbRequest, int, HttpResponseMessage> responder,
        Action<GamesDbOptions>? configure = null,
        IGamesDbCache? cache = null,
        DateTimeOffset? now = null)
    {
        Handler = new FakeGamesDbHandler(responder);
        Clock = new GamesDbTestClock(now ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Cache = cache ?? new InMemoryGamesDbCache();

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));

        // Registered before AddGamesDbIdentityGraph so its TryAdd calls defer to these.
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton(Cache);

        services.AddGamesDbIdentityGraph(options =>
        {
            // Keep the backoff schedule short. The rate limiter is left at its
            // real value: a test that raised it would stop exercising the gate
            // these tests exist to keep in the pipeline.
            options.RetryBaseDelay = TimeSpan.FromMilliseconds(5);
            options.MaxRetryDelay = TimeSpan.FromMilliseconds(50);
            configure?.Invoke(options);
        });

        services.AddHttpClient<IGameIdentityGraph, GamesDbClient>()
            .ConfigurePrimaryHttpMessageHandler(() => Handler);

        _services = services.BuildServiceProvider();
    }

    public FakeGamesDbHandler Handler { get; }

    public GamesDbTestClock Clock { get; }

    public IGamesDbCache Cache { get; }

    public IGameIdentityGraph Client => _services.GetRequiredService<IGameIdentityGraph>();

    public T Resolve<T>()
        where T : notnull
        => _services.GetRequiredService<T>();

    /// <summary>Answers the Fez fixture for <c>epic/Bluebird</c> and 404 for everything else.</summary>
    public static Func<RecordedGamesDbRequest, int, HttpResponseMessage> FezResponder()
        => (request, _) => request is { Platform: "epic", ExternalId: "Bluebird" }
            ? FakeGamesDbHandler.Json(HttpStatusCode.OK, GamesDbFixtures.EpicBluebird())
            : FakeGamesDbHandler.NotFound();

    public void Dispose() => _services.Dispose();
}

/// <summary>The captured gamesdb response. See tests/fixtures/gamesdb/README.md.</summary>
public static class GamesDbFixtures
{
    /// <summary>Epic's codename for Fez, and the spike's worked example.</summary>
    public const string FezAppName = "Bluebird";

    /// <summary>Fez's Steam appid — what the Epic hop is supposed to arrive at.</summary>
    public const string FezSteamAppId = "224760";

    /// <summary>Fez's GOG product id, the second-choice hop.</summary>
    public const string FezGogId = "1207659211";

    private static readonly string FixtureRoot =
        Path.Combine(AppContext.BaseDirectory, "fixtures", "gamesdb");

    public static string EpicBluebird()
        => File.ReadAllText(Path.Combine(FixtureRoot, "epic-bluebird.json"));
}
