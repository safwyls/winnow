using System.Collections.Concurrent;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Services;
using Winnow.Application;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data;
using Winnow.Data.Repositories;

namespace Winnow.Electron.Fixtures;

internal sealed class JournalActivityFixture(ISqliteConnectionFactory database, LibraryChangePublisher publisher,
    IApplicationChangePublisher changes, SessionJournalService journal, JournalActivityControls controls)
{
    internal static void Register(IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<JournalActivityControls>();
        services.AddSingleton<JournalActivityFixture>();
        services.AddSingleton<ISessionRepository, JournalSessionRepository>();
        services.AddSingleton<IAccountStatsRepository>(provider => new JournalAccountReader(
            provider.GetRequiredService<ISqliteConnectionFactory>(), provider.GetRequiredService<JournalActivityControls>(),
            () => provider.GetRequiredService<JournalActivityFixture>()));
        services.AddSingleton<IActivityRepository>(provider => new JournalActivityReader(
            provider.GetRequiredService<ISqliteConnectionFactory>(), provider.GetRequiredService<JournalActivityControls>(),
            () => provider.GetRequiredService<JournalActivityFixture>()));
    }
    public string Kind { get; private set; } = "";
    private Session? _recovered;
    private MonitoredProcessIdentity? _process;
    private long _sessionId;

    public async Task<JournalActivitySeedResult> SeedAsync(string kind)
    {
        if (kind is not ("journal-vm" or "details" or "editor-existing" or "editor-empty" or "fullscreen-existing"
            or "hierarchy" or "recovered" or "empty" or "reload" or "paging" or "paging-disposal" or "week-race" or "account"))
            throw new ArgumentException("Unknown journal/activity fixture.");
        using var seed = database.Open();
        if (seed.ExecuteScalar<int>("SELECT COUNT(*) FROM works") != 0)
            throw new InvalidOperationException("Journal/activity fixture requires an empty library.");
        Kind = kind;
        var count = kind is "empty" or "reload" or "fullscreen-existing" ? 4 : kind == "recovered" ? 2 : 3;
        seed.Execute("""
            WITH RECURSIVE seq(id) AS (SELECT 1 UNION ALL SELECT id+1 FROM seq WHERE id<@count)
            INSERT INTO works(id,name,sort_name) SELECT id,'Game '||id,'Game '||id FROM seq;
            INSERT INTO releases(id,work_id,name,platform) SELECT id,id,name,'windows' FROM works;
            INSERT INTO ownerships(id,release_id,store,installed) SELECT id,id,'steam',0 FROM releases;
            INSERT INTO external_ids(release_id,provider,provider_id) SELECT id,'steam',CAST(id AS TEXT) FROM releases;
            INSERT INTO lists(id,name,is_smart) VALUES(1,'Try next',0);
            INSERT INTO list_items(list_id,release_id,position) VALUES(1,@count,0),(1,1,1);
            """, new { count });
        var now = DateTime.UtcNow;
        var owner = kind is "journal-vm" or "editor-existing" or "editor-empty" or "hierarchy" ? 3 : 1;
        _sessionId = kind == "details" ? 42 : owner == 3 ? 7 : 1;
        var at = kind switch
        {
            "journal-vm" => new DateTime(2026, 8, 22, 21, 0, 0, DateTimeKind.Utc),
            "details" => new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc),
            "editor-existing" or "editor-empty" => new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc),
            "hierarchy" => now.AddSeconds(-7800),
            "recovered" => now.AddMinutes(-11),
            _ => now,
        };
        var title = kind == "details" ? "Bluebird" : kind == "hierarchy"
            ? "The Long Journey Home — Definitive Collector's Edition" : null;
        if (title is not null) seed.Execute("""
            UPDATE works SET name=@title,sort_name=@title WHERE id=@owner;
            UPDATE releases SET name=@title WHERE work_id=@owner;
            """, new { title, owner });
        if (kind == "recovered")
        {
            _process = new(100, at, "game");
            var sessions = new SessionRepository(database);
            var checkpoint = await sessions.SaveMonitoredAsync(new Session
            {
                OwnershipId = 1, StartedAt = at, DetectionMethod = DetectionMethods.ProcessWatch, MonitorKey = "sitting",
            }, [_process]);
            await sessions.SetNoteAsync(new SessionNote { SessionId = checkpoint.Id, Note = "Same sitting, same note", Rating = 4 });
            // A new repository instance discovers the original row from its process evidence.
            _recovered = await new SessionRepository(database).FindOpenMonitoredAsync(1, [_process])
                ?? throw new InvalidOperationException("The source sitting was not recovered.");
            _sessionId = checkpoint.Id;
        }
        else if (kind is "paging" or "paging-disposal" or "week-race" or "reload")
        {
            for (var id = 1; id <= 2; id++)
                seed.Execute("""
                    INSERT INTO sessions(id,ownership_id,started_at,duration_s,detection_method)
                    VALUES(@id,@ownership,@started,@duration,'manual');
                    """, new { id, ownership = kind == "reload" ? id : 1, started = now.AddMinutes(1 - id),
                        duration = kind == "paging" ? (int?)(id * 60) : null });
        }
        else if (kind is not ("empty" or "account"))
        {
            seed.Execute("""
                INSERT INTO sessions(id,ownership_id,started_at,ended_at,duration_s,detection_method)
                VALUES(@id,@owner,@at,@end,@duration,'manual');
                """, new { id = _sessionId, owner, at, end = kind == "hierarchy" ? at.AddSeconds(7800) : (DateTime?)null,
                    duration = kind == "hierarchy" ? (int?)7800 : null });
            var note = kind switch
            {
                "journal-vm" => "First route.", "details" => "Looking for the key.",
                "editor-existing" => "Original note", "fullscreen-existing" => "Old note", _ => null,
            };
            if (note is not null)
                await new SessionRepository(database).SetNoteAsync(new SessionNote
                {
                    SessionId = _sessionId, Note = note, Rating = kind == "journal-vm" ? 2 : kind == "details" ? 3 : 4,
                });
        }
        if (kind == "hierarchy") await journal.SetPromptEnabledAsync(true);
        await publisher.PublishAsync(default);
        return new(Kind, owner, owner, _sessionId, at, Environment.ProcessId,
            kind is "paging" or "paging-disposal" or "week-race" or "reload" ? [1, 2] : [_sessionId]);
    }

    public async Task CompleteRecoveryAsync()
    {
        if (_recovered is null || _process is null) throw new InvalidOperationException("No recovered source sitting.");
        await new SessionRepository(database).SaveMonitoredAsync(_recovered with { EndedAt = _recovered.StartedAt.AddMinutes(10) }, [_process]);
        await publisher.PublishAsync(default);
    }
    public async Task ReloadNoteAsync()
    {
        await new SessionRepository(database).SetNoteAsync(new SessionNote { SessionId = 2, Note = "A newly saved note" });
        await publisher.PublishAsync(default);
    }
    public Task PublishAsync() => publisher.PublishAsync(default);
    public void PublishEnded() => changes.Publish("session.ended", $"sessions/{_sessionId}");
    public object Snapshot()
    {
        using var read = database.Open();
        return new
        {
            Kind, ProcessId = Environment.ProcessId, Controls = controls.Snapshot(),
            Sessions = read.Query<Session>("SELECT id,ownership_id AS OwnershipId,started_at AS StartedAt,ended_at AS EndedAt,duration_s AS DurationSeconds,detection_method AS DetectionMethod,monitor_key AS MonitorKey FROM sessions ORDER BY id").ToArray(),
            Notes = read.Query<SessionNote>("SELECT session_id AS SessionId,note,rating FROM session_notes ORDER BY session_id").ToArray(),
            SessionCount = read.ExecuteScalar<int>("SELECT COUNT(*) FROM sessions"),
            NoteCount = read.ExecuteScalar<int>("SELECT COUNT(*) FROM session_notes"),
            RecoveryAliases = read.ExecuteScalar<int>("SELECT COUNT(*) FROM monitored_session_keys"),
            ProcessObservations = read.ExecuteScalar<int>("SELECT COUNT(*) FROM monitored_session_processes"),
        };
    }
}

internal sealed record JournalActivitySeedResult(string Kind, long WorkId, long OwnershipId, long SessionId,
    DateTime StartedAt, int ProcessId, IReadOnlyList<long> SessionIds);
internal sealed record JournalActivityArm(string Operation, string Target = "any", string Behavior = "hold", bool IgnoreCancellation = true);
internal sealed class JournalActivityControls
{
    private readonly Lock _gate = new();
    private readonly List<Plan> _plans = [];
    private readonly ConcurrentQueue<Plan> _consumed = new();
    private readonly ConcurrentQueue<Call> _calls = new();
    private int _sequence;
    internal IReadOnlyList<Call> Calls => _calls.ToArray();
    public object Arm(JournalActivityArm input)
    {
        if (input.Operation is not ("activity" or "account" or "write" or "delete")
            || input.Target is not ("any" or "first" or "append")
            || input.Behavior is not ("hold" or "fail" or "hold-fail")) throw new ArgumentException("Unknown journal fixture gate.");
        var plan = new Plan(Guid.NewGuid().ToString("N"), input);
        lock (_gate) _plans.Add(plan);
        return new { GateId = plan.Id };
    }
    public void Release(string? id = null)
    {
        foreach (var plan in _consumed.Where(plan => id is null || plan.Id == id)) plan.Released.TrySetResult();
    }
    public async Task<Call> BeforeAsync(string operation, CancellationToken ct, CancellationToken disconnected = default,
        DateTime? from = null, DateTime? until = null, ActivitySection? section = null, ActivityCursor? after = null,
        int? pageSize = null, long? sessionId = null)
    {
        Plan? plan;
        lock (_gate)
        {
            plan = _plans.FirstOrDefault(value => value.Input.Operation == operation &&
                (value.Input.Target == "any" || (value.Input.Target == "append") == (after is not null)));
            if (plan is not null) _plans.Remove(plan);
        }
        var call = new Call(Interlocked.Increment(ref _sequence), operation, ct, disconnected, from, until, section, after,
            pageSize, sessionId, plan?.Id, plan?.Input.IgnoreCancellation ?? false);
        // State polling may release a gate as soon as its call becomes visible.
        if (plan is not null) _consumed.Enqueue(plan);
        _calls.Enqueue(call);
        if (plan is null) return call;
        if (plan.Input.Behavior != "fail") await plan.Released.Task;
        if (plan.Input.Behavior is "fail" or "hold-fail")
        {
            call.Failed = true; call.Completed = true;
            throw new IOException("Source journal/activity fixture refusal.");
        }
        return call;
    }
    public object Snapshot() => new
    {
        Calls = _calls.Select(call => new
        {
            call.Id, call.Operation, call.From, call.Until, call.Section, call.After, call.PageSize, call.SessionId,
            call.GateId, call.IgnoreCancellation, call.Completed, call.Failed, call.ProcessId, call.ThreadId,
            CancellationRequested = call.Cancellation.IsCancellationRequested,
            RequestAborted = call.Disconnected.IsCancellationRequested,
        }).ToArray(),
    };
    internal sealed class Plan(string id, JournalActivityArm input)
    {
        public string Id { get; } = id;
        public JournalActivityArm Input { get; } = input;
        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    internal sealed class Call(int id, string operation, CancellationToken cancellation, CancellationToken disconnected,
        DateTime? from, DateTime? until, ActivitySection? section, ActivityCursor? after, int? pageSize, long? sessionId,
        string? gateId, bool ignoreCancellation)
    {
        public int Id { get; } = id;
        public string Operation { get; } = operation;
        public CancellationToken Cancellation { get; } = cancellation;
        public CancellationToken Disconnected { get; } = disconnected;
        public DateTime? From { get; } = from;
        public DateTime? Until { get; } = until;
        public ActivitySection? Section { get; } = section;
        public ActivityCursor? After { get; } = after;
        public int? PageSize { get; } = pageSize;
        public long? SessionId { get; } = sessionId;
        public string? GateId { get; } = gateId;
        public bool IgnoreCancellation { get; } = ignoreCancellation;
        public bool Completed { get; set; }
        public bool Failed { get; set; }
        public int ProcessId { get; } = Environment.ProcessId;
        public int ThreadId { get; } = Environment.CurrentManagedThreadId;
    }
}

internal sealed class JournalActivityReader(ISqliteConnectionFactory database, JournalActivityControls controls,
    Func<JournalActivityFixture> fixture) : IActivityRepository
{
    private int _calls;
    public async Task<ActivityPage> GetPageAsync(IReadOnlyCollection<long> ownershipIds, DateTime fromUtc, DateTime untilUtc,
        ActivitySection section, ActivityCursor? after = null, int pageSize = 50, CancellationToken ct = default)
    {
        var ordinal = Interlocked.Increment(ref _calls);
        var call = await controls.BeforeAsync("activity", ct, from: fromUtc, until: untilUtc, section: section, after: after, pageSize: pageSize);
        try
        {
            var kind = fixture().Kind;
            if (kind is "paging" or "paging-disposal" or "week-race")
            {
                var owner = ownershipIds.Order().First();
                var id = kind == "week-race" ? ordinal : after is null ? 1 : 2;
                var at = kind == "paging" ? fromUtc.AddDays(1) : fromUtc;
                var note = await new SessionRepository(database).GetNoteAsync(id, CancellationToken.None);
                return new([new(owner, "steam", at, new Session
                {
                    Id = id, OwnershipId = owner, StartedAt = at, DurationSeconds = kind == "paging" ? id * 60 : null,
                    DetectionMethod = "manual",
                }, note, null)], kind != "week-race" && after is null ? new(at, id) : null);
            }
            return await new ActivityRepository(database).GetPageAsync(ownershipIds, fromUtc, untilUtc, section, after,
                pageSize, call.IgnoreCancellation ? CancellationToken.None : ct);
        }
        finally { call.Completed = true; }
    }
}
internal sealed class JournalAccountReader(ISqliteConnectionFactory database, JournalActivityControls controls,
    Func<JournalActivityFixture> fixture) : IAccountStatsRepository
{
    public async Task<AccountStats> GetAsync(string source, CancellationToken ct = default)
    {
        var call = await controls.BeforeAsync("account", ct);
        try
        {
            return fixture().Kind == "account" ? new AccountStats { Source = source, TransactionCount = 1 }
                : await new AccountStatsRepository(database).GetAsync(source, call.IgnoreCancellation ? CancellationToken.None : ct);
        }
        finally { call.Completed = true; }
    }
}
internal sealed class JournalSessionRepository(ISqliteConnectionFactory database, JournalActivityControls controls,
    IHttpContextAccessor context) : ISessionRepository
{
    private readonly SessionRepository _inner = new(database);
    public async Task SetNoteAsync(SessionNote note, CancellationToken ct = default)
    {
        var call = await controls.BeforeAsync("write", ct, context.HttpContext?.RequestAborted ?? default, sessionId: note.SessionId);
        try { await _inner.SetNoteAsync(note, ct); }
        finally { call.Completed = true; }
    }
    public async Task DeleteNoteAsync(long id, CancellationToken ct = default)
    {
        var call = await controls.BeforeAsync("delete", ct, context.HttpContext?.RequestAborted ?? default, sessionId: id);
        try { await _inner.DeleteNoteAsync(id, ct); }
        finally { call.Completed = true; }
    }
    public Task<long> InsertAsync(Session session, CancellationToken ct = default) => _inner.InsertAsync(session, ct);
    public Task<Session?> GetAsync(long id, CancellationToken ct = default) => _inner.GetAsync(id, ct);
    public Task<IReadOnlyList<Session>> GetByOwnershipAsync(long id, CancellationToken ct = default) => _inner.GetByOwnershipAsync(id, ct);
    public Task<SessionNote?> GetNoteAsync(long id, CancellationToken ct = default) => _inner.GetNoteAsync(id, ct);
    public Task<IReadOnlyList<SessionJournalEntry>> GetJournalEntriesByOwnershipAsync(long id, CancellationToken ct = default) => _inner.GetJournalEntriesByOwnershipAsync(id, ct);
    public Task<Session?> FindOpenMonitoredAsync(long id, IReadOnlyList<MonitoredProcessIdentity> processes, CancellationToken ct = default) => _inner.FindOpenMonitoredAsync(id, processes, ct);
    public Task<Session> SaveMonitoredAsync(Session session, IReadOnlyList<MonitoredProcessIdentity> processes, CancellationToken ct = default) => _inner.SaveMonitoredAsync(session, processes, ct);
}
