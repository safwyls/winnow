namespace Winnow.Core.Domain;

/// <summary>
/// A live user-pinned IGDB mapping, projected from the rows of
/// <c>work_igdb_pins</c> (migration 0026) whose <c>cleared_at</c> is
/// null. At most one live pin exists per work.
/// </summary>
public sealed record WorkIgdbPin
{
    public required long WorkId { get; init; }

    public required long IgdbId { get; init; }

    public required DateTime PinnedAt { get; init; }
}

/// <summary>
/// The metadata to write when pinning a work to a chosen IGDB game.
/// Null on any field means "the chosen IGDB entry has nothing here" and
/// is written as NULL, not skipped. This is deliberate: the stored
/// values belonged to the game the resolver got wrong, so keeping them
/// would leave a row that is half one game and half another.
/// </summary>
public sealed record WorkIgdbPinAssignment
{
    public required long WorkId { get; init; }

    public required long IgdbId { get; init; }

    /// <summary>Mapping revision captured before loading the chosen record; null uses current intent.</summary>
    public long? ExpectedIgdbMappingRevision { get; init; }

    /// <summary>
    /// COALESCE'd over blank rather than written unconditionally, because
    /// <c>works.name</c> is NOT NULL and a game must keep a title.
    /// </summary>
    public string? Name { get; init; }

    public int? FirstReleaseYear { get; init; }

    public string? Summary { get; init; }

    public string? CoverUrl { get; init; }

    public string? Publisher { get; init; }

    public string? IgdbGameType { get; init; }

    public long? IgdbParentId { get; init; }

    public long? IgdbVersionParentId { get; init; }
}

/// <summary>
/// The outcome of a pin attempt.
/// </summary>
public enum WorkIgdbPinOutcome
{
    /// <summary>The pin was recorded and the work's metadata was rewritten.</summary>
    Pinned,

    /// <summary>No work with that id exists. Nothing was written.</summary>
    WorkNotFound,

    /// <summary>
    /// Another work already holds that <c>igdb_id</c> in <c>works</c>.
    /// The column is UNIQUE, so this is the same constraint that stops
    /// both halves of a cross-store duplicate holding one id. The caller
    /// must treat this as a real state the UI renders, not a defensive
    /// check.
    /// </summary>
    IgdbIdClaimedByAnotherWork,
}
