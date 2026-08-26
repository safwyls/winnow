using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoard.Ingest.Gog;

/// <summary>
/// A private, disposable copy of Galaxy's client database, opened read-only.
/// <b>Nothing else in this assembly is allowed to open
/// <c>galaxy-2.0.db</c>.</b>
///
/// <para><b>Why the copy is mandatory, not tidy</b>
/// (docs/spikes/epic-gog-local-files.md section 11). The database is in WAL mode
/// and Galaxy holds it open. It was measured directly that <b>opening a WAL
/// database with <c>mode=ro</c> CREATES <c>-wal</c> and <c>-shm</c> files next to
/// it</b>: a directory containing only <c>galaxy-2.0.db</c> contained all three
/// after a single read-only <c>SELECT</c>. <c>mode=ro</c> restricts writes to the
/// <i>database</i>, not to the <i>directory</i>. Pointing it at
/// <c>%PROGRAMDATA%\GOG.com\Galaxy\storage</c> therefore writes into a
/// store-owned directory, which §4.1 forbids absolutely. Copy first, always.</para>
///
/// <para><b>Why all three files are copied.</b> Three strategies were measured
/// on the live 11 MB database with a 10 MB write-ahead log:</para>
/// <list type="table">
/// <item><term>copy <c>.db</c> + <c>-wal</c> + <c>-shm</c>, open the copy <c>?mode=ro</c></term>
///   <description>sees the latest data — the only correct option</description></item>
/// <item><term>copy <c>.db</c> only</term>
///   <description><b>silently stale</b></description></item>
/// <item><term>copy <c>.db</c> + <c>-wal</c>, open <c>?mode=ro&amp;immutable=1</c></term>
///   <description><b>silently stale</b> — <c>immutable=1</c> produced byte-identical
///   results to discarding the write-ahead log entirely. It is the intuitive
///   choice for "don't disturb it" and it is the wrong one: it returns data from
///   an arbitrary past checkpoint with no error and no warning</description></item>
/// </list>
///
/// <para>The main database is copied <i>first</i> deliberately: the WAL only grows
/// within a checkpoint cycle, so a later-copied WAL is a superset of what the
/// earlier-copied main file needs. A missing <c>-wal</c>/<c>-shm</c> is not an
/// error (SQLite recreates them beside the copy); missing WAL <i>data</i> is a
/// silent correctness bug, so they are copied whenever they exist.</para>
///
/// <para><see cref="Dispose"/> deletes the copy. Connection pooling is switched
/// off in the connection string so the handle really closes and the file really
/// goes.</para>
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

    /// <summary>Absolute path of the copy. Hoard owns this file.</summary>
    public string DatabasePath { get; }

    /// <summary>
    /// Copies the database and its write-ahead log into a Hoard-owned temporary
    /// directory and verifies the copy. Returns null — never throws — when the
    /// source is absent or the snapshot could not be taken.
    ///
    /// <para><c>PRAGMA quick_check</c> runs on the copy (0.02 s on the live 11 MB
    /// database). A result other than <c>ok</c> means the snapshot was torn by a
    /// concurrent checkpoint: the copy is discarded and taken again once, and if
    /// that also fails the scan reports rather than importing partial data.</para>
    /// </summary>
    /// <param name="sourceDatabasePath">Path of the live <c>galaxy-2.0.db</c>.</param>
    /// <param name="logger">Optional logger.</param>
    public static GalaxyDatabaseSnapshot? Take(string sourceDatabasePath, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(sourceDatabasePath);
        logger ??= NullLogger.Instance;

        if (!File.Exists(sourceDatabasePath))
        {
            logger.LogDebug("GOG Galaxy database {Path} does not exist", sourceDatabasePath);
            return null;
        }

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var snapshot = TryCopy(sourceDatabasePath, logger);
            if (snapshot is null)
            {
                return null;
            }

            if (snapshot.QuickCheckPasses(logger))
            {
                return snapshot;
            }

            logger.LogWarning(
                "GOG Galaxy database snapshot failed quick_check (attempt {Attempt} of 2); "
                + "a checkpoint probably tore the copy", attempt);
            snapshot.Dispose();
        }

        logger.LogWarning(
            "Could not take a consistent snapshot of {Path}; skipping the Galaxy database this pass",
            sourceDatabasePath);
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

    private static GalaxyDatabaseSnapshot? TryCopy(string sourceDatabasePath, ILogger logger)
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "hoard-gog-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(directory);
            var destination = Path.Combine(directory, GogPaths.ClientDatabaseFileName);

            // Main file FIRST: a WAL copied afterwards is a superset of what this
            // copy of the main file needs, never a subset.
            File.Copy(sourceDatabasePath, destination, overwrite: true);

            foreach (var suffix in new[] { "-wal", "-shm" })
            {
                var sidecar = sourceDatabasePath + suffix;
                if (File.Exists(sidecar))
                {
                    File.Copy(sidecar, destination + suffix, overwrite: true);
                }
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
