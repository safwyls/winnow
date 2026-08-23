using Hoard.Core.Repositories;
using Microsoft.Data.Sqlite;

namespace Hoard.Data;

/// <summary>
/// Creates opened SQLite connections with the pragmas Hoard requires
/// (WAL journal, foreign keys ON) applied to every connection, and owns the
/// ambient <see cref="IUnitOfWork"/> scope repositories enlist in.
/// </summary>
public interface ISqliteConnectionFactory : IUnitOfWorkFactory
{
    /// <summary>Absolute path of the database file.</summary>
    string DatabasePath { get; }

    /// <summary>Connection string for the database (used by DbUp).</summary>
    string ConnectionString { get; }

    /// <summary>Returns an open connection with pragmas applied. Caller disposes.</summary>
    SqliteConnection Open();

    /// <summary>
    /// Borrows a connection for one repository call. Inside a unit of work this
    /// is the scope's connection and transaction; outside one it is a fresh
    /// connection the lease owns and closes. Repositories call this instead of
    /// <see cref="Open"/> so that enlisting is invisible to them.
    /// </summary>
    DbLease Lease();
}
