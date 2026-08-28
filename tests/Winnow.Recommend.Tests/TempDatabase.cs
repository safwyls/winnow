using Winnow.Data;

namespace Winnow.Recommend.Tests;

/// <summary>
/// A migrated, temp-FILE SQLite database (not :memory:), so WAL and DbUp
/// behave exactly as in production. Deleted on dispose, WAL sidecars
/// included. A copy of <c>tests/Winnow.Tests/TempDatabase.cs</c> — duplicated
/// rather than referenced because a test project referencing another test
/// project drags its entire fixture tree along; keep the two in step.
/// </summary>
public sealed class TempDatabase : IDisposable
{
    public TempDatabase()
    {
        DatabasePath = Path.Combine(
            Path.GetTempPath(),
            $"winnow-recommend-test-{Guid.NewGuid():N}.db");
        // Unpooled, same reason as the original: xUnit runs test classes in
        // parallel, and clearing the process-wide pool on dispose reaches into
        // databases other classes are mid-query against.
        Factory = new SqliteConnectionFactory(DatabasePath, pooling: false);
        new DatabaseInitializer(Factory).Initialize();
    }

    public string DatabasePath { get; }

    public SqliteConnectionFactory Factory { get; }

    public void Dispose()
    {
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
