namespace Winnow.Enrich.Igdb.Model;

/// <summary>
/// The outcome of a manual assignment attempt.
/// </summary>
public enum IgdbAssignmentStatus
{
    /// <summary>The pin was recorded and the work's metadata was rewritten.</summary>
    Assigned,

    /// <summary>No work with that id exists.</summary>
    WorkNotFound,

    /// <summary>
    /// IGDB returned no record for the chosen game. Pinning without
    /// metadata would leave the wrong metadata in place AND block the
    /// automatic pass from ever correcting it, which is worse than not
    /// pinning.
    /// </summary>
    MetadataUnavailable,

    /// <summary>
    /// Another work already holds that <c>igdb_id</c>. The column is
    /// UNIQUE, so the caller must present this as a real state rather
    /// than retrying.
    /// </summary>
    IgdbIdClaimedByAnotherWork,

    MappingChanged,
    IdentifierHistoryUnavailable,
    StorefrontObservation,

    /// <summary>
    /// Something threw and was logged. The caller shows an error rather
    /// than a wrong pin.
    /// </summary>
    Failed,
}

/// <summary>
/// Pairs an <see cref="IgdbAssignmentStatus"/> with the IGDB game record
/// when one was fetched. <see cref="Game"/> is null for
/// <see cref="IgdbAssignmentStatus.WorkNotFound"/>,
/// <see cref="IgdbAssignmentStatus.MetadataUnavailable"/> and
/// <see cref="IgdbAssignmentStatus.Failed"/>.
/// </summary>
public sealed record IgdbAssignmentResult(IgdbAssignmentStatus Status, IgdbGame? Game)
{
    public bool Succeeded => Status == IgdbAssignmentStatus.Assigned;
}
