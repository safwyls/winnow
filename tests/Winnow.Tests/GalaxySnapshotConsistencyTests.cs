using Microsoft.Data.Sqlite;
using Winnow.Ingest.Gog;
using Xunit;

namespace Winnow.Tests;

public sealed class GalaxySnapshotConsistencyTests
{
    [Fact]
    public void A_held_native_writer_defers_the_snapshot_without_touching_launcher_files()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var tree = GalaxyFixtureTree.Create(walMode: true);
        using (var writer = Open(tree.DatabasePath))
        {
            Execute(writer, "PRAGMA wal_autocheckpoint=0; CREATE TABLE snapshot_probe(value TEXT); INSERT INTO snapshot_probe VALUES ('committed');");
            var before = Bytes(tree);
            Assert.Null(GalaxyDatabaseSnapshot.Take(tree.DatabasePath));
            AssertUnchanged(tree, before);
        }
        using var later = GalaxyDatabaseSnapshot.Take(tree.DatabasePath);
        Assert.NotNull(later);
        using var read = later.OpenReadOnly();
        Assert.Equal("committed", Scalar(read, "SELECT value FROM snapshot_probe"));
    }

    [Fact]
    public void Copy_guards_block_checkpoint_and_WAL_rollover_between_main_and_WAL_copies()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var tree = GalaxyFixtureTree.Create(walMode: true);
        CreateImmutableWalPair(tree.DatabasePath);
        var before = Bytes(tree);
        var reachedInterleave = false;
        using (var snapshot = GalaxyDatabaseSnapshot.TakeGuarded(tree.DatabasePath, null, () =>
        {
            reachedInterleave = true;
            // This is the exact gap that used to permit a checkpoint followed
            // by a new WAL generation after an older main file was copied.
            Assert.Throws<SqliteException>(() =>
            {
                using var writer = Open(tree.DatabasePath);
                Execute(writer, "UPDATE snapshot_a SET value='next'; UPDATE snapshot_b SET value='next'; PRAGMA wal_checkpoint(TRUNCATE); UPDATE snapshot_a SET value='latest';");
            });
            Assert.Throws<IOException>(() =>
            {
                using var walWriter = new FileStream(tree.DatabasePath + "-wal", FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            });
            Assert.Throws<IOException>(() => File.Delete(tree.DatabasePath + "-wal"));
        }))
        {
            Assert.True(reachedInterleave);
            Assert.NotNull(snapshot);
            using var read = snapshot.OpenReadOnly();
            Assert.Equal("newest|committed", Scalar(read, "SELECT a.value || '|' || b.value FROM snapshot_a a, snapshot_b b"));
        }
        // SQLite may fall back to a read-only main handle and create its SHM
        // before rejecting the write. The competing SQLite connection owns that
        // side effect; both generation-bearing source files must remain intact.
        foreach (var (name, bytes) in before)
            Assert.Equal(bytes, ReadShared(Path.Combine(tree.StoragePath, name)));

        // Once the guard is released a real checkpoint and generation rollover
        // are allowed; the next read sees the later committed state together.
        using (var writer = Open(tree.DatabasePath))
            Execute(writer, "BEGIN; UPDATE snapshot_a SET value='next'; UPDATE snapshot_b SET value='next'; COMMIT; PRAGMA wal_checkpoint(TRUNCATE); UPDATE snapshot_a SET value='latest';");
        using var later = GalaxyDatabaseSnapshot.Take(tree.DatabasePath);
        Assert.NotNull(later);
        using var laterRead = later.OpenReadOnly();
        Assert.Equal("latest|next", Scalar(laterRead, "SELECT a.value || '|' || b.value FROM snapshot_a a, snapshot_b b"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Closed_databases_without_WAL_sidecars_copy_without_source_changes(bool walMode)
    {
        using var tree = GalaxyFixtureTree.Create(walMode: walMode);
        var before = Bytes(tree);
        using var snapshot = OperatingSystem.IsWindows()
            ? GalaxyDatabaseSnapshot.Take(tree.DatabasePath)
            : GalaxyDatabaseSnapshot.CopyImmutable(tree.DatabasePath);
        Assert.NotNull(snapshot);
        Assert.Equal(40, snapshot.ReadUserVersion());
        AssertUnchanged(tree, before);
    }

    [Fact]
    public void A_possible_rollback_journal_is_never_treated_as_a_committed_database()
    {
        using var tree = GalaxyFixtureTree.Create();
        File.WriteAllBytes(tree.DatabasePath + "-journal", [1, 2, 3]);
        var before = Bytes(tree);
        Assert.Null(GalaxyDatabaseSnapshot.CopyImmutable(tree.DatabasePath));
        AssertUnchanged(tree, before);
    }

    [Fact]
    public void Immutable_caller_owned_WAL_snapshots_work_without_claiming_live_platform_support()
    {
        using var tree = GalaxyFixtureTree.Create(walMode: true);
        CreateImmutableWalPair(tree.DatabasePath);
        var before = Bytes(tree);
        if (!OperatingSystem.IsWindows())
            Assert.Null(GalaxyDatabaseSnapshot.Take(tree.DatabasePath));
        using var snapshot = GalaxyDatabaseSnapshot.CopyImmutable(tree.DatabasePath);
        Assert.NotNull(snapshot);
        using var read = snapshot.OpenReadOnly();
        Assert.Equal("newest|committed", Scalar(read, "SELECT a.value || '|' || b.value FROM snapshot_a a, snapshot_b b"));
        AssertUnchanged(tree, before);
    }

    private static void CreateImmutableWalPair(string path)
    {
        byte[] main;
        byte[] wal;
        using (var writer = Open(path))
        {
            Execute(writer, "PRAGMA wal_autocheckpoint=0; CREATE TABLE snapshot_a(value TEXT); CREATE TABLE snapshot_b(value TEXT); BEGIN; INSERT INTO snapshot_a VALUES ('old'); INSERT INTO snapshot_b VALUES ('old'); COMMIT; PRAGMA wal_checkpoint(TRUNCATE); BEGIN; UPDATE snapshot_a SET value='committed'; UPDATE snapshot_b SET value='committed'; COMMIT; PRAGMA wal_checkpoint(TRUNCATE); UPDATE snapshot_a SET value='newest';");
            // This fixture has one writer and no mutations between captures.
            main = ReadShared(path);
            wal = ReadShared(path + "-wal");
        }
        File.WriteAllBytes(path, main);
        File.WriteAllBytes(path + "-wal", wal);
        File.Delete(path + "-shm");
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 1
        }.ToString());
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static Dictionary<string, byte[]> Bytes(GalaxyFixtureTree tree)
        => tree.StorageDirectoryEntries().ToDictionary(name => name,
            name => ReadShared(Path.Combine(tree.StoragePath, name)));

    private static byte[] ReadShared(string path)
    {
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var bytes = new MemoryStream();
        source.CopyTo(bytes);
        return bytes.ToArray();
    }

    private static void AssertUnchanged(GalaxyFixtureTree tree, Dictionary<string, byte[]> before)
    {
        Assert.Equal(before.Keys.Order(), tree.StorageDirectoryEntries().Order());
        foreach (var (name, bytes) in before)
            Assert.Equal(bytes, ReadShared(Path.Combine(tree.StoragePath, name)));
    }
}
