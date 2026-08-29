using Winnow.Core.Queries;

namespace Winnow.Core.Repositories;

/// <summary>
/// Read-side queries over the whole library. Home of the derived-bucket
/// query (§6.1) — buckets are computed here, never stored.
/// </summary>
public interface ILibraryQueryRepository
{
    /// <summary>
    /// One row per ownership with its derived bucket. Excludes consolidated
    /// demos and non-game entries (unless thresholds opt in).
    /// </summary>
    Task<IReadOnlyList<OwnershipBucket>> GetOwnershipBucketsAsync(
        BucketThresholds thresholds, CancellationToken ct = default);

    /// <summary>Every release with its IGDB id and Steam appid, for facet backfill.</summary>
    Task<IReadOnlyList<FacetTarget>> GetFacetTargetsAsync(CancellationToken ct = default);
}
