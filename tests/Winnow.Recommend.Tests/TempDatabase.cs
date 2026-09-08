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
    private static readonly Lazy<string> CurrentSchemaTemplate = new(
        CreateCurrentSchemaTemplate,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public TempDatabase()
    {
        DatabasePath = Path.Combine(
            Path.GetTempPath(),
            $"winnow-recommend-test-{Guid.NewGuid():N}.db");
        File.Copy(CurrentSchemaTemplate.Value, DatabasePath);
        // Unpooled, same reason as the original: xUnit runs test classes in
        // parallel, and clearing the process-wide pool on dispose reaches into
        // databases other classes are mid-query against.
        Factory = new SqliteConnectionFactory(DatabasePath, pooling: false);
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

    private static string CreateCurrentSchemaTemplate()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"winnow-recommend-template-{Environment.ProcessId}-{Guid.NewGuid():N}.db");
        var factory = new SqliteConnectionFactory(path, pooling: false);
        new DatabaseInitializer(factory).Initialize();

        using (var connection = factory.Open())
        using (var checkpoint = connection.CreateCommand())
        {
            checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            checkpoint.ExecuteNonQuery();
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
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
            }
            catch (IOException)
            {
                // Best effort during process teardown.
            }
            catch (UnauthorizedAccessException)
            {
                // Best effort during process teardown.
            }
        };

        return path;
    }
}
