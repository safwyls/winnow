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
    private static readonly Lazy<string> CurrentSchemaTemplate = new(
        CreateCurrentSchemaTemplate,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public TempDatabase(bool migrate = true)
    {
        DatabasePath = Path.Combine(
            Path.GetTempPath(),
            $"winnow-test-{Guid.NewGuid():N}.db");

        // Most tests need an isolated database at the current schema, not a
        // fresh exercise of all migrations. Replaying every transaction-per-
        // script migration for every xUnit case turns theory rows and simple
        // repository assertions into thousands of durable-write workloads on
        // hosted Windows disks. The template is migrated and checkpointed once
        // per test process; each test still owns a separate file and separate
        // connections. Tests that assert migration from an empty database pass
        // migrate: false and call Initializer themselves.
        if (migrate)
        {
            File.Copy(CurrentSchemaTemplate.Value, DatabasePath);
        }

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

    private static string CreateCurrentSchemaTemplate()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"winnow-test-template-{Environment.ProcessId}-{Guid.NewGuid():N}.db");
        var backupDirectory = path + "-backups";
        var factory = new SqliteConnectionFactory(path, pooling: false);
        var initializer = new DatabaseInitializer(factory)
        {
            Backups = DatabaseBackupPolicy.Default with { Directory = backupDirectory },
        };

        initializer.Initialize();

        // A clone must be complete in its main file. WAL is production
        // behavior for each clone, but the template cannot depend on sidecars
        // that File.Copy deliberately does not carry.
        using (var connection = factory.Open())
        using (var checkpoint = connection.CreateCommand())
        {
            checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            checkpoint.ExecuteNonQuery();
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteTemplate(path, backupDirectory);
        return path;
    }

    private static void DeleteTemplate(string path, string backupDirectory)
    {
        try
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var candidate = path + suffix;
                if (File.Exists(candidate))
                {
                    File.Delete(candidate);
                }
            }

            if (Directory.Exists(backupDirectory))
            {
                Directory.Delete(backupDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Process teardown is best effort; the OS temp cleaner owns any
            // file that an exiting native SQLite handle has not released yet.
        }
        catch (UnauthorizedAccessException)
        {
            // Same best-effort rule as IOException during process teardown.
        }
    }
}
