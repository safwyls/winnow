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
