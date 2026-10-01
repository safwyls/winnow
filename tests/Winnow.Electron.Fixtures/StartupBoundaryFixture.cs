using System.Collections.Concurrent;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Winnow.App.Services;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data;
using Winnow.Data.Repositories;
using Winnow.Enrich.Igdb.Credentials;

namespace Winnow.Electron.Fixtures;

internal sealed class StartupBoundaryFixture(StartupReadDatabase database, FirstRunSetupService setup)
{
    public bool Ready { get; private set; }
    public bool Fullscreen { get; private set; }
    public bool Setup { get; private set; }
    public static void Register(IServiceCollection services, string directory)
    {
        services.AddSingleton(new StartupReadDatabase(new SqliteConnectionFactory(Path.Combine(directory, "winnow.db"), pooling: false)));
        services.RemoveAll<ISqliteConnectionFactory>();
        services.AddSingleton<ISqliteConnectionFactory>(provider => provider.GetRequiredService<StartupReadDatabase>());
        services.RemoveAll<ILibraryQueryRepository>();
        services.AddSingleton<ILibraryQueryRepository>(provider => new StartupLibraryReader(provider.GetRequiredService<StartupReadDatabase>()));
        services.AddSingleton<StartupBoundaryFixture>();
        services.RemoveAll<IIgdbCredentialProvider>();
        services.AddSingleton<IIgdbCredentialProvider, StartupNoCredentials>();
    }

    public async Task InitializeAsync(bool fullscreen, bool firstRun)
    {
        using (var connection = database.Open())
        {
            if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM works") != 0)
                throw new InvalidOperationException("Startup fixture must start with an empty database.");
            // Literal LibraryReadFixtures.Seed(20), before the host publishes discovery or serves reads.
            connection.Execute("""
                WITH RECURSIVE seq(id) AS (SELECT 1 UNION ALL SELECT id + 1 FROM seq WHERE id < 20)
                INSERT INTO works (id, name, sort_name) SELECT id, 'Game ' || id, 'Game ' || id FROM seq;
                INSERT INTO releases (id, work_id, name, platform) SELECT id, id, name, 'windows' FROM works;
                INSERT INTO ownerships (id, release_id, store, installed) SELECT id, id, 'steam', 0 FROM releases;
                INSERT INTO external_ids (release_id, provider, provider_id) SELECT id, 'steam', CAST(id AS TEXT) FROM releases;
                INSERT INTO lists (id, name, is_smart) VALUES (1, 'Try next', 0);
                INSERT INTO list_items (list_id, release_id, position) VALUES (1, 20, 0), (1, 1, 1);
                """);
        }
        await new SettingsRepository(database).SetAsync("application.start_in_fullscreen", fullscreen ? "true" : "false");
        await setup.SaveAsync(firstRun ? 0 : null);
        Fullscreen = fullscreen;
        Setup = firstRun;
        Ready = true;
    }

    public object State() => new
    {
        Ready, Fullscreen, Setup, ProcessId = Environment.ProcessId,
        Source = "LibraryReadFixtures.Seed(20)", Calls = database.Calls.ToArray(),
    };

    public static void Map(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? "";
            if (!path.StartsWith("/api/v1/", StringComparison.Ordinal) || path == "/api/v1/events")
            { await next(context); return; }
            var database = context.RequestServices.GetRequiredService<StartupReadDatabase>();
            using var scope = database.BeginCall(path, context.Request.Method);
            try { await next(context); }
            finally
            {
                scope.Call.Status = context.Response.StatusCode;
                scope.Call.Canceled = context.RequestAborted.IsCancellationRequested;
                scope.Call.Completed = true;
                scope.Call.CompletedThreadId = Environment.CurrentManagedThreadId;
            }
        });
        app.MapGet("/__fixture/startup-boundary/state", (StartupBoundaryFixture fixture) => fixture.State());
    }
}

internal sealed class StartupNoCredentials : IIgdbCredentialProvider
{
    public ValueTask<IgdbCredentials?> GetAsync(CancellationToken ct = default) => ValueTask.FromResult<IgdbCredentials?>(null);
    public void Invalidate() { }
}

internal sealed class StartupReadCall(int id, string route, string method)
{
    public int Id { get; } = id;
    public string Route { get; } = route;
    public string Method { get; } = method;
    public int ProcessId { get; } = Environment.ProcessId;
    public int StartedThreadId { get; } = Environment.CurrentManagedThreadId;
    public int CompletedThreadId { get; set; }
    public bool Completed { get; set; }
    public bool Canceled { get; set; }
    public int Status { get; set; }
    public ConcurrentQueue<StartupLease> Leases { get; } = [];
    public ConcurrentQueue<StartupSnapshot> Snapshots { get; } = [];
}
internal sealed record StartupLease(string Operation, int ProcessId, int ThreadId, bool ThreadPool, string? SynchronizationContext);
internal sealed record StartupSnapshot(int WorkCount, int OwnershipCount, int BucketCount, long[] ListReleaseIds);

internal sealed class StartupReadDatabase(ISqliteConnectionFactory inner) : ISqliteConnectionFactory
{
    private readonly AsyncLocal<StartupReadCall?> _call = new();
    private readonly AsyncLocal<string?> _operation = new();
    private int _nextId;
    public ConcurrentQueue<StartupReadCall> Calls { get; } = [];
    public StartupReadCall? Current => _call.Value;
    public string DatabasePath => inner.DatabasePath;
    public string ConnectionString => inner.ConnectionString;
    public SqliteConnection Open() => inner.Open();
    public IUnitOfWork Begin() => inner.Begin();
    public IUnitOfWork BeginRead() => inner.BeginRead();
    public void ReleasePooledConnections() => inner.ReleasePooledConnections();
    public DbLease Lease()
    {
        Current?.Leases.Enqueue(new(_operation.Value ?? "request", Environment.ProcessId,
            Environment.CurrentManagedThreadId, Thread.CurrentThread.IsThreadPoolThread,
            SynchronizationContext.Current?.GetType().FullName));
        return inner.Lease();
    }
    public CallScope BeginCall(string route, string method)
    {
        var previous = _call.Value;
        var call = new StartupReadCall(Interlocked.Increment(ref _nextId), route, method);
        Calls.Enqueue(call);
        _call.Value = call;
        return new CallScope(call, () => _call.Value = previous);
    }
    public IDisposable SnapshotScope()
    {
        var previous = _operation.Value;
        _operation.Value = "library.snapshot";
        return new Scope(() => _operation.Value = previous);
    }
    internal sealed class CallScope(StartupReadCall call, Action close) : IDisposable
    {
        public StartupReadCall Call { get; } = call;
        public void Dispose() => close();
    }
    private sealed class Scope(Action close) : IDisposable { public void Dispose() => close(); }
}

internal sealed class StartupLibraryReader(StartupReadDatabase database) : ILibraryQueryRepository
{
    private readonly LibraryQueryRepository _inner = new(database);
    public Task<LibrarySnapshot> GetSnapshotAsync(BucketThresholds thresholds, CancellationToken ct = default)
        => ReadAsync(() => _inner.GetSnapshotAsync(thresholds, ct));
    public Task<LibrarySnapshot> GetSnapshotAsync(BucketThresholds thresholds, DateTime asOfUtc, CancellationToken ct = default)
        => ReadAsync(() => _inner.GetSnapshotAsync(thresholds, asOfUtc, ct));
    private async Task<LibrarySnapshot> ReadAsync(Func<Task<LibrarySnapshot>> read)
    {
        using var scope = database.SnapshotScope();
        var snapshot = await read();
        database.Current?.Snapshots.Enqueue(new(snapshot.Works.Count, snapshot.Ownerships.Count,
            snapshot.Buckets.Count, snapshot.ListItems.OrderBy(item => item.Position).Select(item => item.ReleaseId).ToArray()));
        return snapshot;
    }
    public Task<IReadOnlyList<OwnershipBucket>> GetOwnershipBucketsAsync(BucketThresholds thresholds, CancellationToken ct = default) => _inner.GetOwnershipBucketsAsync(thresholds, ct);
    public Task<int> CountHiddenByAccountScopeAsync(BucketThresholds thresholds, CancellationToken ct = default) => _inner.CountHiddenByAccountScopeAsync(thresholds, ct);
    public Task<int> CountHiddenByExplicitFilterAsync(BucketThresholds thresholds, CancellationToken ct = default) => _inner.CountHiddenByExplicitFilterAsync(thresholds, ct);
    public Task<int> CountHiddenByRatingCapAsync(BucketThresholds thresholds, CancellationToken ct = default) => _inner.CountHiddenByRatingCapAsync(thresholds, ct);
    public Task<IReadOnlyList<FacetTarget>> GetFacetTargetsAsync(CancellationToken ct = default) => _inner.GetFacetTargetsAsync(ct);
}
