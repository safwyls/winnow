using System.Reflection;
using DbUp;
using DbUp.Sqlite.Helpers;

namespace Hoard.Data;

/// <summary>
/// Applies embedded migrations (src/Hoard.Data/Migrations/*.sql) with DbUp
/// on startup. The journal table (SchemaVersions) lives in the same database.
/// Idempotent: already-applied scripts are skipped.
/// </summary>
public sealed class DatabaseInitializer
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public DatabaseInitializer(ISqliteConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public void Initialize()
    {
        var directory = Path.GetDirectoryName(_connectionFactory.DatabasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Run DbUp over a factory-opened connection so WAL + foreign_keys
        // are in force during migration, exactly as at runtime.
        using var connection = _connectionFactory.Open();

        var upgrader = DeployChanges.To
            .SqliteDatabase(new SharedConnection(connection))
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetExecutingAssembly(),
                name => name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .WithTransactionPerScript()
            .LogToTrace()
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
        {
            throw new InvalidOperationException(
                $"Database migration failed on script '{result.ErrorScript?.Name}'.",
                result.Error);
        }
    }
}
