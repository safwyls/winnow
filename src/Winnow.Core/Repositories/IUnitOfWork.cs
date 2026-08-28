namespace Winnow.Core.Repositories;

/// <summary>
/// One atomic write scope over the whole data layer (§5.3: "get it wrong and
/// the dataset is untrustworthy"). While a unit of work is open, every
/// repository built over the same factory runs on its single connection and
/// its single transaction — call sites keep their shape, they simply become
/// atomic.
///
/// <para>Disposing without <see cref="Commit"/> rolls back. That is the point:
/// a crash midway through creating a work + release + external id must leave
/// nothing behind, not an orphan work the next sync cannot find by external id
/// and therefore duplicates.</para>
/// </summary>
public interface IUnitOfWork : IDisposable
{
    /// <summary>Commits every write made inside the scope. Idempotent.</summary>
    void Commit();
}

/// <summary>
/// Opens <see cref="IUnitOfWork"/> scopes. Implemented by the data layer's
/// connection factory; Resolve depends on this abstraction only (§5.1).
/// </summary>
public interface IUnitOfWorkFactory
{
    /// <summary>
    /// Begins an atomic write scope for the current async flow. Repositories
    /// enlist automatically until it is disposed. SQLite has one writer, so
    /// scopes do not nest — beginning a second one while the first is open
    /// throws.
    /// </summary>
    IUnitOfWork Begin();
}
