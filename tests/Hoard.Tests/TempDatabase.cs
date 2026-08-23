using Hoard.Data;
using Microsoft.Data.Sqlite;

namespace Hoard.Tests;

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
            $"hoard-test-{Guid.NewGuid():N}.db");
        Factory = new SqliteConnectionFactory(DatabasePath);
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
        // Pooled connections keep the file locked on Windows.
        SqliteConnection.ClearAllPools();

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
