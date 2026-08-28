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
    /// Re-points the journal at the renamed assembly.
    ///
    /// <para><b>This is not cosmetic, and skipping it breaks every existing
    /// install.</b> DbUp identifies an applied script by its full manifest
    /// resource name, which begins with the assembly's root namespace — so
    /// renaming <c>Hoard.Data</c> to <c>Winnow.Data</c> turns
    /// <c>Hoard.Data.Migrations.0001_initial.sql</c> into a name the journal has
    /// never seen. DbUp would then replay all ten shipped migrations against a
    /// populated database, and <c>0001</c> would die on
    /// <c>CREATE TABLE works</c> before the window ever opened.</para>
    ///
    /// <para>Done here rather than in the app's data-directory migration on
    /// purpose: this hazard belongs to the database, not to where it is stored,
    /// so it has to run for a database opened from a custom path, from a test,
    /// or from a directory that was moved by hand.</para>
    ///
    /// <para>Idempotent, and safe on a fresh database — the journal table does
    /// not exist yet on a first run, and the UPDATE matches nothing on every run
    /// after the first.</para>
    ///
    /// <para>The journal is a SET of script names, so a database that somehow
    /// holds both spellings of one script is holding the same fact twice. The
    /// legacy row is dropped rather than renamed onto its own twin: renaming it
    /// would leave two identical rows, and leaving it would mean this method is
    /// not idempotent, because the next run would find the legacy prefix
    /// again.</para>
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

        // Qualified, because inside the correlated subquery a bare ScriptName
        // would bind to the INNER table and the guard would compare every row
        // with itself.
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
