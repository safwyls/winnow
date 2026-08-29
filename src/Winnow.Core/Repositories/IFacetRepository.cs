using Winnow.Core.Queries;

namespace Winnow.Core.Repositories;

/// <summary>
/// CRUD for the <c>facets</c> / <c>work_facets</c> / <c>release_facets</c> tables
/// (migration 0007). Reads return the full snapshot for in-memory filtering;
/// writes replace a work's or release's descriptors and skip the write when unchanged.
/// </summary>
public interface IFacetRepository
{
    /// <summary>The whole vocabulary and every release's descriptors, in one read.</summary>
    Task<FacetSnapshot> GetSnapshotAsync(CancellationToken ct = default);

    /// <summary>The vocabulary alone, including facets nothing currently carries.</summary>
    Task<IReadOnlyList<Facet>> GetVocabularyAsync(CancellationToken ct = default);

    /// <summary>
    /// Replaces the descriptors for one work (IGDB genres, themes, perspectives, game modes).
    /// Mints new facet rows as needed; blank names are dropped. An empty list clears
    /// the work's descriptors — callers that failed to fetch must not call this.
    /// </summary>
    /// <returns>Rows written (inserted plus deleted); 0 when the stored set already matched.</returns>
    Task<int> SetWorkFacetsAsync(
        long workId, IReadOnlyList<FacetAssignment> facets, CancellationToken ct = default);

    /// <summary>
    /// Replaces the descriptors for one release (Steam tags, features, controller support).
    /// Same clearing rule as <see cref="SetWorkFacetsAsync"/>.
    /// </summary>
    /// <returns>Rows written (inserted plus deleted); 0 when the stored set already matched.</returns>
    Task<int> SetReleaseFacetsAsync(
        long releaseId, IReadOnlyList<FacetAssignment> facets, CancellationToken ct = default);
}
