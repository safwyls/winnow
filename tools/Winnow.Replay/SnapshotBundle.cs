using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Winnow.Data;

namespace Winnow.Replay;

public sealed record SnapshotManifest(int Version, DateTime AsOfUtc, string DatabaseSha256);

/// <summary>A captured database has a known observation boundary; a copied file's modification time does not.</summary>
public static class SnapshotBundle
{
    public const string DatabaseName = "library.db";
    public const string ManifestName = "capture.json";
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static SnapshotManifest Capture(string sourceDatabase, string destination, TimeProvider? clock = null)
    {
        destination = Path.GetFullPath(destination);
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException("The capture destination already exists.");
        var staging = destination + ".pending-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        try
        {
            using var source = OpenReadOnly(sourceDatabase);
            // A read establishes SQLite's snapshot before its instant is recorded. Backup reads
            // that same transaction, including committed WAL pages, while later writes stay out.
            using var read = source.BeginTransaction(deferred: true);
            source.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_schema;", transaction: read);
            var asOfUtc = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
            var database = Path.Combine(staging, DatabaseName);
            using (var target = new SqliteConnection(new SqliteConnectionStringBuilder
                   { DataSource = database, Pooling = false }.ToString()))
            {
                target.Open();
                source.BackupDatabase(target);
                target.Execute("PRAGMA journal_mode = DELETE;");
            }
            var manifest = new SnapshotManifest(1, asOfUtc, Hash(database));
            File.WriteAllText(Path.Combine(staging, ManifestName), JsonSerializer.Serialize(manifest, JsonOptions));
            Directory.Move(staging, destination);
            return manifest;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    internal static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = Path.GetFullPath(path), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    internal static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static ReplayDatabase Open(string directory, DateTime? asOfUtc = null)
    {
        var manifest = JsonSerializer.Deserialize<SnapshotManifest>(
            File.ReadAllText(Path.Combine(directory, ManifestName)), JsonOptions)
            ?? throw new InvalidDataException("The capture manifest is missing.");
        if (manifest.Version != 1 || manifest.AsOfUtc.Kind != DateTimeKind.Utc)
            throw new InvalidDataException("The capture manifest is incompatible.");
        if (asOfUtc is { } requested && (requested.Kind != DateTimeKind.Utc || requested != manifest.AsOfUtc))
            throw new InvalidDataException("Replay requires the recorded capture instant. Mutable library fields cannot be backdated or advanced.");
        var working = Path.Combine(Path.GetTempPath(), "winnow-replay-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(working);
        try
        {
            var database = Path.Combine(working, DatabaseName);
            File.Copy(Path.Combine(directory, DatabaseName), database);
            // Check the bytes actually used, not a source file that could change between hash and copy.
            if (!StringComparer.OrdinalIgnoreCase.Equals(Hash(database), manifest.DatabaseSha256))
                throw new InvalidDataException("The captured database does not match its manifest.");
            using (var connection = OpenReadOnly(database))
            {
                var expected = typeof(DatabaseInitializer).Assembly.GetManifestResourceNames()
                    .Where(name => name.StartsWith("Winnow.Data.Migrations.", StringComparison.Ordinal)
                        && name.EndsWith(".sql", StringComparison.Ordinal)).Order(StringComparer.Ordinal);
                var actual = connection.Query<string>("SELECT ScriptName FROM SchemaVersions;").Order(StringComparer.Ordinal);
                if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
                    throw new InvalidDataException("Replay requires the current complete schema; captured databases are never migrated.");
            }
            return new ReplayDatabase(working, manifest);
        }
        catch
        {
            Directory.Delete(working, recursive: true);
            throw;
        }
    }
}

/// <summary>Repositories may change connection pragmas only in this disposable private working copy.</summary>
public sealed class ReplayDatabase : IDisposable
{
    private readonly string _directory;
    internal ReplayDatabase(string directory, SnapshotManifest manifest)
    {
        _directory = directory;
        Manifest = manifest;
        Factory = new SqliteConnectionFactory(Path.Combine(directory, SnapshotBundle.DatabaseName), pooling: false);
    }

    public SnapshotManifest Manifest { get; }
    public SqliteConnectionFactory Factory { get; }
    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
