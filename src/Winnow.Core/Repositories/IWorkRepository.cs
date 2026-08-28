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

    /// <summary>
    /// Works still holding a placeholder name, with the external id enrichment
    /// can resolve them by. Backed by the partial index migration 0002 added for
    /// exactly this sweep, so it stays cheap and returns nothing once the
    /// backlog is drained.
    /// </summary>
    Task<IReadOnlyList<Queries.ProvisionalNameTarget>> GetProvisionalNameTargetsAsync(
        string provider, CancellationToken ct = default);

    /// <summary>
    /// Works enrichment still has something to do for: a placeholder name, or
    /// any empty metadata column (<c>igdb_id</c>, <c>first_release_year</c>,
    /// <c>summary</c>, <c>cover_url</c>, <c>publisher</c>).
    ///
    /// <para>Strictly wider than
    /// <see cref="GetProvisionalNameTargetsAsync"/>, which answers only "who
    /// still needs a title". On a library named by an earlier build that set is
    /// empty, so an enrichment pass keyed on it alone would back-fill nothing
    /// — every work would keep the empty year and publisher that make two of
    /// §5.3's four soft-match signals permanently silent.</para>
    ///
    /// <para>A work with every column filled is not returned, so the set
    /// shrinks to nothing as the backlog drains and a warm library costs one
    /// scan that yields no rows. Nothing here touches the network: what stops a
    /// re-fetch is the caller's metadata cache, not this query.</para>
    ///
    /// <para><b>Every store, and no provider parameter — that parameter was the
    /// bug.</b> This used to take one provider and the only caller passed
    /// <c>steam</c>, so the 67 Epic and 14 GOG releases in the author's library
    /// were never in the result set and measured exactly zero <c>igdb_id</c>,
    /// zero covers, zero years and zero summaries. Exactly zero rather than a
    /// low number is the tell: it is what a query that never asks looks like,
    /// and it is distinguishable from "IGDB had nothing" only by counting.
    /// Rows come back for every provider in
    /// <see cref="Domain.ExternalIdProviders.Stores"/>, each carrying its own
    /// <see cref="Queries.EnrichmentTarget.Provider"/>, and it is the caller's
    /// job to know how to look each one up — not this query's job to be told
    /// which single store to care about.</para>
    ///
    /// <para>A work reachable under two providers (a merged cross-store pair)
    /// yields one row per external id. That is deliberate: each is a distinct
    /// lookup route, and the writer is idempotent, so the second row costs a
    /// no-op patch rather than a wrong one.</para>
    /// </summary>
    Task<IReadOnlyList<Queries.EnrichmentTarget>> GetEnrichmentTargetsAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Applies enrichment metadata to one work under the one-way promotion
    /// rule, and reports whether the title was promoted.
    ///
    /// <para><b>One way, per column.</b> A null or blank field in
    /// <paramref name="enrichment"/> means "the source said nothing" and leaves
    /// the stored value alone — no column is ever cleared, and a real title is
    /// never reverted to a placeholder. <c>igdb_id</c> goes further and is
    /// written only when the work has none, because it is the canonical
    /// identity: re-pointing it is a merge, not enrichment, and merges need a
    /// human (§5.3).</para>
    ///
    /// <para>Deliberately NOT an "update the work" method. A general row update
    /// takes a whole <see cref="Work"/>, and every field a partial source did
    /// not know arrives as null — which is precisely how enrichment would erase
    /// the library it was meant to fill in.</para>
    /// </summary>
    /// <returns>
    /// True when a placeholder title was replaced by a real one, so the caller
    /// knows to move the release name with it in the same transaction.
    /// </returns>
    Task<bool> ApplyEnrichmentAsync(
        Queries.WorkEnrichment enrichment, CancellationToken ct = default);
}
