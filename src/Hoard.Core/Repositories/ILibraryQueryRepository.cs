using Hoard.Core.Queries;

namespace Hoard.Core.Repositories;

/// <summary>
/// Read-side queries over the whole library. Home of the derived-bucket
/// query (§6.1) — buckets are computed here, never stored.
/// </summary>
public interface ILibraryQueryRepository
{
    /// <summary>
    /// One row per ownership with its derived bucket, computed from the
    /// latest play record and latest update event using the supplied
    /// thresholds. Precedence: never-touched, bounced, retired,
    /// stale-but-patched, active.
    /// </summary>
    Task<IReadOnlyList<OwnershipBucket>> GetOwnershipBucketsAsync(
        BucketThresholds thresholds, CancellationToken ct = default);
}
