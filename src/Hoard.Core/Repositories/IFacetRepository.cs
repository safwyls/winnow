using Hoard.Core.Queries;

namespace Hoard.Core.Repositories;

/// <summary>
/// The <c>facets</c> / <c>work_facets</c> / <c>release_facets</c> tables
/// (migration 0007): the descriptors the library filter panel asks about.
///
/// <para><b>Read side:</b> one <see cref="GetSnapshotAsync"/> per library load.
/// The filter runs in memory over the whole library — the same decision
/// <c>LibraryViewModel</c> already made for search and buckets — so this returns
/// everything at once rather than offering a filtered query. There is
/// deliberately no <c>GetReleasesMatching(LibraryFilter)</c> here: pushing the
/// filter into SQL would mean two implementations of what a filter means, and
/// the live-list one and the panel one would drift.</para>
///
/// <para><b>Write side:</b> whole-scope replacement, and cheap when nothing
/// changed. The backfill re-derives a work's or a release's descriptors from
/// <c>metadata_cache</c> and hands the result over; if it matches what is stored,
/// nothing is written and the call reports zero. That is what makes a re-run
/// free rather than merely harmless.</para>
/// </summary>
public interface IFacetRepository
{
    /// <summary>
    /// The whole vocabulary and every release's descriptors, in one read.
    ///
    /// <para>A release with no cached metadata simply has no entry. It has NOT
    /// left the library — the bucket query is what decides which tiles exist, and
    /// nothing here can remove one.</para>
    /// </summary>
    Task<FacetSnapshot> GetSnapshotAsync(CancellationToken ct = default);

    /// <summary>
    /// The vocabulary alone, including facets nothing currently carries (the
    /// seeded game modes among them).
    /// </summary>
    Task<IReadOnlyList<Facet>> GetVocabularyAsync(CancellationToken ct = default);

    /// <summary>
    /// Replaces the descriptors stored against one <c>works</c> row — IGDB's
    /// genres, themes, player perspectives and game modes, which are facts about
    /// the game rather than about one storefront's listing of it.
    ///
    /// <para>Mints any <c>facets</c> row the library has not seen before. An
    /// assignment whose name is blank is dropped rather than minting a nameless
    /// facet: an unnamed checkbox is not a filter, it is a puzzle.</para>
    ///
    /// <para>An empty list CLEARS this work's descriptors. Callers that merely
    /// failed to fetch must not call this at all — "the source said nothing" and
    /// "the source says there is nothing" are different facts, and only the
    /// second one belongs in the database.</para>
    /// </summary>
    /// <returns>Rows written (inserted plus deleted); 0 when the stored set already matched.</returns>
    Task<int> SetWorkFacetsAsync(
        long workId, IReadOnlyList<FacetAssignment> facets, CancellationToken ct = default);

    /// <summary>
    /// Replaces the descriptors stored against one <c>releases</c> row — Steam's
    /// store tags, storefront features, controller support and player categories,
    /// each a fact about one appid.
    ///
    /// <para>Kept at this layer because it belongs here: Skyrim and Skyrim
    /// Special Edition are separate apps with separately-voted tags, and folding
    /// them onto the Work would be §6.2's forbidden blend in a different
    /// costume.</para>
    ///
    /// <para>Same clearing rule as <see cref="SetWorkFacetsAsync"/>.</para>
    /// </summary>
    /// <returns>Rows written (inserted plus deleted); 0 when the stored set already matched.</returns>
    Task<int> SetReleaseFacetsAsync(
        long releaseId, IReadOnlyList<FacetAssignment> facets, CancellationToken ct = default);
}
