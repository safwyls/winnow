using Hoard.Core.Domain;

namespace Hoard.Core.Repositories;

public interface IOwnershipRepository
{
    /// <summary>Inserts an ownership (Id ignored) and returns the assigned id.</summary>
    Task<long> InsertAsync(Ownership ownership, CancellationToken ct = default);

    Task<Ownership?> GetAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<Ownership>> GetByReleaseAsync(long releaseId, CancellationToken ct = default);

    /// <summary>
    /// Updates the install-state fields (install_path, installed) observed by
    /// an ingest sync. Other columns are left untouched.
    /// </summary>
    Task UpdateInstallStateAsync(long id, string? installPath, bool installed, CancellationToken ct = default);

    Task<IReadOnlyList<Ownership>> GetAllAsync(CancellationToken ct = default);
}
