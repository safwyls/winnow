using Winnow.Core.Domain;

namespace Winnow.Core.Repositories;

/// <summary>
/// The <c>work_images</c> table (migration 0028). One row per (work, source,
/// kind), so each enrichment source keeps its own reading and a re-read from
/// one source cannot clobber the other's. Modelled on
/// <see cref="IWorkMaturityRepository"/>.
/// </summary>
public interface IWorkImageRepository
{
    /// <summary>
    /// Inserts or replaces the images from one source and kind. Replaces that
    /// source's own row only — the other source's row is untouched.
    /// </summary>
    Task UpsertAsync(WorkImages images, CancellationToken ct = default);

    /// <summary>
    /// Removes one source's images for one work. Exists because a source that
    /// stops reporting a figure must have its row removed rather than left
    /// standing: the last screenshot IGDB withdrew is not a screenshot the
    /// modal should still try to draw.
    /// </summary>
    Task<bool> DeleteAsync(long workId, string source, string kind, CancellationToken ct = default);

    /// <summary>Every source's images for one work, ordered by source then kind.</summary>
    Task<IReadOnlyList<WorkImages>> GetForWorkAsync(long workId, CancellationToken ct = default);
}

/// <summary>
/// The <c>work_ratings</c> table (migration 0028). One row per (work, source),
/// so IGDB's user body, IGDB's critics and Steam each keep their own reading
/// and a re-read from one source cannot clobber the other's. Modelled on
/// <see cref="IWorkMaturityRepository"/>.
/// </summary>
public interface IWorkRatingRepository
{
    /// <summary>
    /// Inserts or replaces the rating from one source. Replaces that source's
    /// own row only — the other source's row is untouched.
    /// </summary>
    Task UpsertAsync(WorkRating rating, CancellationToken ct = default);

    /// <summary>
    /// Removes one source's rating for one work. Exists because a source that
    /// stops reporting a figure must have its row removed rather than left
    /// standing.
    /// </summary>
    Task<bool> DeleteAsync(long workId, string source, CancellationToken ct = default);

    /// <summary>Every source's rating for one work, ordered by source.</summary>
    Task<IReadOnlyList<WorkRating>> GetForWorkAsync(long workId, CancellationToken ct = default);
}
