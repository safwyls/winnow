using Microsoft.Data.Sqlite;

namespace Hoard.Data;

public sealed class SqliteConnectionFactory : ISqliteConnectionFactory
{
    public SqliteConnectionFactory(string databasePath)
    {
        DapperConfig.EnsureConfigured();

        DatabasePath = Path.GetFullPath(databasePath);
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            ForeignKeys = true, // emits PRAGMA foreign_keys=ON on open
        }.ToString();
    }

    public string DatabasePath { get; }

    public string ConnectionString { get; }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        // WAL is persistent per-database, but issuing it per-connection is
        // cheap and keeps the guarantee independent of who created the file.
        // foreign_keys is per-connection; the connection string already set
        // it, the explicit pragma makes the requirement visible.
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();

        return connection;
    }
}
