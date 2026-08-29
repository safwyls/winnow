using Winnow.Core.Domain;

namespace Winnow.Core.Repositories;

public interface IOwnershipRepository
{
    /// <summary>Inserts an ownership (Id ignored) and returns the assigned id.</summary>
    Task<long> InsertAsync(Ownership ownership, CancellationToken ct = default);

    Task<Ownership?> GetAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<Ownership>> GetByReleaseAsync(long releaseId, CancellationToken ct = default);

    /// <summary>
    /// Upserts an ownership keyed on (release_id, store). Null fields mean
    /// "this pass could not tell" and never overwrite a better stored value
    /// (COALESCE). Install state is three-valued: null writes neither column,
    /// non-null writes both.
    /// </summary>
    Task<long> UpsertAsync(OwnershipUpsert ownership, CancellationToken ct = default);

    Task<IReadOnlyList<Ownership>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Fills empty acquisition columns on an existing ownership row. Every
    /// assignment is COALESCE(stored, incoming) so a column that already holds a
    /// value keeps it. Returns true when at least one column was actually written.
    ///
    /// <para>The WHERE clause requires at least one column to be genuinely empty
    /// AND to have something to put in it. That makes re-runs idempotent and
    /// makes the return value honest: without it the UPDATE would write each
    /// column back onto itself and "did the import do anything" would always
    /// answer yes.</para>
    /// </summary>
    Task<bool> FillAcquisitionFactsAsync(OwnershipAcquisitionFill fill, CancellationToken ct = default);
}
