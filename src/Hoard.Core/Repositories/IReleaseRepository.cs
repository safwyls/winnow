using Hoard.Core.Domain;

namespace Hoard.Core.Repositories;

public interface IReleaseRepository
{
    /// <summary>Inserts a release (Id ignored) and returns the assigned id.</summary>
    Task<long> InsertAsync(Release release, CancellationToken ct = default);

    Task<Release?> GetAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<Release>> GetByWorkAsync(long workId, CancellationToken ct = default);

    Task AddExternalIdAsync(ExternalId externalId, CancellationToken ct = default);

    Task<IReadOnlyList<ExternalId>> GetExternalIdsAsync(long releaseId, CancellationToken ct = default);

    /// <summary>The hard-join lookup (§5.3 step 1): find a release by provider id.</summary>
    Task<Release?> FindByExternalIdAsync(string provider, string providerId, CancellationToken ct = default);
}
