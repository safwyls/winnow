using Microsoft.Data.Sqlite;

namespace Winnow.Data;

/// <summary>
/// A connection borrowed for the duration of one repository call, plus the
/// transaction (if any) its commands must enlist in.
///
/// <para>Repositories never care which case they got: they write
/// <c>using var lease = _factory.Lease();</c> and pass
/// <see cref="Transaction"/> into every <c>CommandDefinition</c>. Outside a
/// unit of work the lease owns a fresh connection and closes it on dispose —
/// exactly the old per-call behaviour. Inside one, it borrows the scope's
/// connection and disposing is a no-op, so a whole resolve pass is a single
/// connection, a single set of pragmas and a single fsync'd commit.</para>
///
/// <para>Passing <see cref="Transaction"/> is not optional decoration:
/// Microsoft.Data.Sqlite rejects a command whose transaction does not match the
/// connection's active one.</para>
/// </summary>
public sealed class DbLease : IDisposable
{
    private readonly bool _owned;

    internal DbLease(SqliteConnection connection, SqliteTransaction? transaction, bool owned)
    {
        Connection = connection;
        Transaction = transaction;
        _owned = owned;
    }

    /// <summary>The connection to run commands on.</summary>
    public SqliteConnection Connection { get; }

    /// <summary>The ambient transaction, or null when there is no open unit of work.</summary>
    public SqliteTransaction? Transaction { get; }

    public void Dispose()
    {
        if (_owned)
        {
            Connection.Dispose();
        }
    }
}
