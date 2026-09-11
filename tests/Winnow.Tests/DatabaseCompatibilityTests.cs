using Dapper;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Winnow.Tests;

public sealed class DatabaseCompatibilityTests
{
    [Theory]
    [InlineData("Winnow.Data.Migrations.9999_future_schema.sql")]
    [InlineData("Hoard.Data.Migrations.9999_future_schema.sql")]
    [InlineData("Winnow.Data.Migrations.0001_unrecognized_schema.sql")]
    public void Unsupported_history_is_refused_before_any_database_or_journal_change(string script)
    {
        using var db = new TempDatabase();
        using (var connection = db.Factory.Open())
        {
            connection.Execute("UPDATE SchemaVersions SET ScriptName = replace(ScriptName, 'Winnow.', 'Hoard.');");
            connection.Execute("INSERT INTO SchemaVersions (ScriptName, Applied) VALUES (@script, @applied);",
                new { script, applied = DateTime.UtcNow });
            connection.Execute("PRAGMA wal_checkpoint(TRUNCATE);");
            connection.Execute("PRAGMA journal_mode = DELETE;");
        }

        var before = File.ReadAllBytes(db.DatabasePath);

        var error = Assert.Throws<InvalidOperationException>(db.Initializer.Initialize);

        Assert.Contains(script, error.Message, StringComparison.Ordinal);
        Assert.Contains("same or a newer Winnow", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(db.DatabasePath));
        Assert.Null(db.Initializer.LastBackupPath);
        Assert.False(Directory.Exists(db.BackupDirectory));
        using var inspected = ReadOnly(db.DatabasePath);
        Assert.Equal("delete", inspected.ExecuteScalar<string>("PRAGMA journal_mode;"));
        Assert.DoesNotContain(inspected.Query<string>("SELECT ScriptName FROM SchemaVersions;"),
            name => name.StartsWith("Winnow.Data.Migrations.", StringComparison.Ordinal) && name != script);
    }

    [Fact]
    public void Current_history_remains_idempotent()
    {
        using var db = new TempDatabase();
        using var connection = db.Factory.Open();
        var before = connection.Query<string>("SELECT ScriptName FROM SchemaVersions ORDER BY ScriptName;").ToArray();

        db.Initializer.Initialize();

        Assert.Equal(before,
            connection.Query<string>("SELECT ScriptName FROM SchemaVersions ORDER BY ScriptName;").ToArray());
        Assert.Null(db.Initializer.LastBackupPath);
    }

    private static SqliteConnection ReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }
}
