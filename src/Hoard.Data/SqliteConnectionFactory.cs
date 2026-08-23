using Hoard.Core.Repositories;
using Microsoft.Data.Sqlite;

namespace Hoard.Data;

public sealed class SqliteConnectionFactory : ISqliteConnectionFactory
{
    /// <summary>
    /// The open unit of work for the current async flow, if any. Ambient rather
    /// than threaded through every method so that enlisting does not change the
    /// shape of a single call site. Held per factory instance (not statically)
    /// so two databases — or two tests — never see each other's scope.
    /// </summary>
    private readonly AsyncLocal<SqliteUnitOfWork?> _ambient = new();

    /// <param name="databasePath">Path to the SQLite file.</param>
    /// <param name="pooling">
    /// Connection pooling. On for the app. Tests pass <c>false</c>: pooling is a
    /// process-wide resource, and the only way to release a pooled handle so a
    /// temp file can be deleted is <c>SqliteConnection.ClearAllPools()</c>,
    /// which clears every pool in the process — including those belonging to
    /// other test classes running in parallel. Unpooled connections close when
    /// disposed, so each test's database is genuinely its own.
    /// </param>
    public SqliteConnectionFactory(string databasePath, bool pooling = true)
    {
        DapperConfig.EnsureConfigured();

        DatabasePath = Path.GetFullPath(databasePath);
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            ForeignKeys = true, // emits PRAGMA foreign_keys=ON on open
            Pooling = pooling,
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

    public DbLease Lease()
    {
        var ambient = _ambient.Value;
        return ambient is { IsOpen: true }
            ? new DbLease(ambient.Connection, ambient.Transaction, owned: false)
            : new DbLease(Open(), transaction: null, owned: true);
    }

    /// <summary>
    /// Begins the ambient transaction. Deliberately synchronous: the ambient
    /// slot must be written in the caller's execution context to be visible to
    /// the rest of the caller's flow, and SQLite's BeginTransaction does no I/O
    /// worth awaiting anyway.
    /// </summary>
    public IUnitOfWork Begin()
    {
        if (_ambient.Value is { IsOpen: true })
        {
            throw new InvalidOperationException(
                "A unit of work is already open on this connection factory. "
                + "SQLite has a single writer; scopes do not nest.");
        }

        var connection = Open();
        SqliteUnitOfWork unitOfWork;
        try
        {
            unitOfWork = new SqliteUnitOfWork(this, connection, connection.BeginTransaction());
        }
        catch
        {
            connection.Dispose();
            throw;
        }

        _ambient.Value = unitOfWork;
        return unitOfWork;
    }

    private void Release(SqliteUnitOfWork unitOfWork)
    {
        if (ReferenceEquals(_ambient.Value, unitOfWork))
        {
            _ambient.Value = null;
        }
    }

    /// <summary>
    /// The ambient scope: one connection, one transaction, rolled back unless
    /// <see cref="Commit"/> is called.
    /// </summary>
    private sealed class SqliteUnitOfWork : IUnitOfWork
    {
        private readonly SqliteConnectionFactory _factory;

        internal SqliteUnitOfWork(
            SqliteConnectionFactory factory, SqliteConnection connection, SqliteTransaction transaction)
        {
            _factory = factory;
            Connection = connection;
            Transaction = transaction;
            IsOpen = true;
        }

        internal SqliteConnection Connection { get; }

        internal SqliteTransaction Transaction { get; }

        internal bool IsOpen { get; private set; }

        public void Commit()
        {
            if (!IsOpen)
            {
                return;
            }

            Transaction.Commit();
            Close();
        }

        public void Dispose()
        {
            if (IsOpen)
            {
                // Never committed: the whole scope disappears, which is the
                // guarantee an entity-creating pass depends on.
                Transaction.Rollback();
                Close();
            }
        }

        private void Close()
        {
            IsOpen = false;
            _factory.Release(this);
            Transaction.Dispose();
            Connection.Dispose();
        }
    }
}
