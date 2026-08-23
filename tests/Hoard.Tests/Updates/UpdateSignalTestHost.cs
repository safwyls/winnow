using System.Net;
using Hoard.Data;
using Hoard.Enrich.Updates;
using Hoard.Enrich.Updates.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hoard.Tests.Updates;

/// <summary>A <see cref="TimeProvider"/> tests move by hand, for TTL and schedule assertions.</summary>
public sealed class UpdateTestClock : TimeProvider
{
    public UpdateTestClock(DateTimeOffset now) => Now = now;

    public DateTimeOffset Now { get; set; }

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;

    /// <summary>Moves to the same time on a later day — how the stagger tests travel.</summary>
    public void AdvanceDays(int days) => Now = Now.AddDays(days);
}

/// <summary>
/// A real service provider wired by
/// <see cref="ServiceCollectionExtensions.AddUpdateSignals(IServiceCollection)"/>,
/// with only the primary transport swapped for <see cref="FakeUpdateHandler"/>.
///
/// <para>Going through the actual DI extension rather than newing up the clients
/// is the point: handler order, singleton lifetimes and the two separate rate
/// limiters are themselves under test, and a hand-wired client would assert
/// nothing about the registration a host will actually use.</para>
///
/// <para>Pass a <see cref="TempDatabase"/> for the tests that need real SQL —
/// eligibility, idempotency, the end-to-end correlation. Omit it and everything
/// runs against in-memory storage.</para>
/// </summary>
public sealed class UpdateSignalTestHost : IDisposable
{
    private readonly ServiceProvider _services;

    public UpdateSignalTestHost(
        Func<RecordedUpdateRequest, int, HttpResponseMessage> responder,
        Action<UpdateSignalOptions>? configure = null,
        TempDatabase? database = null,
        IUpdateSignalCache? cache = null,
        DateTimeOffset? now = null,
        IReadOnlyList<PollCandidate>? candidates = null)
    {
        Handler = new FakeUpdateHandler(responder);
        Clock = new UpdateTestClock(now ?? new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        Cache = cache ?? new InMemoryUpdateSignalCache();

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));

        // Registered before AddUpdateSignals so its TryAdd calls defer to these.
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton(Cache);

        if (database is not null)
        {
            services.AddSingleton<ISqliteConnectionFactory>(database.Factory);
        }

        // A supplied candidate list overrides the SQL source even when a
        // database is present, so a schedule test can state its eligible set
        // outright instead of seeding a library to imply one.
        if (candidates is not null || database is null)
        {
            Candidates = new FakeCandidateSource(candidates ?? []);
            services.AddSingleton<IPollCandidateSource>(Candidates);
        }

        if (database is null)
        {
            EventWriter = new RecordingUpdateEventWriter();
            services.AddSingleton<IUpdateEventWriter>(EventWriter);
        }

        services.AddUpdateSignals(options =>
        {
            // Keep the backoff schedule and the rate limiters out of the way;
            // the tests that care about either override these deliberately.
            options.RetryBaseDelay = TimeSpan.FromMilliseconds(5);
            options.MaxRetryDelay = TimeSpan.FromMilliseconds(20);
            options.NewsRequestsPerSecond = 1000;
            options.BuildInfoRequestsPerSecond = 1000;
            configure?.Invoke(options);
        });

        services.AddHttpClient<ISteamNewsClient, SteamNewsClient>()
            .ConfigurePrimaryHttpMessageHandler(() => Handler);
        services.AddHttpClient<IBuildInfoClient, SteamCmdBuildInfoClient>()
            .ConfigurePrimaryHttpMessageHandler(() => Handler);

        _services = services.BuildServiceProvider();
    }

    public FakeUpdateHandler Handler { get; }

    public UpdateTestClock Clock { get; }

    public IUpdateSignalCache Cache { get; }

    /// <summary>The stubbed eligible set, when the test supplied one.</summary>
    public FakeCandidateSource? Candidates { get; }

    /// <summary>The in-memory event writer, when running without a database.</summary>
    public RecordingUpdateEventWriter? EventWriter { get; }

    public ISteamNewsClient News => _services.GetRequiredService<ISteamNewsClient>();

    public IBuildInfoClient Builds => _services.GetRequiredService<IBuildInfoClient>();

    public UpdateSignalPoller Poller => _services.GetRequiredService<UpdateSignalPoller>();

    public T Resolve<T>()
        where T : notnull
        => _services.GetRequiredService<T>();

    /// <summary>404 for anything a test did not explicitly arrange, so gaps are loud.</summary>
    public static HttpResponseMessage Unarranged(RecordedUpdateRequest request)
        => FakeUpdateHandler.Json(HttpStatusCode.NotFound, $$"""{"unarranged":"{{request.AppId}}"}""");

    public void Dispose() => _services.Dispose();
}

/// <summary>An eligible set supplied by the test rather than by a query.</summary>
public sealed class FakeCandidateSource : IPollCandidateSource
{
    private readonly IReadOnlyList<PollCandidate> _candidates;

    public FakeCandidateSource(IReadOnlyList<PollCandidate> candidates) => _candidates = candidates;

    public long? LastRetiredFloor { get; private set; }

    public Task<IReadOnlyList<PollCandidate>> GetEligibleAsync(
        long retiredFloorMinutes, CancellationToken ct = default)
    {
        LastRetiredFloor = retiredFloorMinutes;
        return Task.FromResult(_candidates);
    }
}

/// <summary>Captures written events without a database, for the schedule tests.</summary>
public sealed class RecordingUpdateEventWriter : IUpdateEventWriter
{
    private readonly List<Hoard.Core.Domain.UpdateEvent> _written = [];

    public IReadOnlyList<Hoard.Core.Domain.UpdateEvent> Written => _written.ToArray();

    public Task<bool> UpsertAsync(Hoard.Core.Domain.UpdateEvent updateEvent, CancellationToken ct = default)
    {
        var duplicate = _written.Any(e =>
            e.ReleaseId == updateEvent.ReleaseId
            && string.Equals(e.Kind, updateEvent.Kind, StringComparison.Ordinal)
            && e.OccurredAt == updateEvent.OccurredAt);

        _written.Add(updateEvent);
        return Task.FromResult(!duplicate);
    }
}
