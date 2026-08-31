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

    /// <summary>
    /// How many library entries the account-visibility filter would remove —
    /// the number the toggle's own label states.
    ///
    /// <para>Answered by running the bucket query in both modes and
    /// subtracting, so the figure counts tiles that actually disappear rather
    /// than rows in a table: a demo already folded into its base game, or a
    /// soundtrack the non-game filter had removed anyway, was never on screen
    /// to be hidden and is not counted.</para>
    ///
    /// <para>Independent of the stored preference — it answers the same way
    /// whether the filter is currently on or off, because the toggle has to
    /// state what it does before it is used. Zero on any install with no
    /// confirmed Steam account, which is also every install where the toggle is
    /// disabled.</para>
    /// </summary>
    Task<int> CountHiddenByAccountScopeAsync(
        BucketThresholds thresholds, CancellationToken ct = default);

    /// <summary>Every release with its IGDB id and Steam appid, for facet backfill.</summary>
    Task<IReadOnlyList<FacetTarget>> GetFacetTargetsAsync(CancellationToken ct = default);
}
