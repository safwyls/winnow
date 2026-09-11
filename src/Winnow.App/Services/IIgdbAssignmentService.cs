using Winnow.Core.Domain;

namespace Winnow.App.Services;

/// <summary>
/// One candidate in a title search: name, cover, first release year and
/// platforms — the four facts that separate Prey (2006) from Prey (2017).
/// <see cref="Platforms"/> is empty rather than null when IGDB named none.
/// <see cref="CoverUrl"/> is IGDB's own <c>t_cover_big</c> URL; the image
/// id inside it becomes the tile's cover key.
///
/// <para>An App-layer read model, not the enrichment module's own record.
/// Architecture-boundary tests enforce §5.1 on what a view model names as
/// well as what it receives, so a candidate row reaches the modal through
/// this type and no enrichment type crosses the seam.</para>
/// </summary>
public sealed record IgdbCandidate(
    long IgdbId,
    string Name,
    string? CoverUrl,
    int? FirstReleaseYear,
    IReadOnlyList<string> Platforms);

/// <summary>
/// The game that already holds an IGDB entry, shaped as an App-layer read
/// model like <see cref="IgdbCandidate"/>. Architecture-boundary tests
/// enforce §5.1 on what a view model names, so the holder reaches the
/// modal through this type and no repository type crosses the seam.
/// </summary>
public sealed record IgdbClaimingGame(
    long WorkId,
    string Title,
    string? CoverUrl,
    int? FirstReleaseYear);

/// <summary>
/// What an assignment attempt did. Every refusal is one of these rather than
/// an exception, and each is a state the modal renders with its own sentence.
/// </summary>
public enum IgdbAssignmentOutcome
{
    /// <summary>The pin was recorded and the work's metadata was rewritten.</summary>
    Assigned,

    /// <summary>No such game is in the library any more.</summary>
    WorkNotFound,

    /// <summary>
    /// IGDB returned no record for the chosen entry, so nothing was pinned.
    /// Pinning without metadata would leave the wrong metadata in place and
    /// block the automatic pass from ever correcting it.
    /// </summary>
    MetadataUnavailable,

    /// <summary>Another game in the library already holds that IGDB entry (<c>works.igdb_id</c> is UNIQUE).</summary>
    IgdbIdClaimedByAnotherWork,

    MappingChanged,
    IdentifierHistoryUnavailable,
    StorefrontObservation,

    /// <summary>Something threw and was logged. The user sees an error, not a wrong pin.</summary>
    Failed,
}

/// <summary>
/// App-layer seam in front of
/// <c>Winnow.Enrich.Igdb.IgdbManualAssignment</c>, and the only App type
/// that names it. The same arrangement <see cref="IUpdateFlagService"/>
/// and <see cref="IFeedService"/> have in front of their own modules. A
/// view model depends on this interface, so the details modal's control is
/// testable without an IGDB host.
///
/// <para>Nothing on this interface throws at a view model. A dead network,
/// a rejected query or a failed write comes back as an empty list, a
/// status or false.</para>
/// </summary>
public interface IIgdbAssignmentService
{
    /// <summary>
    /// Candidates for a title, at the client's default limit. An empty list
    /// means either nothing matched or the search failed; the two are the
    /// same answer to the user.
    /// </summary>
    Task<IReadOnlyList<IgdbCandidate>> SearchAsync(string title, CancellationToken ct = default);

    /// <summary>
    /// The one IGDB game carrying this id, shaped as a candidate, or null.
    /// Null covers three cases that are one answer to the user: no such id,
    /// a non-positive id, and a failed lookup. Like everything else on this
    /// seam, it does not throw at a view model. The candidate carries the
    /// same facts a <see cref="SearchAsync"/> candidate does, platforms
    /// included, so a row found by id and a row found by title draw
    /// alike.
    /// </summary>
    Task<IgdbCandidate?> GetCandidateByIdAsync(long igdbId, CancellationToken ct = default);

    /// <summary>
    /// Pins the work to the chosen IGDB game and rewrites its metadata.
    /// Every refusal is a status, never an exception.
    /// </summary>
    Task<IgdbAssignmentOutcome> AssignAsync(long workId, long igdbId, CancellationToken ct = default);

    /// <summary>
    /// The work that already holds this IGDB entry, or null. Null covers
    /// four cases that are one answer to the view model: no such holder,
    /// a non-positive id, no work repository registered, and a failed
    /// read. Like everything else on this seam, it does not throw at a
    /// view model.
    /// </summary>
    Task<IgdbClaimingGame?> FindClaimingGameAsync(long igdbId, CancellationToken ct = default);

    /// <summary>
    /// Returns the work to automatic resolution. False means the write did
    /// not land or there was no live pin.
    /// </summary>
    Task<bool> ClearAsync(long workId, CancellationToken ct = default);

    /// <summary>
    /// The live pin, or null when the work is on automatic resolution. Null
    /// hides the Clear control on the modal.
    /// </summary>
    Task<WorkIgdbPin?> GetPinAsync(long workId, CancellationToken ct = default);

    /// <summary>
    /// Every work id carrying a live pin, in one read. The library load needs
    /// the whole set at once to decide cover-key precedence (a live pin
    /// outranks the store capsule), and <see cref="GetPinAsync"/> is per-work.
    ///
    /// <para>Empty is a normal answer and also what a failed read degrades
    /// into, like every other call on this seam: the library still loads,
    /// and an empty set is the store-capsule precedence the view model had
    /// before pins existed.</para>
    /// </summary>
    Task<IReadOnlySet<long>> GetLivePinnedWorkIdsAsync(CancellationToken ct = default);
}
