using Dapper;
using Winnow.App.Services;
using Winnow.Core.Lifecycle;
using Winnow.Data;
using Winnow.Data.Repositories;

namespace Winnow.Electron.Fixtures;

/// <summary>Frozen visibility fixtures using production queries, mutations and event publication.</summary>
internal sealed class LibraryVisibilityFixture(ISqliteConnectionFactory database, LibraryChangePublisher publisher, TimeProvider clock)
{
    public async Task SeedAsync(string kind)
    {
        using (var connection = database.Open())
        {
            if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM works") != 0)
                throw new InvalidOperationException("A visibility fixture can only be seeded once.");
            switch (kind)
            {
                case "grouped":
                    connection.Execute("""
                        INSERT INTO works(id,name,sort_name) VALUES(1,'Grouped game','Grouped game'),(2,'Unselected game','Unselected game');
                        INSERT INTO releases(id,work_id,name,platform) VALUES(1,1,'Steam copy','windows'),(2,1,'Epic copy','windows'),(3,2,'Unselected game','windows');
                        INSERT INTO ownerships(id,release_id,store,installed) VALUES(1,1,'steam',0),(2,2,'epic',0),(3,3,'gog',0);
                        """);
                    break;
                case "bulk-read":
                case "bulk-derelict":
                    connection.Execute("""
                        INSERT INTO works(id,name,sort_name) VALUES(1,'Alpha','Alpha'),(2,'Bravo','Bravo'),(3,'Charlie','Charlie');
                        INSERT INTO releases(id,work_id,name,platform) SELECT id,id,name,'windows' FROM works;
                        INSERT INTO ownerships(id,release_id,store,installed) SELECT id,id,'steam',0 FROM releases;
                        INSERT INTO play_records(ownership_id,playtime_minutes,last_played_at,source,observed_at) VALUES
                            (1,600,'2024-01-01','steam_local','2026-09-01'),(2,600,'2024-01-01','steam_local','2026-09-01'),(3,600,'2024-01-01','steam_local','2026-09-01');
                        INSERT INTO update_events(release_id,kind,occurred_at) VALUES
                            (1,'build_push','2026-06-01'),(1,'announcement','2026-06-02'),
                            (2,'build_push','2026-06-01'),(2,'announcement','2026-06-02'),
                            (3,'build_push','2026-06-01'),(3,'announcement','2026-06-02');
                        """);
                    break;
                case "settings":
                    connection.Execute("""
                        INSERT INTO works(id,name,sort_name) VALUES(1,'Kept','Kept'),(2,'Hidden','Hidden');
                        INSERT INTO releases(id,work_id,name,platform) SELECT id,id,name,'windows' FROM works;
                        INSERT INTO ownerships(id,release_id,store,installed) SELECT id,id,'steam',0 FROM releases;
                        INSERT INTO play_records(ownership_id,playtime_minutes,source,observed_at) SELECT id,0,'steam_localconfig','2026-09-04' FROM ownerships;
                        """);
                    break;
                case "explicit":
                    connection.Execute("""
                        INSERT INTO works(id,name,sort_name) VALUES(1,'Anything','Anything');
                        INSERT INTO releases(id,work_id,name,platform) VALUES(1,1,'Anything','windows');
                        INSERT INTO ownerships(id,release_id,store,installed) VALUES(1,1,'steam',0);
                        INSERT INTO play_records(ownership_id,playtime_minutes,source,observed_at) VALUES(1,0,'steam_localconfig','2026-09-04');
                        """);
                    break;
                case "hide-selection":
                    connection.Execute("""
                        INSERT INTO works(id,name,sort_name) VALUES(1,'One','One'),(2,'Two','Two'),(3,'Three','Three');
                        INSERT INTO releases(id,work_id,name,platform) SELECT id,id,name,'windows' FROM works;
                        INSERT INTO ownerships(id,release_id,store,installed) SELECT id,id,'steam',0 FROM releases;
                        """);
                    break;
                case "scroll":
                    connection.Execute("""
                        WITH RECURSIVE seq(id) AS(SELECT 1 UNION ALL SELECT id+1 FROM seq WHERE id<100)
                        INSERT INTO works(id,name,sort_name) SELECT id,'Game '||id,'Game '||id FROM seq;
                        INSERT INTO releases(id,work_id,name,platform) SELECT id,id,name,'windows' FROM works;
                        INSERT INTO ownerships(id,release_id,store,installed) SELECT id,id,'steam',0 FROM releases;
                        INSERT INTO external_ids(release_id,provider,provider_id) SELECT id,'steam',CAST(id AS TEXT) FROM releases;
                        INSERT INTO lists(id,name,is_smart) VALUES(1,'Try next',0);
                        INSERT INTO list_items(list_id,release_id,position) VALUES(1,100,0),(1,1,1);
                        """);
                    break;
                default: throw new ArgumentException("Unknown visibility fixture kind.");
            }
        }
        if (kind is "grouped" or "bulk-derelict")
            foreach (var releaseId in new long[] { 1, 2, 3 })
                await new LifecycleRepository(database).AppendAsync(Shutdown(releaseId, new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)));
        await publisher.PublishAsync(CancellationToken.None);
    }

    public void FailExemptions()
    {
        using var connection = database.Open();
        connection.Execute("""
            CREATE TRIGGER refuse_exemption BEFORE INSERT ON derelict_exemptions
            BEGIN SELECT RAISE(FAIL,'simulated storage failure'); END;
            """);
    }

    public async Task LaterEvidenceAsync()
    {
        foreach (var releaseId in new long[] { 1, 2 })
            await new LifecycleRepository(database).AppendAsync(Shutdown(releaseId, clock.GetUtcNow().UtcDateTime));
        await publisher.PublishAsync(CancellationToken.None);
    }

    public object Snapshot()
    {
        using var connection = database.Open();
        return new
        {
            Exemptions = connection.Query<long>("SELECT release_id FROM derelict_exemptions ORDER BY release_id"),
            Hidden = connection.Query<long>("SELECT work_id FROM hidden_games WHERE unhidden_at IS NULL ORDER BY work_id"),
            Explicit = connection.QuerySingleOrDefault<string>("SELECT value FROM settings WHERE key='library.show_explicit_content'"),
            LifecycleCounts = connection.Query<(long ReleaseId, int Count)>("SELECT release_id,COUNT(*) FROM lifecycle_observations GROUP BY release_id ORDER BY release_id")
                .ToDictionary(row => row.ReleaseId.ToString(), row => row.Count),
            OwnershipCount = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM ownerships"),
        };
    }

    private static LifecycleObservation Shutdown(long releaseId, DateTime observedAt) => new()
    {
        ReleaseId = releaseId,
        Source = "official",
        ObservedAt = observedAt,
        Signals = new() { OfficialShutdownAt = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
    };
}

// Keep the frozen September fixtures within the real classifier's evidence freshness window.
internal sealed class VisibilityFixtureClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
}
