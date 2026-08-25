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
    ///
    /// <para>Two rows are absent from the result, both derived here and neither
    /// stored: a demo or beta whose full game is also owned
    /// (<see cref="DemoConsolidation"/>), and — unless
    /// <see cref="BucketThresholds.ShowNonGameEntries"/> says otherwise — an
    /// entry Valve typed as something other than a game
    /// (<see cref="NonGameEntries"/>). Callers count buckets from THESE rows, so
    /// the rail's totals and the grid's tiles cannot disagree.</para>
    /// </summary>
    Task<IReadOnlyList<OwnershipBucket>> GetOwnershipBucketsAsync(
        BucketThresholds thresholds, CancellationToken ct = default);
}
