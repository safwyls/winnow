using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Ingest.Gog;

/// <summary>
/// A private, disposable copy of Galaxy's client database, opened read-only.
/// Copies guarded <c>.db</c> + <c>-wal</c> files without opening SQLite in the
/// store-owned directory. SQLite rebuilds SHM only beside the private copy.
/// <see cref="Dispose"/> deletes the copy.
/// </summary>
public sealed class GalaxyDatabaseSnapshot : IDisposable
{
    private readonly string _directory;
    private readonly ILogger _logger;
    private bool _disposed;

    private GalaxyDatabaseSnapshot(string directory, string databasePath, ILogger logger)
    {
        _directory = directory;
        DatabasePath = databasePath;
        _logger = logger;
    }

    /// <summary>Absolute path of the copy. Winnow owns this file.</summary>
    public string DatabasePath { get; }

    /// <summary>
    /// On Windows, copies while read-only file handles deny writes and deletion.
    /// A writer already holding the source makes the read fail conservatively;
    /// the caller can retry on its next scan. Other platforms decline live files
    /// because FileShare cannot exclude native SQLite writers there. Structural
    /// validation runs only after a coherent copy has been established.
    /// </summary>
    /// <param name="sourceDatabasePath">Path of the live <c>galaxy-2.0.db</c>.</param>
    /// <param name="logger">Optional logger.</param>
    public static GalaxyDatabaseSnapshot? Take(string sourceDatabasePath, ILogger? logger = null)
        => TakeGuarded(sourceDatabasePath, logger, afterMainCopied: null);

    /// <summary>
    /// Copies a caller-owned, immutable database and its matching WAL on any
    /// platform. The caller must ensure neither file can change during the call.
    /// Never use this entry point for a launcher's live files.
    /// </summary>
    public static GalaxyDatabaseSnapshot? CopyImmutable(string sourceDatabasePath, ILogger? logger = null)
        => TakeCore(sourceDatabasePath, logger ?? NullLogger.Instance, afterMainCopied: null);

    internal static GalaxyDatabaseSnapshot? TakeGuarded(string sourceDatabasePath, ILogger? logger,
        Action? afterMainCopied)
    {
        ArgumentNullException.ThrowIfNull(sourceDatabasePath);
        logger ??= NullLogger.Instance;
        if (!OperatingSystem.IsWindows())
        {
            logger.LogWarning("Live GOG Galaxy snapshots require Windows file sharing guards; skipping this pass");
            return null;
        }
        return TakeCore(sourceDatabasePath, logger, afterMainCopied);
    }

    private static GalaxyDatabaseSnapshot? TakeCore(string sourceDatabasePath, ILogger logger,
        Action? afterMainCopied)
    {
        ArgumentNullException.ThrowIfNull(sourceDatabasePath);
        var snapshot = TryCopy(sourceDatabasePath, logger, afterMainCopied);
        if (snapshot is null) return null;
        if (snapshot.QuickCheckPasses(logger)) return snapshot;
        logger.LogWarning("GOG Galaxy snapshot failed structural validation; skipping this pass");
        snapshot.Dispose();
        return null;
    }

    /// <summary>
    /// Opens the copy read-only. The caller disposes the connection; the snapshot
    /// itself must outlive it.
    /// </summary>
    public SqliteConnection OpenReadOnly()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            // Pooling would keep the file handle alive past Close() and the
            // temp directory would survive Dispose on Windows.
            Pooling = false,
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Galaxy's schema version, for a log line. Galaxy migrates this schema; a
    /// jump from the verified <c>40</c> is early warning that the ownership query
    /// needs re-verifying.
    /// </summary>
    public long ReadUserVersion()
    {
        using var connection = OpenReadOnly();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Deletes the copy and its temporary directory.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not delete Galaxy snapshot directory {Path}", _directory);
        }
    }

    private static GalaxyDatabaseSnapshot? TryCopy(string sourceDatabasePath, ILogger logger,
        Action? afterMainCopied)
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "winnow-gog-" + Guid.NewGuid().ToString("N"));

        try
        {
            // Windows checks sharing against existing handles as well as future
            // opens. Holding the main handle excludes native SQLite writers and
            // checkpoints before we inspect or copy either generation-bearing file.
            using var main = OpenGuard(sourceDatabasePath);
            using var wal = OpenOptionalGuard(sourceDatabasePath + "-wal");
            using var journal = OpenOptionalGuard(sourceDatabasePath + "-journal");
            if (journal is not null)
                throw new IOException("Galaxy has a rollback journal; recovery may be required.");

            Directory.CreateDirectory(directory);
            var destination = Path.Combine(directory, GogPaths.ClientDatabaseFileName);
            Copy(main, destination);
            afterMainCopied?.Invoke();
            if (wal is not null)
            {
                Copy(wal, destination + "-wal");
            }

            return new GalaxyDatabaseSnapshot(directory, destination, logger);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            logger.LogWarning(ex, "Could not copy the GOG Galaxy database from {Path}", sourceDatabasePath);
            TryDelete(directory);
            return null;
        }
    }

    private static FileStream OpenGuard(string path)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    private static FileStream? OpenOptionalGuard(string path)
    {
        try { return OpenGuard(path); }
        catch (FileNotFoundException) { return null; }
    }

    private static void Copy(Stream source, string destination)
    {
        using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        source.CopyTo(target);
    }

    private bool QuickCheckPasses(ILogger logger)
    {
        try
        {
            using var connection = OpenReadOnly();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA quick_check;";
            var result = command.ExecuteScalar() as string;
            return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not open the GOG Galaxy database snapshot at {Path}", DatabasePath);
            return false;
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover temp directory is not worth failing a scan.
        }
    }
}
