namespace Winnow.Core.Queries;

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
/// <param name="SteamAppType">
/// Valve's <c>common.type</c> for the appid, verbatim (migration 0006). Feeds
/// <see cref="DemoConsolidation"/>'s first gate. Null is "the source did not
/// say", which for this column is the common case: several appids are
/// unreadable without a Steam Web API key.
/// </param>
/// <param name="EpicCategories">
/// Epic's <c>categories[].path</c> list for the catalog item, comma-joined and
/// verbatim (migration 0009). Feeds the library view's non-game filter through
/// <see cref="EpicGameFilter"/> — the same rule the local Epic scan applies
/// before a candidate is ever emitted.
///
/// <para>Null is "the source did not say", and for this column that is the
/// common case: it is only ever non-null for an Epic work, on an install where
/// the user has signed in to Epic, once the catalog service has answered. A null
/// leaves the stored value exactly as it was — a work classified on an earlier
/// run must not be un-classified by a later run that could not reach the
/// service.</para>
/// </param>
public sealed record WorkEnrichment(
    long WorkId,
    string? Name = null,
    long? IgdbId = null,
    int? FirstReleaseYear = null,
    string? Summary = null,
    string? CoverUrl = null,
    string? Publisher = null,
    string? SteamAppType = null,
    string? EpicCategories = null)
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
        && string.IsNullOrWhiteSpace(Publisher)
        && string.IsNullOrWhiteSpace(SteamAppType)
        && string.IsNullOrWhiteSpace(EpicCategories);
}
