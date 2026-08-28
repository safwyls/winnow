using Winnow.Data;
using Microsoft.Data.Sqlite;

namespace Winnow.Tests;

/// <summary>
/// A migrated, temp-FILE SQLite database (not :memory:), so WAL and DbUp
/// behave exactly as in production. Deleted on dispose, WAL sidecars
/// included.
/// </summary>
public sealed class TempDatabase : IDisposable
{
    public TempDatabase(bool migrate = true)
    {
        DatabasePath = Path.Combine(
            Path.GetTempPath(),
            $"winnow-test-{Guid.NewGuid():N}.db");
        // Unpooled: see the factory's `pooling` parameter. xUnit runs test
        // classes in parallel, and clearing the process-wide pool on dispose
        // reached into databases other classes were mid-query against — which
        // showed up as roughly one bucket-query test failing per five full-suite
        // runs, a different test each time.
        Factory = new SqliteConnectionFactory(DatabasePath, pooling: false);
        Initializer = new DatabaseInitializer(Factory);

        if (migrate)
        {
            Initializer.Initialize();
        }
    }

    public string DatabasePath { get; }

    public SqliteConnectionFactory Factory { get; }

    public DatabaseInitializer Initializer { get; }

    public void Dispose()
    {
        // No ClearAllPools() here on purpose — this factory is unpooled, so
        // disposed connections are already closed, and clearing the global pool
        // would disrupt test classes running in parallel.
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = DatabasePath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
