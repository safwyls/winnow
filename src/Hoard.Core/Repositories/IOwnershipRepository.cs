using Hoard.Core.Domain;

namespace Hoard.Core.Repositories;

public interface IOwnershipRepository
{
    /// <summary>Inserts an ownership (Id ignored) and returns the assigned id.</summary>
    Task<long> InsertAsync(Ownership ownership, CancellationToken ct = default);

    Task<Ownership?> GetAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<Ownership>> GetByReleaseAsync(long releaseId, CancellationToken ct = default);

    /// <summary>
    /// Inserts the ownership, or refreshes the existing row for the same
    /// (release_id, store), and returns its id. This is the ingest path: one
    /// ownership per release per store, enforced by a UNIQUE index rather than
    /// by a read-then-insert that races.
    ///
    /// <para>Refreshed on conflict: install path, install state, and
    /// <see cref="Ownership.AccountRef"/> attribution — the account the
    /// accompanying play record came from, so minutes, last-played and
    /// attribution never disagree. A candidate that names no account leaves the
    /// stored attribution alone rather than erasing it.</para>
    /// </summary>
    Task<long> UpsertAsync(Ownership ownership, CancellationToken ct = default);

    /// <summary>
    /// Updates the install-state fields (install_path, installed) observed by
    /// an ingest sync. Other columns are left untouched.
    /// </summary>
    Task UpdateInstallStateAsync(long id, string? installPath, bool installed, CancellationToken ct = default);

    Task<IReadOnlyList<Ownership>> GetAllAsync(CancellationToken ct = default);
}
