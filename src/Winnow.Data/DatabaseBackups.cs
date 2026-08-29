using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Winnow.Data;

/// <summary>
/// How much of a safety net <see cref="DatabaseInitializer"/> puts under a
/// schema change, and what it does when it cannot put one there at all.
/// </summary>
public sealed record DatabaseBackupPolicy
{
    /// <summary>The policy the app ships with: three copies kept, and no
    /// migration at all if a copy cannot be written.</summary>
    public static DatabaseBackupPolicy Default { get; } = new();

    /// <summary>
    /// Where the copies go. Null means <c>backups</c> beside the database, which
    /// keeps them inside the one directory the user already knows about and the
    /// one the rename shim moves as a whole.
    /// </summary>
    public string? Directory { get; init; }

    /// <summary>
    /// How many copies survive a prune. Three: enough to step back past a bad
    /// migration and the launch that followed it, few enough that a large
    /// library does not quietly cost four times its size on disk.
    /// </summary>
    public int Keep { get; init; } = 3;

    /// <summary>
    /// Whether a failed backup stops the migration.
    ///
    /// <para>True, and it should stay true. The database is the only durable
    /// thing Winnow owns — no server, no account, nothing to re-sync a play
    /// history or a journal entry from — so "apply an irreversible schema change
    /// to the sole copy, having just failed to copy it" is not a trade the app
    /// gets to make on the user's behalf. A brand-new database is exempt
    /// automatically (there is nothing yet to lose), so the only user this
    /// refusal ever stops is one whose disk is full or whose data directory is
    /// not writable — both of which are worth stopping for and both of which say
    /// so in the exception.</para>
    ///
    /// <para>Set false only where losing the database is genuinely acceptable —
    /// a throwaway fixture — and expect the warning it logs to be read.</para>
    /// </summary>
    public bool Required { get; init; } = true;
}

/// <summary>
/// Pre-upgrade copies of the database, made with SQLite's online backup API.
///
/// <para>Not a file copy. A file copy of a live database races the write-ahead
/// log and can capture a torn page set; <c>BackupDatabase</c> walks the pager
/// under SQLite's own locking and produces a consistent database whether or not
/// the checkpoint before it managed to empty the log.</para>
/// </summary>
public static class DatabaseBackups
{
    /// <summary>What a finished backup is called.</summary>
    public const string Extension = ".bak";

    /// <summary>What sits between the database name and the schema version it holds.</summary>
    public const string VersionMarker = ".pre-";

    /// <summary>The prefix of a backup still being written. Deliberately not
    /// <c>.bak.partial</c>: Windows pattern matching treats a three-character
    /// extension as a prefix, so <c>*.bak</c> would have matched it.</summary>
    private const string PartialMarker = ".partial-";

    /// <summary>Where the copies for this database live.</summary>
    public static string DirectoryFor(string databasePath, DatabaseBackupPolicy policy)
        => policy.Directory
           ?? Path.Combine(
               Path.GetDirectoryName(Path.GetFullPath(databasePath)) ?? ".",
               "backups");

    /// <summary>
    /// Copies the open database to a new file named after the schema version it
    /// still holds, validates the copy, and only then gives it its final name.
    /// Returns the path of the finished backup.
    /// </summary>
    /// <param name="source">The open live database.</param>
    /// <param name="databasePath">The live database's path, which names the copies.</param>
    /// <param name="schemaVersion">The version the copy can be restored to.</param>
    /// <param name="expectedTables">
    /// Every table the live database holds. The copy must hold all of them —
    /// which is a stronger check than "is it a valid Winnow database", and,
    /// unlike that one, does not refuse to back up a database that was already
    /// half-built before this run found it.
    /// </param>
    /// <param name="policy">Where the copies go.</param>
    public static string Create(
        SqliteConnection source,
        string databasePath,
        string schemaVersion,
        IReadOnlyCollection<string> expectedTables,
        DatabaseBackupPolicy policy)
    {
        var directory = DirectoryFor(databasePath, policy);
        Directory.CreateDirectory(directory);

        var final = UniquePath(directory, Path.GetFileName(databasePath), schemaVersion);
        var partial = Path.Combine(
            directory,
            Path.GetFileName(final) + PartialMarker + Guid.NewGuid().ToString("N"));

        try
        {
            using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = partial,
                Pooling = false,
            }.ToString()))
            {
                destination.Open();
                source.BackupDatabase(destination);
            }

            Verify(partial, expectedTables);

            // The rename is what publishes it. Until this line there is no file
            // in the directory that rotation would count or a restore would
            // trust, so an interrupted backup costs a stray .partial- file and
            // nothing else.
            File.Move(partial, final);
            return final;
        }
        catch
        {
            Discard(partial);
            throw;
        }
    }

    /// <summary>
    /// Deletes all but the newest <see cref="DatabaseBackupPolicy.Keep"/> copies.
    /// Called only after the upgraded database has been checked, so a migration
    /// that produced an unreadable database never takes the copies of the
    /// readable one with it.
    /// </summary>
    public static void Prune(string databasePath, DatabaseBackupPolicy policy)
    {
        var directory = DirectoryFor(databasePath, policy);
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var stale in All(databasePath, policy).Skip(Math.Max(policy.Keep, 0)))
        {
            try
            {
                stale.Delete();
            }
            catch (Exception held) when (held is IOException or UnauthorizedAccessException)
            {
                // A backup we could not delete is the harmless kind of failure.
            }
        }
    }

    /// <summary>Every finished backup of this database, newest first.</summary>
    public static IReadOnlyList<FileInfo> All(string databasePath, DatabaseBackupPolicy policy)
    {
        var directory = DirectoryFor(databasePath, policy);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var stem = Path.GetFileName(databasePath) + VersionMarker;

        return new DirectoryInfo(directory)
            .EnumerateFiles(stem + "*" + Extension)
            // Windows matches a three-character extension as a prefix, so the
            // pattern alone would also hand back anything ending .bakXYZ.
            .Where(file =>
                file.Name.StartsWith(stem, StringComparison.Ordinal)
                && file.Name.EndsWith(Extension, StringComparison.Ordinal))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The copy has to be a database in its own right before it is allowed to
    /// look like one — a backup that only fails when it is needed is worse than
    /// no backup, because it is the one that stopped anyone worrying.
    /// </summary>
    private static void Verify(string backupPath, IReadOnlyCollection<string> expectedTables)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = backupPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();

        var integrity = SqliteDatabaseCheck.QuickCheck(connection);
        if (!string.Equals(integrity, "ok", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The backup written to '{backupPath}' failed quick_check: {integrity}");
        }

        var copied = SqliteDatabaseCheck.Tables(connection);
        var missing = expectedTables.Where(table => !copied.Contains(table)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"The backup written to '{backupPath}' is missing {missing.Count} of the "
                + $"live database's tables ({string.Join(", ", missing)}).");
        }
    }

    /// <summary>
    /// <c>winnow.db.pre-0011.20260828T101500123Z.bak</c>. The version says what
    /// a restore gets you; the millisecond stamp keeps two migrations in one
    /// second apart; the counter is there because "essentially impossible" is
    /// not a guarantee to give a file name that overwrites a backup.
    /// </summary>
    private static string UniquePath(string directory, string stem, string schemaVersion)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        var basePath = Path.Combine(directory, $"{stem}{VersionMarker}{schemaVersion}.{stamp}");

        var candidate = basePath + Extension;
        for (var counter = 2; File.Exists(candidate); counter++)
        {
            candidate = $"{basePath}.{counter}{Extension}";
        }

        return candidate;
    }

    private static void Discard(string partial)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try
            {
                File.Delete(partial + suffix);
            }
            catch (Exception held) when (held is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
