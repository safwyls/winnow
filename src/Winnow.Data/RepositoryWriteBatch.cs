using Microsoft.Data.Sqlite;

namespace Winnow.Data;

/// <summary>
/// Makes a repository batch atomic without committing its caller's unit of work.
/// An ambient transaction gets a savepoint so a caught batch failure cannot leave
/// partial writes for the caller to commit later.
/// </summary>
internal sealed class RepositoryWriteBatch : IDisposable
{
    private readonly DbLease _borrowed;
    private readonly SqliteTransaction _transaction;
    private readonly string? _savepoint;
    private bool _completed;

    public RepositoryWriteBatch(ISqliteConnectionFactory factory)
    {
        _borrowed = factory.Lease();
        try
        {
            if (_borrowed.Transaction is { } ambient)
            {
                _transaction = ambient;
                _savepoint = "batch_" + Guid.NewGuid().ToString("N");
                _transaction.Save(_savepoint);
            }
            else
            {
                _transaction = _borrowed.Connection.BeginTransaction();
            }

            Lease = new DbLease(_borrowed.Connection, _transaction, owned: false);
        }
        catch
        {
            _borrowed.Dispose();
            throw;
        }
    }

    public DbLease Lease { get; }

    public void Commit()
    {
        if (_completed)
        {
            return;
        }

        if (_savepoint is null)
        {
            _transaction.Commit();
        }
        else
        {
            _transaction.Release(_savepoint);
        }

        _completed = true;
    }

    public void Dispose()
    {
        try
        {
            if (!_completed)
            {
                if (_savepoint is null)
                {
                    _transaction.Rollback();
                }
                else
                {
                    _transaction.Rollback(_savepoint);
                    _transaction.Release(_savepoint);
                }

                _completed = true;
            }
        }
        finally
        {
            if (_savepoint is null)
            {
                _transaction.Dispose();
            }

            _borrowed.Dispose();
        }
    }
}
