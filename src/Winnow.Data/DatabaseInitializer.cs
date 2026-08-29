using System.Data;
using System.Reflection;
using DbUp;
using DbUp.Sqlite.Helpers;

namespace Winnow.Data;

/// <summary>
/// Applies embedded migrations (src/Winnow.Data/Migrations/*.sql) with DbUp
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

        RenameLegacyJournalEntries(connection);

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

    /// <summary>The journal prefix written by every build before the rename to Winnow.</summary>
    private const string LegacyScriptPrefix = "Hoard.Data.Migrations.";

    /// <summary>What <see cref="WithScriptsEmbeddedInAssembly"/> calls the same scripts now.</summary>
    private const string ScriptPrefix = "Winnow.Data.Migrations.";

    /// <summary>
    /// Re-points DbUp journal entries from the old <c>Hoard.Data</c> namespace
    /// to <c>Winnow.Data</c>. Without this, renamed scripts replay against a
    /// populated database. Idempotent and safe on a fresh database.
    /// </summary>
    private static void RenameLegacyJournalEntries(IDbConnection connection)
    {
        using var exists = connection.CreateCommand();
        exists.CommandText =
            "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'SchemaVersions';";
        if (exists.ExecuteScalar() is null)
        {
            return;
        }

        // Qualified to avoid binding ScriptName to the inner table.
        var suffix = $"substr(SchemaVersions.ScriptName, {LegacyScriptPrefix.Length + 1})";

        using var rename = connection.CreateCommand();
        rename.CommandText = $"""
            DELETE FROM SchemaVersions
             WHERE ScriptName LIKE '{LegacyScriptPrefix}%'
               AND EXISTS (
                     SELECT 1
                       FROM SchemaVersions AS already
                      WHERE already.ScriptName = '{ScriptPrefix}' || {suffix});

            UPDATE SchemaVersions
               SET ScriptName =
                   '{ScriptPrefix}' || substr(ScriptName, {LegacyScriptPrefix.Length + 1})
             WHERE ScriptName LIKE '{LegacyScriptPrefix}%';
            """;
        rename.ExecuteNonQuery();
    }
}
