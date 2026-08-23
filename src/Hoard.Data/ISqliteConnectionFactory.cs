using Microsoft.Data.Sqlite;

namespace Hoard.Data;

/// <summary>
/// Creates opened SQLite connections with the pragmas Hoard requires
/// (WAL journal, foreign keys ON) applied to every connection.
/// </summary>
public interface ISqliteConnectionFactory
{
    /// <summary>Absolute path of the database file.</summary>
    string DatabasePath { get; }

    /// <summary>Connection string for the database (used by DbUp).</summary>
    string ConnectionString { get; }

    /// <summary>Returns an open connection with pragmas applied. Caller disposes.</summary>
    SqliteConnection Open();
}
