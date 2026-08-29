using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Winnow.Data;

/// <summary>How much of a database is actually there.</summary>
public enum DatabaseHealth
{
    /// <summary>No file at that path.</summary>
    Missing,

    /// <summary>There is a file, but SQLite will not read it — not a database,
    /// truncated header, locked, or on a volume we cannot open.</summary>
    Unreadable,

    /// <summary><c>PRAGMA quick_check</c> said something other than <c>ok</c>.</summary>
    Corrupt,

    /// <summary>Opens and checks clean, but holds no tables at all. This is what
    /// a database looks like the instant SQLite creates the file and before the
    /// first migration runs — a shape with nothing in it to lose.</summary>
    Empty,

    /// <summary>Opens, checks clean, holds tables — but not the ones a Winnow
    /// database has. A half-built database, or somebody else's.</summary>
    Incomplete,

    /// <summary>Opens, checks clean, and carries the migration journal and the
    /// core tables. Safe to adopt.</summary>
    Healthy,
}

/// <summary>What one look at a database file found.</summary>
/// <param name="Path">The file that was looked at.</param>
/// <param name="Health">How much of a database is there.</param>
/// <param name="Detail">The sentence to put in a log line, if there is one.</param>
public sealed record DatabaseCheck(string Path, DatabaseHealth Health, string? Detail)
{
    /// <summary>Whether this file may be adopted as the user's library.</summary>
    public bool IsUsable => Health is DatabaseHealth.Healthy;

    /// <summary>Whether there is nothing here yet to lose.</summary>
    public bool IsNew => Health is DatabaseHealth.Missing or DatabaseHealth.Empty;
}

/// <summary>
/// Reads a SQLite file and says whether it is a Winnow library. Two callers, one
/// question: <c>WinnowDataLocation</c> asks it before adopting a directory, and
/// <see cref="DatabaseInitializer"/> asks it before and after changing a schema.
///
/// <para>Nothing here runs on the steady-state launch path. The directory checks
/// happen only when two data directories exist or a copy has just been staged,
/// and the initializer's checks happen only when a migration is actually
/// pending — so <c>quick_check</c>, which is O(database), is never a per-launch
/// cost on a library that has nothing to decide.</para>
/// </summary>
public static class SqliteDatabaseCheck
{
    /// <summary>
    /// The tables whose absence means "this is not a Winnow library". DbUp's
    /// journal proves a migration ran; <c>works</c> is 0001's first table and so
    /// exists in every schema version that has ever shipped. Deliberately two
    /// names and not twenty: this is an identity check, not a schema assertion,
    /// and listing the current tables would make every future migration a change
    /// to what counts as a valid old database.
    /// </summary>
    public static readonly string[] RequiredTables = ["SchemaVersions", "works"];

    /// <summary>The version stamp used when a database has no journal at all.</summary>
    public const string NoSchemaVersion = "0000";

    /// <summary>The four-digit prefix DbUp script names carry: <c>0012_...</c>.</summary>
    private static readonly Regex ScriptNumber = new(@"(?<n>\d{4,})_", RegexOptions.CultureInvariant);

    /// <summary>
    /// The first fifteen bytes of every SQLite database ever written. The
    /// sixteenth is a NUL, checked separately rather than escaped into this
    /// literal.
    /// </summary>
    private static readonly byte[] HeaderMagic = "SQLite format 3"u8.ToArray();

    /// <summary>Opens the file and looks. Never creates one that is not there.</summary>
    public static DatabaseCheck Inspect(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            return new DatabaseCheck(databasePath, DatabaseHealth.Missing, "There is no file at this path.");
        }

        if (!LooksLikeDatabase(databasePath, out var notADatabase))
        {
            return new DatabaseCheck(databasePath, DatabaseHealth.Unreadable, notADatabase);
        }

        try
        {
            using var connection = OpenExisting(databasePath);
            return Inspect(connection, databasePath);
        }
        catch (Exception unopenable)
            when (unopenable is SqliteException or IOException or UnauthorizedAccessException
                or InvalidOperationException)
        {
            return new DatabaseCheck(databasePath, DatabaseHealth.Unreadable, unopenable.Message);
        }
    }

    /// <summary>
    /// Looks through a connection somebody else owns — used where the database is
    /// already open and closing it to inspect it would be worse than the check.
    /// </summary>
    public static DatabaseCheck Inspect(SqliteConnection connection, string databasePath)
    {
        try
        {
            var integrity = QuickCheck(connection);
            if (!string.Equals(integrity, "ok", StringComparison.Ordinal))
            {
                return new DatabaseCheck(databasePath, DatabaseHealth.Corrupt, integrity);
            }

            var tables = Tables(connection);
            if (tables.Count == 0)
            {
                return new DatabaseCheck(databasePath, DatabaseHealth.Empty, "The file holds no tables.");
            }

            var missing = RequiredTables.Where(table => !tables.Contains(table)).ToList();
            return missing.Count > 0
                ? new DatabaseCheck(
                    databasePath,
                    DatabaseHealth.Incomplete,
                    $"Missing {string.Join(", ", missing)}.")
                : new DatabaseCheck(databasePath, DatabaseHealth.Healthy, null);
        }
        catch (SqliteException notADatabase)
        {
            // The usual shape of this: a file that is not SQLite at all. The
            // connection opens lazily, so the complaint arrives on the first
            // statement rather than on Open().
            return new DatabaseCheck(databasePath, DatabaseHealth.Unreadable, notADatabase.Message);
        }
    }

    /// <summary><c>PRAGMA quick_check</c>, stopping at the first complaint.</summary>
    public static string QuickCheck(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check(1);";
        return command.ExecuteScalar() as string ?? "quick_check returned no answer.";
    }

    /// <summary>Every user table in the database, sqlite's own excluded.</summary>
    public static HashSet<string> Tables(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name FROM sqlite_master
             WHERE type = 'table' AND name NOT LIKE 'sqlite_%';
            """;

        using var reader = command.ExecuteReader();
        var names = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    /// <summary>
    /// The highest migration number in the journal, four digits, or
    /// <see cref="NoSchemaVersion"/> if nothing has been applied. Used to name a
    /// backup after the schema it can be restored to.
    /// </summary>
    public static string SchemaVersion(SqliteConnection connection)
    {
        using var exists = connection.CreateCommand();
        exists.CommandText =
            "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'SchemaVersions';";
        if (exists.ExecuteScalar() is null)
        {
            return NoSchemaVersion;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ScriptName FROM SchemaVersions ORDER BY ScriptName DESC LIMIT 1;";
        if (command.ExecuteScalar() is not string script)
        {
            return NoSchemaVersion;
        }

        var number = ScriptNumber.Match(script);
        return number.Success ? number.Groups["n"].Value : NoSchemaVersion;
    }

    /// <summary>
    /// Folds the write-ahead log back into the database and closes cleanly, so
    /// that the sidecars go away and the library is one file again.
    ///
    /// <para>This is what makes renaming a database safe. <c>hoard.db</c>,
    /// <c>hoard.db-wal</c> and <c>hoard.db-shm</c> cannot be renamed as one
    /// atomic act on any filesystem we run on, and either order of the three
    /// leaves a crash window where a database sits beside a write-ahead log that
    /// is not its own — the exact corruption this method exists to make
    /// impossible. After a truncating checkpoint there is nothing in the log to
    /// lose and, in the normal case, no log left to rename.</para>
    ///
    /// <para>Best effort by design: it returns false rather than throwing for a
    /// file that is not a database, because the caller's fallback (rename the
    /// whole set, all or none) is still correct — just no longer free.</para>
    /// </summary>
    public static bool TryCheckpoint(string databasePath)
    {
        if (!File.Exists(databasePath) || !LooksLikeDatabase(databasePath, out _))
        {
            return false;
        }

        try
        {
            using var connection = Open(databasePath, SqliteOpenMode.ReadWrite);
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            command.ExecuteNonQuery();
            return true;
        }
        catch (Exception refused)
            when (refused is SqliteException or IOException or UnauthorizedAccessException
                or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Sixteen bytes read, and nothing opened. The guard exists because SQLite
    /// is not a passive reader: handed a file that is not a database it will
    /// still take the sidecar names beside it, decide the <c>-wal</c> it finds
    /// there has an invalid header, and unlink the pair on close. That is a
    /// perfectly reasonable thing for a database engine to do and a completely
    /// unreasonable thing for an inspection to do, so nothing that fails this
    /// check is ever opened.
    ///
    /// <para>A zero-byte file passes: SQLite creates one exactly that way and it
    /// is a legal empty database, which <see cref="Inspect(string)"/> then
    /// reports as <see cref="DatabaseHealth.Empty"/>.</para>
    /// </summary>
    private static bool LooksLikeDatabase(string databasePath, out string? why)
    {
        try
        {
            using var stream = new FileStream(
                databasePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            if (stream.Length == 0)
            {
                why = null;
                return true;
            }

            Span<byte> header = stackalloc byte[16];
            if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
            {
                why = $"The file is {stream.Length} bytes long — too short to be a database.";
                return false;
            }

            if (!header[..HeaderMagic.Length].SequenceEqual(HeaderMagic) || header[15] != 0)
            {
                why = "The file does not begin with SQLite's header, so it is not a database.";
                return false;
            }

            why = null;
            return true;
        }
        catch (Exception unreadable)
            when (unreadable is IOException or UnauthorizedAccessException)
        {
            why = unreadable.Message;
            return false;
        }
    }

    /// <summary>
    /// Read-write if we can, read-only if we cannot. Never
    /// <c>ReadWriteCreate</c>: the whole point of these checks is to find out
    /// what is already on disk, and a mode that conjures an empty database would
    /// answer the question by destroying it.
    /// </summary>
    private static SqliteConnection OpenExisting(string databasePath)
    {
        try
        {
            return Open(databasePath, SqliteOpenMode.ReadWrite);
        }
        catch (SqliteException)
        {
            // A read-only volume, or a file somebody else holds for writing.
            // Worth one more try: reading is all this class ever needs.
            return Open(databasePath, SqliteOpenMode.ReadOnly);
        }
    }

    private static SqliteConnection Open(string databasePath, SqliteOpenMode mode)
    {
        // Unpooled: these connections are opened against files that are about to
        // be renamed, moved or deleted, and a pooled handle outliving the check
        // would hold the very file the caller is trying to move.
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = mode,
            Pooling = false,
        }.ToString());

        try
        {
            connection.Open();
        }
        catch
        {
            // A connection that failed to open still owns whatever handle it
            // got as far as taking, and every caller of this class is about to
            // rename or delete the file it is holding.
            connection.Dispose();
            throw;
        }

        return connection;
    }
}
