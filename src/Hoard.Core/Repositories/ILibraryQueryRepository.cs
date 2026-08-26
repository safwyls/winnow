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

    /// <summary>
    /// Every release with the two external ids the facet backfill looks its
    /// descriptors up by: the work's IGDB id and the release's Steam appid.
    ///
    /// <para><b>Every release, not a "needs work" subset.</b> There is no
    /// watermark and no "already has facets" filter, for the reason
    /// <c>EnrichmentSyncService</c> records about its own targets: a watermark
    /// permanently suppresses rows a source only learns about later, and Steam
    /// tags in particular change under a game that has not itself changed. What
    /// keeps a re-run cheap is the cache (nothing is re-fetched) and the
    /// repository's read-before-write (nothing is re-stored), not a narrower
    /// query.</para>
    ///
    /// <para>Both ids are nullable and usually at least one is present. A release
    /// with neither contributes no facets and is left entirely alone — it has NOT
    /// left the library, it simply has nothing to be described by.</para>
    /// </summary>
    Task<IReadOnlyList<FacetTarget>> GetFacetTargetsAsync(CancellationToken ct = default);
}
