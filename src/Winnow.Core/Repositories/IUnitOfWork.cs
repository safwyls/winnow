namespace Winnow.Core.Repositories;

/// <summary>
/// One atomic write scope over the data layer. Disposing without
/// <see cref="Commit"/> rolls back.
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
    /// <summary>Begins an atomic write scope. Scopes do not nest (SQLite has one writer).</summary>
    IUnitOfWork Begin();
}
