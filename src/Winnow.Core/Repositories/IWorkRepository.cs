using Winnow.Core.Domain;

namespace Winnow.Core.Repositories;

public interface IWorkRepository
{
    /// <summary>Inserts a work (Id ignored) and returns the assigned id.</summary>
    Task<long> InsertAsync(Work work, CancellationToken ct = default);

    /// <summary>
    /// Renames a work and sets <see cref="Work.NameIsProvisional"/>. Used to
    /// promote a placeholder name to the real title once a source supplies one;
    /// never to demote a real title back to a placeholder.
    /// </summary>
    Task UpdateNameAsync(long id, string name, bool nameIsProvisional, CancellationToken ct = default);

    Task<Work?> GetAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<Work>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Works still holding a placeholder name, with their external id for enrichment lookup.</summary>
    Task<IReadOnlyList<Queries.ProvisionalNameTarget>> GetProvisionalNameTargetsAsync(
        string provider, CancellationToken ct = default);

    /// <summary>
    /// Works with any empty metadata column (name, igdb_id, year, summary, cover, publisher).
    /// Returns one row per external id across all stores. Shrinks to nothing as enrichment fills in.
    /// </summary>
    Task<IReadOnlyList<Queries.EnrichmentTarget>> GetEnrichmentTargetsAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Applies enrichment metadata under a one-way promotion rule: null/blank fields
    /// are skipped, no column is ever cleared, and igdb_id is write-once.
    /// </summary>
    /// <returns>True when a placeholder title was replaced by a real one.</returns>
    Task<bool> ApplyEnrichmentAsync(
        Queries.WorkEnrichment enrichment, CancellationToken ct = default);
}
