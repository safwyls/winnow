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
}
