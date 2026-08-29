using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Ingest.Gog;

/// <summary>
/// A private, disposable copy of Galaxy's client database, opened read-only.
/// Copies <c>.db</c> + <c>-wal</c> + <c>-shm</c> to avoid writing into the
/// store-owned directory. <see cref="Dispose"/> deletes the copy.
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
    /// Copies the database and its write-ahead log into a Winnow-owned temporary
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
            Path.GetTempPath(), "winnow-gog-" + Guid.NewGuid().ToString("N"));

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
