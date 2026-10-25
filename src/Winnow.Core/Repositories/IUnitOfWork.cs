namespace Winnow.Core.Repositories;

/// <summary>
/// One atomic transaction scope over the data layer. Disposing without
/// <see cref="Commit"/> rolls back.
/// </summary>
public interface IUnitOfWork : IDisposable
{
    /// <summary>Completes the scope and commits any writes. Idempotent.</summary>
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

    /// <summary>
    /// Begins a coherent read snapshot without reserving the database writer.
    /// Callers must not write inside this scope. Scopes do not nest.
    /// </summary>
    IUnitOfWork BeginRead();
}
