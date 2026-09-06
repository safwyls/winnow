using Winnow.Core.Domain;

namespace Winnow.Core.Repositories;

/// <summary>
/// The <c>work_maturity</c> table (migration 0024). One row per (work,
/// source), so each enrichment source keeps its own reading and a re-read
/// from one source cannot clobber the other's.
/// </summary>
public interface IWorkMaturityRepository
{
    /// <summary>
    /// Inserts or replaces the evidence from one source. Replaces that
    /// source's own row only — the other source's row is untouched.
    /// </summary>
    Task UpsertAsync(WorkMaturity maturity, CancellationToken ct = default);

    /// <summary>Every source's evidence for one work, ordered by source.</summary>
    Task<IReadOnlyList<WorkMaturity>> GetForWorkAsync(long workId, CancellationToken ct = default);

    /// <summary>Every stored evidence row, ordered by work then source.</summary>
    Task<IReadOnlyList<WorkMaturity>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Whether any stored evidence for this work reaches the adults-only
    /// tier, evaluated by
    /// <see cref="Winnow.Core.Queries.MaturityRules.IsExplicit"/> in C# over
    /// the stored rows. A work with no rows is never explicit.
    /// </summary>
    Task<bool> IsExplicitAsync(long workId, CancellationToken ct = default);
}
