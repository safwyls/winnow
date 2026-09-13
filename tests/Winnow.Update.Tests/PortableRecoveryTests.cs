using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Winnow.Update;
using Xunit;

namespace Winnow.Update.Tests;

public sealed class PortableRecoveryTests
{
    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("folder/../../escape")]
    [InlineData("C:/escape")]
    [InlineData("folder\\..\\escape")]
    public void RejectsZipTraversal(string name)
    {
        using var f = new Fixture();
        f.Zip(extra: name);
        Assert.Throws<IOException>(() => f.Stage());
        Assert.Equal("old", File.ReadAllText(f.Executable));
        Assert.False(File.Exists(Path.Combine(f.Root, "escape")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsArchiveLinks(bool tar)
    {
        using var f = new Fixture();
        if (tar) f.Tar("link", link: true); else f.Zip(link: true);
        Assert.Throws<IOException>(() => f.Stage());
        Assert.Equal("old", File.ReadAllText(f.Executable));
    }

    [Fact]
    public void RejectsTarTraversal()
    {
        using var f = new Fixture(); f.Tar("../escape");
        Assert.Throws<IOException>(() => f.Stage());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArchiveCaseDistinctNamesFollowPlatformSemantics(bool tar)
    {
        using var f = new Fixture();
        var launcher = f.ExecutableName.ToLowerInvariant();
        if (tar) f.ReleaseTar(launcher); else f.Zip(extra: launcher);

        if (OperatingSystem.IsWindows())
        {
            Assert.Throws<IOException>(() => f.Stage());
        }
        else
        {
            f.Stage();
            var staged = Path.Combine(f.Workspace, "staged");
            Assert.Equal("new", File.ReadAllText(Path.Combine(staged, f.ExecutableName)));
            Assert.Equal("escape", File.ReadAllText(Path.Combine(staged, launcher)));
        }
        Assert.Equal("old", File.ReadAllText(f.Executable));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsExactDuplicateArchiveNames(bool tar)
    {
        using var f = new Fixture();
        if (tar) f.ReleaseTar(f.ExecutableName); else f.Zip(extra: f.ExecutableName);
        Assert.Throws<IOException>(() => f.Stage());
        Assert.Equal("old", File.ReadAllText(f.Executable));
    }

    [Fact]
    public void RejectsWrongDigestAndMetadataWithoutChangingInstallation()
    {
        using var f = new Fixture(); f.Zip();
        Assert.Throws<IOException>(() => f.Stage(new string('0', 64)));
        f.Zip(version: "9.0.0");
        Assert.Throws<IOException>(() => f.Stage());
        Assert.Equal("old", File.ReadAllText(f.Executable));
    }

    [Fact]
    public async Task RefusesTamperedStageBeforeBackupOrReplacement()
    {
        using var f = new Fixture(); f.Zip(); f.Stage();
        File.WriteAllText(Path.Combine(f.Workspace, "staged", f.ExecutableName), "tampered");
        await Assert.ThrowsAsync<IOException>(() => PortableUpdateEngine.ApplyAsync(f.Journal));
        Assert.Equal("old", File.ReadAllText(f.Executable));
        Assert.False(File.Exists(Path.Combine(f.Workspace, "before.db")));
    }

    [Theory]
    [InlineData(false, 0)] [InlineData(false, 1)] [InlineData(false, 2)] [InlineData(false, 3)]
    [InlineData(true, 0)] [InlineData(true, 1)] [InlineData(true, 2)] [InlineData(true, 3)]
    public void RecoversEveryReplacementBoundaryWithoutChangingData(bool internalData, int boundary)
    {
        using var f = new Fixture(internalData); f.Zip(); f.Stage(); f.CreateDatabase().Dispose();
        f.SetPhase(UpdatePhase.Replacing);
        if (boundary >= 1) Directory.Move(f.Install, Path.Combine(f.Workspace, "previous"));
        if (boundary >= 2) Directory.Move(Path.Combine(f.Workspace, "staged"), f.Install);
        if (boundary >= 3 && internalData) Directory.Move(Path.Combine(f.Workspace, "previous", "data"), f.Data);
        PortableUpdateEngine.Recover(f.Journal);
        Assert.Equal("old", File.ReadAllText(f.Executable));
        Assert.Equal("library", f.ReadDatabase());
        Assert.Equal(UpdatePhase.Restored, PortableUpdateEngine.ReadJournal(f.Journal).Phase);
        PortableUpdateEngine.Recover(f.Journal); // Completed recovery is idempotent.
    }

    [Fact]
    public async Task CapturesCommittedWalAndRequiresExplicitPairedRestoreAfterLaunchFailure()
    {
        using var f = new Fixture(); f.Zip(); f.Stage();
        using var connection = f.CreateDatabase(wal: true);
        Assert.True(File.Exists(f.Database + "-wal"));
        await Assert.ThrowsAnyAsync<Exception>(() => PortableUpdateEngine.ApplyAsync(f.Journal, readyTimeout: TimeSpan.FromMilliseconds(50)));
        connection.Dispose();
        Assert.True(PortableUpdateEngine.ReadJournal(f.Journal).MigrationMayHaveStarted);
        Assert.Equal("library", f.ReadDatabase(Path.Combine(f.Workspace, "before.db")));
        using (var migrated = new SqliteConnection($"Data Source={f.Database};Pooling=False"))
        {
            migrated.Open(); using var command = migrated.CreateCommand(); command.CommandText = "UPDATE items SET value='migrated'"; command.ExecuteNonQuery();
        }
        Assert.Throws<IOException>(() => PortableUpdateEngine.Recover(f.Journal));
        PortableUpdateEngine.Recover(f.Journal, restoreBackup: true);
        Assert.Equal("old", File.ReadAllText(f.Executable));
        Assert.Equal("library", f.ReadDatabase());
        Assert.True(File.Exists(Path.Combine(f.Workspace, "post-upgrade.db")));
        Assert.Equal("migrated", f.ReadDatabase(Path.Combine(f.Workspace, "post-upgrade.db")));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MissingOrCorruptBackupDoesNotChangeEitherVersion(bool corrupt)
    {
        using var f = new Fixture(); f.Zip(); f.Stage(); f.CreateDatabase().Dispose();
        f.SetPhase(UpdatePhase.RecoveryRequired, migrated: true, databaseExisted: true);
        Directory.Move(f.Install, Path.Combine(f.Workspace, "previous"));
        Directory.Move(Path.Combine(f.Workspace, "staged"), f.Install);
        if (corrupt) File.WriteAllText(Path.Combine(f.Workspace, "before.db"), "not sqlite");
        Assert.ThrowsAny<Exception>(() => PortableUpdateEngine.Recover(f.Journal, true));
        Assert.Equal("new", File.ReadAllText(f.Executable));
        Assert.Equal("old", File.ReadAllText(Path.Combine(f.Workspace, "previous", f.ExecutableName)));
        Assert.Equal("library", f.ReadDatabase());
    }

    [Fact]
    public async Task ApplicationLeasePreventsReplacement()
    {
        using var f = new Fixture(); f.Zip(); f.Stage();
        using var lease = PortableUpdateEngine.AcquireApplicationLease(f.Install);
        await Assert.ThrowsAsync<IOException>(() => PortableUpdateEngine.ApplyAsync(f.Journal));
        Assert.Equal("old", File.ReadAllText(f.Executable));
    }

    [Fact]
    public void OperationLeasePreventsConcurrentStaging()
    {
        using var f = new Fixture(); f.Zip();
        using var lease = new FileStream(f.Workspace + ".operation.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Assert.Throws<IOException>(() => f.Stage());
        Assert.Equal("old", File.ReadAllText(f.Executable));
    }

    [Fact]
    public async Task DataLeasePreventsReplacement()
    {
        using var f = new Fixture(); f.Zip(); f.Stage();
        var name = "Local\\Winnow.Data." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(f.Data.ToUpperInvariant())));
        using var mutex = new Mutex(false, name);
        await Assert.ThrowsAsync<IOException>(() => PortableUpdateEngine.ApplyAsync(f.Journal));
        Assert.Equal("old", File.ReadAllText(f.Executable));
    }

    [Fact]
    public async Task BackupIoFailureLeavesDatabaseAndOldBinaries()
    {
        using var f = new Fixture(); f.Zip(); f.Stage(); f.CreateDatabase().Dispose();
        Directory.CreateDirectory(Path.Combine(f.Workspace, "before.db"));
        await Assert.ThrowsAnyAsync<Exception>(() => PortableUpdateEngine.ApplyAsync(f.Journal));
        Assert.Equal("old", File.ReadAllText(f.Executable));
        Assert.Equal("library", f.ReadDatabase());
    }

    [Fact]
    public void PackageManagerBoundaryRejectsStaging()
    {
        using var f = new Fixture(); f.Zip(); File.WriteAllText(Path.Combine(f.Install, "package-managed"), "deb");
        Assert.Throws<IOException>(() => f.Stage());
    }

    [Fact]
    public async Task InsufficientDiskSpaceLeavesOldBinariesAndDatabase()
    {
        using var f = new Fixture(); f.Zip(); f.CreateDatabase().Dispose();
        Assert.Throws<IOException>(() => PortableUpdateEngine.Stage(f.Archive, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f.Archive))), "2.0.0", f.Runtime, f.Install, f.Data, f.ExecutableName, [], availableSpace: _ => 0));
        f.Stage();
        await Assert.ThrowsAsync<IOException>(() => PortableUpdateEngine.ApplyAsync(f.Journal, availableSpace: _ => 0));
        Assert.Equal("old", File.ReadAllText(f.Executable));
        Assert.Equal("library", f.ReadDatabase());
    }

    [Fact]
    public void StartupHandshakeRequiresMatchingPathsAndOrderedMigrationThenReadiness()
    {
        using var f = new Fixture(); f.Zip(); f.Stage();
        Assert.Throws<IOException>(() => PortableUpdateEngine.MarkReady(f.Journal));
        f.SetPhase(UpdatePhase.Installed);
        Assert.Throws<IOException>(() => PortableUpdateEngine.ValidateStartup(f.Journal, f.Install, f.Root));
        PortableUpdateEngine.ValidateStartup(f.Journal, f.Install, f.Data);
        Assert.True(PortableUpdateEngine.ReadJournal(f.Journal).MigrationMayHaveStarted);
        Assert.Equal(UpdatePhase.MigrationStarted, PortableUpdateEngine.ReadJournal(f.Journal).Phase);
        PortableUpdateEngine.MarkReady(f.Journal);
        Assert.Equal(UpdatePhase.Ready, PortableUpdateEngine.ReadJournal(f.Journal).Phase);
    }

    [Fact]
    public void UnixParentPermissionsPreventStagingWithoutChangingInstallation()
    {
        if (OperatingSystem.IsWindows()) return;
        using var f = new Fixture(); f.Zip();
        var mode = File.GetUnixFileMode(f.Root);
        try
        {
            File.SetUnixFileMode(f.Root, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            Assert.Throws<UnauthorizedAccessException>(() => f.Stage());
            Assert.Equal("old", File.ReadAllText(f.Executable));
        }
        finally { File.SetUnixFileMode(f.Root, mode); }
    }

    [Fact]
    public void RestoreBeforeBackupCompletionLeavesOriginalLibraryUntouched()
    {
        using var f = new Fixture(); f.Zip(); f.Stage(); f.CreateDatabase().Dispose();
        Assert.Throws<IOException>(() => PortableUpdateEngine.Recover(f.Journal, restoreBackup: true));
        Assert.Equal("library", f.ReadDatabase());
        Assert.Equal("old", File.ReadAllText(f.Executable));
        Assert.Equal(UpdatePhase.Staged, PortableUpdateEngine.ReadJournal(f.Journal).Phase);
        Assert.False(File.Exists(Path.Combine(f.Workspace, "post-upgrade.db")));
    }

    [Fact]
    public void OrphanedExtractionCanRestageWithoutLosingItsEvidence()
    {
        using var f = new Fixture(); f.Zip();
        var extraction = Path.Combine(f.Workspace, "extracted"); Directory.CreateDirectory(extraction);
        File.WriteAllText(Path.Combine(extraction, "partial"), "interrupted extraction");
        f.Stage();
        Assert.Equal(UpdatePhase.Staged, PortableUpdateEngine.ReadJournal(f.Journal).Phase);
        var retained = Assert.Single(Directory.GetDirectories(f.Root, ".portable.winnow-update.retained-*"));
        Assert.Equal("interrupted extraction", File.ReadAllText(Path.Combine(retained, "extracted", "partial")));
        Assert.Equal("old", File.ReadAllText(f.Executable));
    }

    [Fact]
    public void InterruptedExplicitRestoreRequiresExplicitFlagAgain()
    {
        using var f = new Fixture(); f.Zip(); f.Stage(); f.CreateDatabase().Dispose();
        var journal = PortableUpdateEngine.ReadJournal(f.Journal);
        journal.Phase = UpdatePhase.Restoring;
        journal.DatabaseRestoreStarted = true;
        journal.MigrationMayHaveStarted = false;
        File.WriteAllText(f.Journal, JsonSerializer.Serialize(journal));
        Assert.Throws<IOException>(() => PortableUpdateEngine.Recover(f.Journal));
        Assert.Equal("library", f.ReadDatabase());
        Assert.Equal("old", File.ReadAllText(f.Executable));
        Assert.Equal(UpdatePhase.Restoring, PortableUpdateEngine.ReadJournal(f.Journal).Phase);
    }

    [Fact]
    public async Task ReplacedTransactionRejectsStaleDiscardPrepareAndApply()
    {
        using var f = new Fixture(); f.Zip(); f.Stage();
        var first = PortableUpdateEngine.ReadJournal(f.Journal).TransactionId;
        f.Stage();
        var second = PortableUpdateEngine.ReadJournal(f.Journal).TransactionId;
        Assert.NotEqual(first, second);
        Assert.Throws<IOException>(() => PortableUpdateEngine.DiscardStaged(f.Journal, first));
        Assert.Throws<IOException>(() => PortableUpdateEngine.PrepareHelper(f.Journal, first));
        await Assert.ThrowsAsync<IOException>(() => PortableUpdateEngine.ApplyAsync(f.Journal, expectedTransactionId: first));
        var unchanged = PortableUpdateEngine.ReadJournal(f.Journal);
        Assert.Equal(second, unchanged.TransactionId);
        Assert.Equal(UpdatePhase.Staged, unchanged.Phase);
        Assert.Equal("old", File.ReadAllText(f.Executable));
        Assert.False(Directory.Exists(Path.Combine(f.Workspace, "helper")));
        PortableUpdateEngine.DiscardStaged(f.Journal, second);
        Assert.False(Directory.Exists(f.Workspace));
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "Winnow-update-tests-" + Guid.NewGuid().ToString("N"));
        public string Install => Path.Combine(Root, "portable");
        public string Data { get; }
        public string Database => Path.Combine(Data, "winnow.db");
        public string ExecutableName => OperatingSystem.IsWindows() ? "Winnow.exe" : "Winnow";
        public string Executable => Path.Combine(Install, ExecutableName);
        public string Runtime => OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";
        public string Journal => PortableUpdateEngine.GetJournalPath(Install);
        public string Workspace => Path.GetDirectoryName(Journal)!;
        public string Archive { get; private set; }
        public Fixture(bool internalData = false)
        {
            Directory.CreateDirectory(Install); Data = Path.Combine(internalData ? Install : Root, "data"); Directory.CreateDirectory(Data);
            Archive = Path.Combine(Root, "release.zip"); File.WriteAllText(Executable, "old");
            File.WriteAllText(Path.Combine(Install, "release-info.json"), Manifest("1.0.0"));
        }
        private string Manifest(string version) => JsonSerializer.Serialize(new { version, runtime = Runtime, commit = new string('a', 40) });
        public void Zip(string? extra = null, bool link = false, string version = "2.0.0")
        {
            using var file = File.Create(Archive); using var zip = new ZipArchive(file, ZipArchiveMode.Create);
            void Add(string name, string value) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(value); }
            Add("release-info.json", Manifest(version)); Add(ExecutableName, "new");
            if (extra is not null) Add(extra, "escape");
            if (link) { var entry = zip.CreateEntry("link"); entry.ExternalAttributes = unchecked((int)0xA1FF0000); }
        }
        public void Tar(string name, bool link = false)
        {
            Archive = Path.Combine(Root, "release.tar.gz"); using var file = File.Create(Archive);
            using var gzip = new GZipStream(file, CompressionMode.Compress); using var tar = new TarWriter(gzip);
            var entry = new PaxTarEntry(link ? TarEntryType.SymbolicLink : TarEntryType.RegularFile, name);
            if (link) entry.LinkName = "../escape"; else entry.DataStream = new MemoryStream([1, 2]);
            tar.WriteEntry(entry);
        }
        public void ReleaseTar(string extra)
        {
            Archive = Path.Combine(Root, "release.tar.gz");
            using var file = File.Create(Archive);
            using var gzip = new GZipStream(file, CompressionMode.Compress);
            using var tar = new TarWriter(gzip);
            const string root = "Winnow-2.0.0-linux-x64/";
            tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, root));
            void Add(string name, string value)
            {
                using var data = new MemoryStream(Encoding.UTF8.GetBytes(value));
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, root + name) { DataStream = data });
            }
            Add("release-info.json", Manifest("2.0.0"));
            Add(ExecutableName, "new");
            Add(extra, "escape");
        }
        public string Stage(string? hash = null) => PortableUpdateEngine.Stage(Archive, hash ?? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Archive))), "2.0.0", Runtime, Install, Data, ExecutableName, ["--no-sync"]);
        public void SetPhase(UpdatePhase phase, bool migrated = false, bool databaseExisted = false)
        {
            var journal = PortableUpdateEngine.ReadJournal(Journal); journal.Phase = phase; journal.MigrationMayHaveStarted = migrated; journal.DatabaseExisted = databaseExisted; journal.BackupCompleted = databaseExisted;
            File.WriteAllText(Journal, JsonSerializer.Serialize(journal));
        }
        public SqliteConnection CreateDatabase(bool wal = false)
        {
            var connection = new SqliteConnection($"Data Source={Database};Pooling=False"); connection.Open();
            using var cmd = connection.CreateCommand(); cmd.CommandText = (wal ? "PRAGMA journal_mode=WAL;" : "") + "CREATE TABLE items(value TEXT); INSERT INTO items VALUES('library');"; cmd.ExecuteNonQuery();
            return connection;
        }
        public string ReadDatabase(string? path = null)
        {
            using var connection = new SqliteConnection($"Data Source={path ?? Database};Mode=ReadOnly;Pooling=False"); connection.Open();
            using var cmd = connection.CreateCommand(); cmd.CommandText = "SELECT value FROM items"; return (string)cmd.ExecuteScalar()!;
        }
        public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(Root, true); }
    }
}
