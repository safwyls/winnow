namespace Hoard.Core.Domain;

/// <summary>
/// A raw update signal for a <see cref="Release"/>. Both signal kinds
/// (<see cref="UpdateEventKinds"/>) are stored raw so the "major update"
/// heuristic can be retuned without re-fetching. Timestamps are UTC.
/// </summary>
public sealed record UpdateEvent
{
    public long Id { get; init; }
    public required long ReleaseId { get; init; }
    public required string Kind { get; init; }
    public string? BuildId { get; init; }
    public required DateTime OccurredAt { get; init; }
    public string? Title { get; init; }
    public string? RawJson { get; init; }
}
