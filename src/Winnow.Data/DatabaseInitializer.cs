using System.Data;
using System.Diagnostics;
using System.Reflection;
using DbUp;
using DbUp.Engine;
using DbUp.Sqlite.Helpers;
using Microsoft.Data.Sqlite;

namespace Winnow.Data;

/// <summary>
/// Applies embedded migrations (src/Winnow.Data/Migrations/*.sql) with DbUp
/// on startup. The journal table (SchemaVersions) lives in the same database.
/// Idempotent: already-applied scripts are skipped.
///
/// <para>A schema change is the one moment this app rewrites the user's only
/// durable asset in a way no later run can undo — transaction-per-script stops a
/// half-applied script, not a script that applies perfectly and means the wrong
/// thing. So when, and only when, the schema version is about to move, this
/// class checkpoints the log, checks the database, copies it, and prunes the old
/// copies afterwards rather than before. See <see cref="DatabaseBackupPolicy"/>
/// for what happens when the copy cannot be written.</para>
///
/// <para>A launch with nothing pending — every launch after the first, for most
/// of a release's life — costs one journal read and does none of that.</para>
/// </summary>
public sealed class DatabaseInitializer
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public DatabaseInitializer(ISqliteConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    /// <summary>
    /// The safety net under a schema change. An init-only property rather than a
    /// constructor parameter so that the DI registration in the composition root
    /// stays a bare <c>AddSingleton&lt;DatabaseInitializer&gt;()</c>.
    /// </summary>
    public DatabaseBackupPolicy Backups { get; init; } = DatabaseBackupPolicy.Default;

    /// <summary>
    /// The backup this run wrote, or null if it wrote none — because nothing was
    /// pending, because the database was brand new, or because a non-default
    /// policy allowed the migration to proceed without one.
    /// </summary>
    public string? LastBackupPath { get; private set; }

    public void Initialize()
    {
        LastBackupPath = null;

        var directory = Path.GetDirectoryName(_connectionFactory.DatabasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Run DbUp over a factory-opened connection so WAL + foreign_keys
        // are in force during migration, exactly as at runtime.
        //
        // Every step up to and including the pending count is a read of a
        // database this class did not write, so all of them are wrapped: a
        // database damaged badly enough refuses at `PRAGMA journal_mode` rather
        // than at quick_check, and "SQLite Error 11" is not a sentence anybody
        // can act on. The guarantee is that a database SQLite cannot read is
        // never migrated, wherever it happens to say so.
        using var connection = Reading(_connectionFactory.Open);

        var upgrader = Reading(() =>
        {
            RenameLegacyJournalEntries(connection);

            return DeployChanges.To
                .SqliteDatabase(new SharedConnection(connection))
                .WithScriptsEmbeddedInAssembly(
                    Assembly.GetExecutingAssembly(),
                    name => name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                .WithTransactionPerScript()
                .LogToTrace()
                .Build();
        });

        // Asked before the upgrade rather than inferred after it: the backup has
        // to exist BEFORE the schema moves, and this is the only way to know the
        // schema is about to move. Reads the journal and the embedded resource
        // list; touches no page of user data.
        var pending = Reading(upgrader.GetScriptsToExecute);
        if (pending.Count > 0)
        {
            // Wrapped too: its own reads run against the same unverified
            // database, and its deliberate refusals are not SqliteExceptions so
            // they pass through untouched.
            Reading(() => Protect(connection, pending));
        }

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
        {
            throw new InvalidOperationException(
                $"Database migration failed on script '{result.ErrorScript?.Name}'.",
                result.Error);
        }

        if (pending.Count == 0)
        {
            return;
        }

        // Opened fresh rather than reusing the connection DbUp just finished
        // with: this is the check that decides whether the previous copies of
        // the database are allowed to be deleted, and it should not depend on
        // assumptions about what the upgrade engine left the connection in.
        var upgraded = SqliteDatabaseCheck.Inspect(_connectionFactory.DatabasePath);
        if (!upgraded.IsUsable)
        {
            throw new InvalidOperationException(
                $"The migration reported success, but '{_connectionFactory.DatabasePath}' does not "
                + $"read back as a Winnow database afterwards ({upgraded.Health}: {upgraded.Detail}). "
                + $"No backup has been pruned. {Restore(LastBackupPath)}");
        }

        DatabaseBackups.Prune(_connectionFactory.DatabasePath, Backups);
    }

    /// <summary>
    /// Runs one of the reads that happen before the schema is allowed to move,
    /// turning SQLite's refusal into the same sentence the quick_check gate
    /// gives — and, like that gate, never letting the upgrade proceed past it.
    /// </summary>
    private void Reading(Action read)
        => Reading<object?>(() =>
        {
            read();
            return null;
        });

    /// <inheritdoc cref="Reading(Action)"/>
    private T Reading<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (SqliteException unreadable)
        {
            throw new InvalidOperationException(
                $"Refusing to migrate '{_connectionFactory.DatabasePath}': the database cannot be "
                + $"read ({unreadable.Message}). Nothing was changed. Restore from a copy in "
                + $"'{DatabaseBackups.DirectoryFor(_connectionFactory.DatabasePath, Backups)}' and "
                + "start again.",
                unreadable);
        }
    }

    /// <summary>
    /// Everything that has to be true, and everything that has to exist, before
    /// a pending script is allowed to run.
    /// </summary>
    private void Protect(SqliteConnection connection, IReadOnlyCollection<SqlScript> pending)
    {
        var path = _connectionFactory.DatabasePath;

        // A database with no tables at all is this launch's own creation —
        // ISqliteConnectionFactory.Open() made the file seconds ago. There is
        // nothing in it to protect, and refusing to migrate it because a backup
        // directory could not be created would mean a first run that cannot
        // start on a full disk without saying why.
        var tables = SqliteDatabaseCheck.Tables(connection);
        if (tables.Count == 0)
        {
            Trace.TraceInformation(
                $"Winnow: creating the database at '{path}' — {pending.Count} migration(s) to apply, "
                + "and no backup taken because there is nothing in it yet to lose.");
            return;
        }

        // Fold the write-ahead log in first, so the copy about to be taken is a
        // whole library in one file rather than one that needs its sidecars to
        // be complete. Best effort: the online backup below is consistent with
        // or without it.
        Checkpoint(connection, path);

        var before = SqliteDatabaseCheck.Inspect(connection, path);
        if (before.Health is DatabaseHealth.Corrupt or DatabaseHealth.Unreadable)
        {
            throw new InvalidOperationException(
                $"Refusing to migrate '{path}': the database does not check out before the "
                + $"upgrade ({before.Health}: {before.Detail}). Applying {pending.Count} schema "
                + "change(s) to a database SQLite already cannot read would turn a recoverable "
                + "file into an unrecoverable one. Restore from a copy in "
                + $"'{DatabaseBackups.DirectoryFor(path, Backups)}' before starting again.");
        }

        // Deliberately NOT gated on before.IsUsable. A database that is readable
        // but half-built (Incomplete — an interrupted first migration) is exactly
        // the one that most needs the pending scripts, and refusing to migrate it
        // would strand it forever.
        var version = SqliteDatabaseCheck.SchemaVersion(connection);

        try
        {
            LastBackupPath = DatabaseBackups.Create(connection, path, version, tables, Backups);
            Trace.TraceInformation(
                $"Winnow: schema {version} backed up to '{LastBackupPath}' before applying "
                + $"{pending.Count} migration(s).");
        }
        catch (Exception failed)
            when (failed is IOException or UnauthorizedAccessException or SqliteException
                or InvalidOperationException)
        {
            if (Backups.Required)
            {
                throw new InvalidOperationException(
                    $"Refusing to apply {pending.Count} pending migration(s) to '{path}': the "
                    + "pre-upgrade backup could not be written to "
                    + $"'{DatabaseBackups.DirectoryFor(path, Backups)}' ({failed.Message}). This "
                    + "database is the only copy of your library, so it is not migrated without "
                    + "one. Free space or fix permissions on that directory and start again.",
                    failed);
            }

            // The deliberate, logged choice the policy exists to make visible.
            Trace.TraceWarning(
                $"Winnow: applying {pending.Count} migration(s) to '{path}' WITHOUT a backup — "
                + $"the copy could not be written ({failed.Message}) and the configured policy "
                + "does not require one. If this migration goes wrong the library is gone.");
        }
    }

    private static void Checkpoint(SqliteConnection connection, string path)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            command.ExecuteNonQuery();
        }
        catch (SqliteException busy)
        {
            Trace.TraceWarning(
                $"Winnow: could not checkpoint the write-ahead log of '{path}' before backing it "
                + $"up ({busy.Message}). The backup is still consistent — SQLite's online backup "
                + "reads through the log — so this is a note, not a failure.");
        }
    }

    private static string Restore(string? backupPath)
        => backupPath is null
            ? "No pre-upgrade copy was taken on this run."
            : $"The pre-upgrade copy is at '{backupPath}': close Winnow, move the damaged "
              + "database aside, and rename that file into its place.";

    /// <summary>The journal prefix written by every build before the rename to Winnow.</summary>
    private const string LegacyScriptPrefix = "Hoard.Data.Migrations.";

    /// <summary>What <see cref="WithScriptsEmbeddedInAssembly"/> calls the same scripts now.</summary>
    private const string ScriptPrefix = "Winnow.Data.Migrations.";

    /// <summary>
    /// Re-points DbUp journal entries from the old <c>Hoard.Data</c> namespace
    /// to <c>Winnow.Data</c>. Without this, renamed scripts replay against a
    /// populated database. Idempotent and safe on a fresh database.
    ///
    /// <para>Runs BEFORE the pending-script count is taken, and must keep doing
    /// so: a journal still spelled the old way makes every shipped migration
    /// look pending, which would take a backup and then replay 0001 into a
    /// populated database.</para>
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
