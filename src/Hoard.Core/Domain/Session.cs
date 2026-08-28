namespace Hoard.Core.Domain;

/// <summary>
/// A single detected play session. DetectionMethod values come from
/// <see cref="DetectionMethods"/>. Timestamps are UTC.
/// </summary>
public sealed record Session
{
    public long Id { get; init; }
    public required long OwnershipId { get; init; }
    public required DateTime StartedAt { get; init; }
    public DateTime? EndedAt { get; init; }
    public long? DurationSeconds { get; init; }
    public required string DetectionMethod { get; init; }

    /// <summary>
    /// <b>How this session came to be about THIS game</b> — see
    /// <see cref="SessionAttributions"/>. Orthogonal to
    /// <see cref="DetectionMethod"/>, which says how the start and end times
    /// were measured, and deliberately a separate column rather than a fifth
    /// detection method: a session Hoard launched is still timed by the process
    /// watcher, so calling it anything other than <c>process_watch</c> would be
    /// a claim about its timestamps that is not true.
    ///
    /// <para>Null is a real answer and means "not recorded" — every row written
    /// before M3b has it, and folding that into "inferred" would be inventing a
    /// fact about history. Three-valued for the same reason
    /// <c>ownerships.installed</c> is.</para>
    /// </summary>
    public string? AttributedBy { get; init; }
}
