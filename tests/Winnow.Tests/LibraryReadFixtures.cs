using Dapper;
using Microsoft.Data.Sqlite;
using Winnow.Core.Repositories;
using Winnow.Data;

namespace Winnow.Tests;

public static class LibraryReadFixtures
{
    public static void Seed(TempDatabase db, int count)
    {
        using var connection = db.Factory.Open();
        connection.Execute("""
            WITH RECURSIVE seq(id) AS (SELECT 1 UNION ALL SELECT id + 1 FROM seq WHERE id < @count)
            INSERT INTO works (id, name, sort_name) SELECT id, 'Game ' || id, 'Game ' || id FROM seq;
            INSERT INTO releases (id, work_id, name, platform) SELECT id, id, name, 'windows' FROM works;
            INSERT INTO ownerships (id, release_id, store, installed) SELECT id, id, 'steam', 0 FROM releases;
            INSERT INTO external_ids (release_id, provider, provider_id) SELECT id, 'steam', CAST(id AS TEXT) FROM releases;
            INSERT INTO lists (id, name, is_smart) VALUES (1, 'Try next', 0);
            INSERT INTO list_items (list_id, release_id, position) VALUES (1, @count, 0), (1, 1, 1);
            """, new { count });
    }
}

public sealed class LibraryReadTrackingFactory(ISqliteConnectionFactory inner) : ISqliteConnectionFactory
{
    private int _leases;
    public int Leases => Volatile.Read(ref _leases);
    public Action? BeforeLease { get; set; }
    public string DatabasePath => inner.DatabasePath;
    public string ConnectionString => inner.ConnectionString;
    public SqliteConnection Open() => inner.Open();
    public IUnitOfWork Begin() => inner.Begin();
    public DbLease Lease()
    {
        BeforeLease?.Invoke();
        Interlocked.Increment(ref _leases);
        return inner.Lease();
    }
}
