using Winnow.Core.Domain;

namespace Winnow.Core.Repositories;

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
    ///
    /// <para><b>Every column here refreshes only on a real answer.</b>
    /// <see cref="OwnershipUpsert.AccountRef"/> and
    /// <see cref="OwnershipUpsert.AcquiredAt"/> are COALESCEd — null means "this
    /// pass could not tell", and an emptier answer never replaces a better one.
    /// <see cref="OwnershipUpsert.Installed"/> is three-valued for the same
    /// reason, but its rule is not COALESCE: a non-null answer writes
    /// <i>both</i> install columns, <c>false</c> included, because a game the
    /// user uninstalled must stop showing as installed; a null writes neither.
    /// The path is never carried independently of the flag.</para>
    ///
    /// <para><b><see cref="UpsertAsync"/> is the only install-state writer, on
    /// purpose.</b> There used to be a second one — an <c>UpdateInstallStateAsync</c>
    /// taking a plain <c>bool</c> — with no callers anywhere. A two-valued
    /// setter cannot express "this source could not tell", so wiring it up would
    /// have reinstated exactly the bug the three-valued rule above exists to
    /// prevent. It was removed rather than left lying around; add install-state
    /// writes here, where the rule is.</para>
    /// </summary>
    Task<long> UpsertAsync(OwnershipUpsert ownership, CancellationToken ct = default);

    Task<IReadOnlyList<Ownership>> GetAllAsync(CancellationToken ct = default);
}
