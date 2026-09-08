using System.Globalization;
using Microsoft.Data.Sqlite;
using Winnow.Data;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// <see cref="SqliteConnectionFactory.Open"/> issues pragmas after
/// <c>SqliteConnection.Open()</c> succeeds, so a database that opens but fails
/// the pragma — a corrupt file — must not leak the connection it already
/// took. A leaked handle holds the file open for the rest of the process,
/// which breaks every recovery path that wants to move or delete it.
/// </summary>
public sealed class SqliteConnectionFactoryTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"winnow-connectionfactory-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public void Opening_a_corrupt_database_throws_and_releases_the_file()
    {
        WriteCorruptDatabase(_path);
        var factory = new SqliteConnectionFactory(_path, pooling: false);

        Assert.Throws<SqliteException>(() => factory.Open());

        // The regression this guards: a leaked open connection holds the file
        // for the life of the process, which on Windows turns this delete into
        // an IOException. If Open() disposed on failure, this is immediate and
        // needs no retry.
        File.Delete(_path);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void Opening_a_corrupt_database_through_Lease_also_releases_the_file()
    {
        // Lease() is the path every repository actually calls; it opens a
        // fresh connection through the same Open() whenever there is no
        // ambient unit of work, so the same leak would show up here too.
        WriteCorruptDatabase(_path);
        var factory = new SqliteConnectionFactory(_path, pooling: false);

        Assert.Throws<SqliteException>(() => factory.Lease());

        File.Delete(_path);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void Open_bounds_the_page_cache_of_every_connection()
    {
        // A pooled connection keeps its page cache while the pool holds it, so
        // this budget, not SQLite's 2 MiB default, is the ceiling on what a
        // burst of concurrent leases can be holding at once.
        using var database = new TempDatabase();
        using var connection = database.Factory.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA cache_size;";

        // Negative means KiB; positive would mean pages.
        Assert.Equal(
            -SqliteConnectionFactory.PageCacheKib,
            Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Releasing_pooled_connections_leaves_a_borrowed_one_usable()
    {
        // The trim at the end of the startup pipeline drops the pool while the
        // schedulers and the UI may still be mid-query, so a connection that is
        // already leased must be untouched and the next lease must still open.
        // Every other factory in this suite is unpooled, so clearing the
        // process-wide pool here reaches only this test's database.
        var path = Path.Combine(Path.GetTempPath(), $"winnow-pool-{Guid.NewGuid():N}.db");
        var factory = new SqliteConnectionFactory(path, pooling: true);
        try
        {
            using (var borrowed = factory.Lease())
            {
                factory.ReleasePooledConnections();
                Assert.Equal(1, Scalar(borrowed));
            }

            using var reopened = factory.Lease();
            Assert.Equal(1, Scalar(reopened));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                if (File.Exists(path + suffix))
                {
                    File.Delete(path + suffix);
                }
            }
        }

        static int Scalar(DbLease lease)
        {
            using var command = lease.Connection.CreateCommand();
            command.CommandText = "SELECT 1;";
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// A file that opens (SQLite's <c>Open()</c> does no I/O) but is not a
    /// database, so the pragma issued right after — the exact step
    /// <see cref="SqliteConnectionFactory.Open"/> was leaking on — is what
    /// actually throws.
    /// </summary>
    private static void WriteCorruptDatabase(string path)
    {
        var garbage = new byte[4096];
        new Random(1).NextBytes(garbage);
        File.WriteAllBytes(path, garbage);
    }
}
