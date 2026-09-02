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

        // Pre-upgrade backups are a real behaviour (F42), so tests get them —
        // but into this database's own directory rather than the default
        // `backups` folder beside it, which here is the shared system temp
        // directory. Deleted with the database on dispose.
        BackupDirectory = DatabasePath + "-backups";
        Initializer = new DatabaseInitializer(Factory)
        {
            Backups = DatabaseBackupPolicy.Default with { Directory = BackupDirectory },
        };

        if (migrate)
        {
            Initializer.Initialize();
        }
    }

    public string DatabasePath { get; }

    /// <summary>Where <see cref="Initializer"/> writes its pre-upgrade copies.</summary>
    public string BackupDirectory { get; }

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

        if (Directory.Exists(BackupDirectory))
        {
            Directory.Delete(BackupDirectory, recursive: true);
        }
    }
}
