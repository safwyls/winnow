using Winnow.Core.Domain;

namespace Winnow.Core.Repositories;

public interface IReleaseRepository
{
    /// <summary>Inserts a release (Id ignored) and returns the assigned id.</summary>
    Task<long> InsertAsync(Release release, CancellationToken ct = default);

    /// <summary>
    /// Renames a release. Releases carry no provisional flag of their own — in
    /// M0 they are 1:1 with their work, so the work's
    /// <see cref="Domain.Work.NameIsProvisional"/> governs when a rename is
    /// allowed.
    /// </summary>
    Task UpdateNameAsync(long id, string name, CancellationToken ct = default);

    Task<Release?> GetAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<Release>> GetByWorkAsync(long workId, CancellationToken ct = default);

    Task AddExternalIdAsync(ExternalId externalId, CancellationToken ct = default);

    Task<IReadOnlyList<ExternalId>> GetExternalIdsAsync(long releaseId, CancellationToken ct = default);

    /// <summary>The hard-join lookup (§5.3 step 1): find a release by provider id.</summary>
    Task<Release?> FindByExternalIdAsync(string provider, string providerId, CancellationToken ct = default);

    /// <summary>
    /// Every release joined to its work, ordered by release id — the input to
    /// the soft-match sweep (§5.3 step 2).
    ///
    /// <para>Deliberately unfiltered and unpaged. The sweep needs the whole
    /// library in memory at once to block titles against each other, and the
    /// projection is four scalars per row: a 3,000-release library is well
    /// under a megabyte. Ordering by id is what makes a re-run examine pairs in
    /// the same order and therefore report the same counts.</para>
    /// </summary>
    Task<IReadOnlyList<Queries.ReleaseIdentity>> GetIdentitiesAsync(CancellationToken ct = default);
}
