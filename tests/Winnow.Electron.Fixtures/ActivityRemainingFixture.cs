using System.Collections.Concurrent;
using System.Diagnostics;
using Dapper;
using Microsoft.Data.Sqlite;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Data;
using Winnow.Data.Repositories;

namespace Winnow.Electron.Fixtures;

internal sealed class ActivityRemainingFixture(ISqliteConnectionFactory database, LibraryChangePublisher publisher)
{
    internal static readonly DateTime SourceNow = new(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc);

    public async Task<object> SeedAsync(string kind)
    {
        if (kind is not ("ack" or "ack-correlated" or "sparse-linked" or "range" or "large"))
            throw new ArgumentException("Unknown activity fixture.");
        using (var check = database.Open())
            if (check.ExecuteScalar<int>("SELECT COUNT(*) FROM works") != 0)
                throw new InvalidOperationException("Activity fixture requires an empty library.");
        if (kind == "large") SeedLarge(database);
        else await SeedSmallAsync(database, kind);
        await publisher.PublishAsync(default);
        return new { WorkId = 1, SourceNow, Counts = Counts(database) };
    }

    internal static async Task SeedSmallAsync(ISqliteConnectionFactory database, string kind)
    {
        var acknowledgement = kind is "ack" or "ack-correlated";
        var work = await new WorkRepository(database).InsertAsync(new Work { Name = "Activity fixture" });
        var release = await new ReleaseRepository(database).InsertAsync(new Release
            { WorkId = work, Name = "Activity fixture", Platform = "windows" });
        var ownership = await new OwnershipRepository(database).InsertAsync(new Ownership
        {
            ReleaseId = release, Store = "steam",
            AcquiredAt = kind == "range" ? SourceNow.AddYears(-6) : acknowledgement ? SourceNow.AddYears(-2) : null,
        });
        await new PlayRecordRepository(database).InsertAsync(new PlayRecord
        {
            OwnershipId = ownership, PlaytimeMinutes = kind == "range" ? 960 : 600,
            LastPlayedAt = kind == "range" ? SourceNow.AddDays(-15) : acknowledgement ? SourceNow.AddDays(-20) : null,
            Source = "steam", ObservedAt = SourceNow,
        });
        if (acknowledgement)
            await new UpdateEventRepository(database).InsertAsync(new UpdateEvent
            {
                ReleaseId = release, Kind = UpdateEventKinds.Announcement,
                OccurredAt = SourceNow.AddDays(-2), Title = "Exploration update",
            });
        if (kind == "ack-correlated")
            await new UpdateEventRepository(database).InsertAsync(new UpdateEvent
            {
                ReleaseId = release, Kind = UpdateEventKinds.BuildPush,
                OccurredAt = SourceNow.AddDays(-2), Title = "Corroborating build",
            });
        if (kind != "range") return;
        foreach (var (month, minutes) in new[] { (3, 360), (4, 600), (5, 780) })
            await new PlaytimeSnapshotRepository(database).InsertAsync(new PlaytimeSnapshot
            {
                OwnershipId = ownership, PlaytimeMinutes = minutes,
                ObservedAt = new DateTime(2022, month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1).AddSeconds(-1),
            });
        foreach (var (days, seconds) in new[] { (-20, 3600), (-15, 7200) })
            await new SessionRepository(database).InsertAsync(new Session
            {
                OwnershipId = ownership, StartedAt = SourceNow.AddDays(days),
                EndedAt = SourceNow.AddDays(days).AddSeconds(seconds),
                // The source's in-memory "process" tag is stored using SQLite's process_watch vocabulary.
                DurationSeconds = seconds, DetectionMethod = "process_watch",
            });
    }

    internal static void SeedLarge(ISqliteConnectionFactory database)
    {
        using var connection = database.Open();
        // Exact LibraryReadFixtures(2000) and LargeHistoryResponsivenessTests SQL.
        connection.Execute("""
            WITH RECURSIVE seq(id) AS (SELECT 1 UNION ALL SELECT id+1 FROM seq WHERE id<2000)
            INSERT INTO works(id,name,sort_name) SELECT id,'Game '||id,'Game '||id FROM seq;
            INSERT INTO releases(id,work_id,name,platform) SELECT id,id,name,'windows' FROM works;
            INSERT INTO ownerships(id,release_id,store,installed) SELECT id,id,'steam',0 FROM releases;
            INSERT INTO external_ids(release_id,provider,provider_id) SELECT id,'steam',CAST(id AS TEXT) FROM releases;
            INSERT INTO lists(id,name,is_smart) VALUES(1,'Try next',0);
            INSERT INTO list_items(list_id,release_id,position) VALUES(1,2000,0),(1,1,1);
            WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<60)
            INSERT INTO sessions(ownership_id,started_at,ended_at,duration_s,detection_method)
            SELECT o.id,datetime('now','-'||(seq.n*10)||' days'),datetime('now','-'||(seq.n*10)||' days','+1 hour'),3600,'process_watch'
            FROM ownerships o CROSS JOIN seq;
            WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<5000)
            INSERT INTO sessions(ownership_id,started_at,ended_at,duration_s,detection_method)
            SELECT 1,datetime('now','-'||n||' days'),datetime('now','-'||n||' days','+1 hour'),3600,'process_watch' FROM seq;
            INSERT INTO session_notes(session_id,note,rating) SELECT id,'A preserved journal entry',4 FROM sessions;
            WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<14600)
            INSERT INTO playtime_snapshots(ownership_id,playtime_minutes,observed_at)
            SELECT 1,n*10,datetime('now','-'||((14600-n)*6)||' hours') FROM seq;
            INSERT INTO update_events(release_id,kind,occurred_at,title)
            SELECT id,'announcement',datetime('now','-30 days'),'Older update' FROM releases;
            WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<40000)
            INSERT INTO account_transactions(source,transaction_type_raw,occurred_at,kind,refunded,total_cents,currency_symbol,item_names_json,item_count,captured_at)
            SELECT 'steam','Purchase',datetime('now','-'||n||' hours'),'purchase',0,1000,'$','["Game"]',1,datetime('now') FROM seq;
            """, commandTimeout: 60);
    }

    internal static Dictionary<string, int> Counts(ISqliteConnectionFactory database)
    {
        using var read = database.Open();
        return new[] { "works", "releases", "ownerships", "sessions", "session_notes", "playtime_snapshots",
            "update_events", "account_transactions" }.ToDictionary(table => table,
            table => read.ExecuteScalar<int>("SELECT COUNT(*) FROM " + table));
    }

    public object Snapshot() => new
    {
        ProcessId = Environment.ProcessId, Counts = Counts(database),
        Measurements = ((ActivityReadTrackingFactory)database).Completed,
    };
}

// Matches the source's elapsed guard at repository-lease boundaries. The native
// renderer measures its own input heartbeat and proves it has a different PID.
internal sealed class ActivityReadTrackingFactory(ISqliteConnectionFactory inner) : ISqliteConnectionFactory
{
    private Measurement? _active;
    private readonly ConcurrentQueue<ActivityReadMeasurement> _completed = new();
    public ActivityReadMeasurement[] Completed => _completed.ToArray();
    public string DatabasePath => inner.DatabasePath;
    public string ConnectionString => inner.ConnectionString;
    public SqliteConnection Open() => inner.Open();
    public IUnitOfWork Begin() => inner.Begin();
    public void ReleasePooledConnections() => inner.ReleasePooledConnections();
    public DbLease Lease()
    {
        Volatile.Read(ref _active)?.RecordLease();
        return inner.Lease();
    }
    public void Start(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (Interlocked.CompareExchange(ref _active, new Measurement(name), null) is not null)
            throw new InvalidOperationException("An activity measurement is already active.");
    }
    public ActivityReadMeasurement End()
    {
        var measurement = Interlocked.Exchange(ref _active, null)
            ?? throw new InvalidOperationException("No activity measurement is active.");
        var result = measurement.Finish();
        _completed.Enqueue(result);
        return result;
    }
    private sealed class Measurement(string name)
    {
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private readonly ConcurrentDictionary<int, byte> _processIds = new();
        private int _leases;
        private int _exceeded;
        public void RecordLease()
        {
            _processIds.TryAdd(Environment.ProcessId, 0);
            Interlocked.Increment(ref _leases);
            if (_watch.Elapsed <= TimeSpan.FromSeconds(10)) return;
            Interlocked.Exchange(ref _exceeded, 1);
            throw new TimeoutException("Synthetic measurement exceeded its ten-second read budget.");
        }
        public ActivityReadMeasurement Finish()
        {
            _watch.Stop();
            return new(name, _watch.Elapsed.TotalMilliseconds, Volatile.Read(ref _leases),
                Volatile.Read(ref _exceeded) != 0, _processIds.Keys.Order().ToArray());
        }
    }
}

internal sealed record ActivityReadMeasurement(string Name, double ElapsedMs, int RepositoryLeases,
    bool Exceeded, IReadOnlyList<int> LeaseProcessIds);
