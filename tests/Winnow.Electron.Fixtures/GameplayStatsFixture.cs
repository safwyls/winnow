using System.Collections.Concurrent;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data;
using Winnow.Data.Repositories;

namespace Winnow.Electron.Fixtures;

internal sealed class GameplayStatsFixture(ISqliteConnectionFactory database, LibraryChangePublisher publisher)
{
    public static readonly DateTime Now = new(2026, 12, 1, 12, 0, 0, DateTimeKind.Utc);
    public string Kind { get; private set; } = "";
    public LibrarySnapshot? Preview { get; private set; }
    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<TimeProvider, GameplayFixtureClock>();
        services.AddSingleton<GameplayStatsFixture>();
        services.AddSingleton<GameplayFixtureControls>();
        services.AddSingleton<IGameplayStatsRepository, GameplayFixtureReader>();
        services.AddSingleton<IAccountStatsRepository, GameplayFixtureSpending>();
        services.AddSingleton<ILibraryQueryRepository, GameplayFixtureLibrary>();
    }

    public async Task<object> SeedAsync(string kind, string secondStore = "gog")
    {
        if (kind is not ("scope" or "xbox" or "preview" or "actual") || secondStore is not ("gog" or "plugin:xbox"))
            throw new ArgumentException("Unknown gameplay fixture.");
        using var connection = database.Open();
        if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM works") != 0)
            throw new InvalidOperationException("Gameplay fixture requires an empty database.");
        Kind = kind;
        if (kind == "preview")
        {
            // Linked pure source fixtures preserve the complete preview library and chart formula.
            Preview = PreviewLibrary.Snapshot();
            connection.Execute("INSERT INTO works(id,name,sort_name,first_release_year,publisher,summary) VALUES(@Id,@Name,@Name,@FirstReleaseYear,@Publisher,@Summary)", PreviewLibrary.Works);
            connection.Execute("INSERT INTO releases(id,work_id,name,platform) VALUES(@Id,@WorkId,@Name,@Platform)", PreviewLibrary.Releases);
            connection.Execute("INSERT INTO ownerships(id,release_id,store,installed,install_path,acquired_at,license_type,price_paid_cents) VALUES(@Id,@ReleaseId,@Store,@Installed,@InstallPath,@AcquiredAt,@LicenseType,@PricePaidCents)", PreviewLibrary.Ownerships);
            connection.Execute("INSERT INTO external_ids(release_id,provider,provider_id) VALUES(@ReleaseId,@Provider,@ProviderId)", PreviewLibrary.ExternalIds);
            await new IdentityLinkRepository(database).LinkAsync(new() { ParentWorkId = 3, ChildWorkIds = [30] });
        }
        else
        {
            connection.Execute("""
                INSERT INTO works(id,name,sort_name) VALUES(1,'Game 1','Game 1'),(2,'Game 2','Game 2');
                INSERT INTO releases(id,work_id,name,platform) VALUES(1,1,'Game 1','windows'),(2,2,'Game 2','windows');
                INSERT INTO ownerships(id,release_id,store,installed) VALUES(1,1,'steam',0),(2,2,@store,0);
                INSERT INTO external_ids(release_id,provider,provider_id) VALUES(1,'steam','1'),(2,'steam','2');
                INSERT INTO lists(id,name,is_smart) VALUES(1,'Try next',0);
                INSERT INTO list_items(list_id,release_id,position) VALUES(1,2,0),(1,1,1);
                """, new { store = kind == "xbox" ? "steam" : secondStore });
            if (kind == "actual")
                for (var id = 1; id <= 2; id++)
                    connection.Execute("""
                        INSERT INTO sessions(id,ownership_id,started_at,ended_at,duration_s,detection_method)
                        VALUES(@id,@id,@start,@end,@seconds,'manual')
                        """, new { id, start = Now.AddDays(-id), end = Now.AddDays(-id).AddHours(id), seconds = id * 3600 });
        }
        await publisher.PublishAsync(default);
        return new { Kind, Now, ProcessId = Environment.ProcessId, WorkCount = kind == "preview" ? 9 : 2,
            OwnershipCount = kind == "preview" ? 9 : 2, ResolvedGameCount = kind == "preview" ? 8 : 2 };
    }

    public async Task ChangeAsync(string change)
    {
        if (Kind == "preview") throw new InvalidOperationException("Source preview observations are immutable.");
        if (change == "hide2") await new HiddenGameRepository(database).HideAsync(2);
        else if (change == "link1-2") await new IdentityLinkRepository(database).LinkAsync(new() { ParentWorkId = 1, ChildWorkIds = [2] });
        else
        {
            using var connection = database.Open();
            if (change == "remove2") connection.Execute("DELETE FROM ownerships WHERE id=2");
            else if (change == "import-xbox") connection.Execute("UPDATE ownerships SET store='plugin:xbox' WHERE id=2");
            else throw new ArgumentException("Unknown gameplay change.");
        }
        await publisher.PublishAsync(default);
    }
    public Task PublishAsync() => publisher.PublishAsync(default);
}

internal sealed class GameplayFixtureClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(GameplayStatsFixture.Now);
}

internal sealed record GameplayFixtureMatch(string? Store, IReadOnlyList<GameplayOwnershipScope> Ownerships);
internal sealed record GameplayFixtureArm(string Behavior = "hold", double? Seconds = null, string Target = "dashboard",
    bool Repeat = false, GameplayFixtureMatch? Match = null);
internal sealed class GameplayFixtureControls
{
    private readonly Lock _gate = new();
    private readonly List<Plan> _plans = [];
    private readonly ConcurrentDictionary<string, Plan> _held = new();
    private readonly ConcurrentQueue<Call> _calls = new();
    private int _id;
    internal IReadOnlyList<Call> Calls => _calls.ToArray();
    public object Arm(GameplayFixtureArm input)
    {
        if (input.Behavior is not ("hold" or "fail" or "value") || input.Target is not ("dashboard" or "any")
            || (input.Repeat && input.Behavior != "value"))
            throw new ArgumentException("Unknown gameplay gate.");
        var plan = new Plan(Guid.NewGuid().ToString("N"), input);
        lock (_gate) _plans.Add(plan);
        return new { GateId = plan.Id };
    }
    public void Release(string? gateId = null, double? seconds = null)
    {
        foreach (var plan in _held.Values.Where(plan => gateId is null || plan.Id == gateId)) plan.Result.TrySetResult(seconds ?? plan.Input.Seconds);
    }
    public async Task<(Call Call, double? Seconds)> BeforeAsync(GameplayStatsRequest request, CancellationToken ct)
    {
        Plan? plan;
        lock (_gate)
        {
            plan = _plans.FirstOrDefault(value => Matches(value.Input, request));
            if (plan is { Input.Repeat: false }) _plans.Remove(plan);
        }
        var call = new Call(Interlocked.Increment(ref _id), request, ct, plan?.Id);
        if (plan is not null) _held[plan.Id] = plan;
        // Entered gates must be releasable before state polling can see their calls.
        _calls.Enqueue(call);
        if (plan?.Input.Behavior == "fail")
        {
            call.Failed = true; call.Completed = true;
            throw new InvalidOperationException("Source gameplay reader refusal.");
        }
        return (call, plan?.Input.Behavior == "hold" ? await plan.Result.Task : plan?.Input.Seconds);
    }
    private static bool Matches(GameplayFixtureArm input, GameplayStatsRequest request)
    {
        if (input.Target != "any" && !IsDashboardRequest(request)) return false;
        if (input.Match is not { } expected) return true;
        return request.Store == expected.Store && request.Ownerships.OrderBy(item => item.OwnershipId)
            .SequenceEqual(expected.Ownerships.OrderBy(item => item.OwnershipId));
    }
    private static bool IsDashboardRequest(GameplayStatsRequest request)
        => request.AsOfUtc == GameplayStatsFixture.Now && request.UntilUtc != request.AsOfUtc;
    public object Snapshot() => new { ProcessId = Environment.ProcessId, Calls = _calls.Select(call => new
    {
        call.Id, call.Request, call.GateId, call.Completed, call.Failed, call.ProcessId, call.IsDashboard,
        CancellationRequested = call.Token.IsCancellationRequested,
    }).ToArray() };
    private sealed class Plan(string id, GameplayFixtureArm input)
    {
        public string Id { get; } = id;
        public GameplayFixtureArm Input { get; } = input;
        public TaskCompletionSource<double?> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    internal sealed class Call(int id, GameplayStatsRequest request, CancellationToken token, string? gateId)
    {
        public int Id { get; } = id;
        public GameplayStatsRequest Request { get; } = request;
        public CancellationToken Token { get; } = token;
        public string? GateId { get; } = gateId;
        public bool Completed { get; set; }
        public bool Failed { get; set; }
        public int ProcessId { get; } = Environment.ProcessId;
        public bool IsDashboard => IsDashboardRequest(Request);
    }
}

internal sealed class GameplayFixtureReader(ISqliteConnectionFactory database, GameplayStatsFixture fixture,
    GameplayFixtureControls controls) : IGameplayStatsRepository
{
    public async Task<GameplayStats> GetAsync(GameplayStatsRequest request, CancellationToken ct = default)
    {
        var (call, seconds) = await controls.BeforeAsync(request, ct);
        try
        {
            if (seconds is { } value) return new() { RecordedSeconds = value };
            if (fixture.Kind == "preview") return await new PreviewGameplayStatsRepository().GetAsync(request, CancellationToken.None);
            if (fixture.Kind == "scope") return new();
            return await new GameplayStatsRepository(database).GetAsync(request, CancellationToken.None);
        }
        finally { call.Completed = true; }
    }
}

internal sealed class GameplayFixtureSpending : IAccountStatsRepository
{
    public Task<AccountStats> GetAsync(string source, CancellationToken ct = default) => Task.FromResult(new AccountStats
    {
        Source = source, TransactionCount = 1, GrossProductTransactionCount = 1, GrossProductSpendCents = 2000,
        Currencies = [new("$", 1)], Purchases = new(1, 2000), SpendByYear = [new(2026, 1, 2000)],
    });
}

internal sealed class GameplayFixtureLibrary(ISqliteConnectionFactory database, GameplayStatsFixture fixture) : ILibraryQueryRepository
{
    private readonly LibraryQueryRepository _real = new(database);
    public Task<LibrarySnapshot> GetSnapshotAsync(BucketThresholds thresholds, CancellationToken ct = default)
        => fixture.Preview is { } preview ? Task.FromResult(preview) : _real.GetSnapshotAsync(thresholds, GameplayStatsFixture.Now, ct);
    public async Task<IReadOnlyList<OwnershipBucket>> GetOwnershipBucketsAsync(BucketThresholds thresholds, CancellationToken ct = default)
        => (await GetSnapshotAsync(thresholds, ct)).Buckets;
    public Task<int> CountHiddenByAccountScopeAsync(BucketThresholds thresholds, CancellationToken ct = default) => _real.CountHiddenByAccountScopeAsync(thresholds, ct);
    public Task<int> CountHiddenByExplicitFilterAsync(BucketThresholds thresholds, CancellationToken ct = default) => _real.CountHiddenByExplicitFilterAsync(thresholds, ct);
    public Task<int> CountHiddenByRatingCapAsync(BucketThresholds thresholds, CancellationToken ct = default) => _real.CountHiddenByRatingCapAsync(thresholds, ct);
    public Task<IReadOnlyList<FacetTarget>> GetFacetTargetsAsync(CancellationToken ct = default) => _real.GetFacetTargetsAsync(ct);
}
