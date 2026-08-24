namespace Hoard.Core.Queries;

/// <summary>
/// One work's worth of metadata offered by an enrichment source, to be applied
/// under the one-way promotion rule (see
/// <see cref="Repositories.IWorkRepository.ApplyEnrichmentAsync"/>).
///
/// <para><b>Deliberately not a <c>Work</c>.</b> An "update the whole row"
/// method is the one shape that can clobber: pass a <c>Work</c> assembled from
/// a partial IGDB answer and every field the source did not know arrives as
/// null and overwrites a real value with it. This record carries only the
/// fields enrichment is entitled to fill, and every one of them is nullable
/// with "null means the source said nothing" — never "the source said
/// nothing is there".</para>
///
/// <para>Blank and whitespace strings are normalised to null by the repository,
/// so a source answering with <c>""</c> is treated as no answer rather than as
/// an instruction to blank the column.</para>
/// </summary>
/// <param name="WorkId">The work to fill in.</param>
/// <param name="Name">
/// The canonical title, or null. Applied ONLY while the stored name is still a
/// machine-minted placeholder: a real title — whether from an earlier run, from
/// the store, or edited by the user — is never overwritten (§ the resolver's
/// one-way promotion rule, and <see cref="Domain.Work.NameIsProvisional"/>).
/// </param>
/// <param name="IgdbId">
/// IGDB's game id. Applied only when the work has none: <c>works.igdb_id</c> is
/// the canonical identity and re-pointing it is not enrichment, it is a merge.
/// </param>
/// <param name="FirstReleaseYear">Feeds the matcher's ±1-year signal (§5.3).</param>
/// <param name="Summary">IGDB's prose summary.</param>
/// <param name="CoverUrl">Absolute cover url.</param>
/// <param name="Publisher">
/// The primary publisher, already reduced to one deterministic name by the
/// caller. Feeds the matcher's publisher signal (§5.3, migration 0005).
/// </param>
public sealed record WorkEnrichment(
    long WorkId,
    string? Name = null,
    long? IgdbId = null,
    int? FirstReleaseYear = null,
    string? Summary = null,
    string? CoverUrl = null,
    string? Publisher = null)
{
    /// <summary>
    /// True when there is nothing to write. Enrichment skips these outright
    /// rather than opening a transaction to update a row to its own values —
    /// which is what keeps a re-run of a fully enriched library free.
    /// </summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Name)
        && IgdbId is null
        && FirstReleaseYear is null
        && string.IsNullOrWhiteSpace(Summary)
        && string.IsNullOrWhiteSpace(CoverUrl)
        && string.IsNullOrWhiteSpace(Publisher);
}
