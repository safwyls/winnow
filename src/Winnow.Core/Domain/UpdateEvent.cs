namespace Winnow.Core.Domain;

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

    /// <summary>
    /// Where a human reads this update. Set for announcements from the news
    /// item's own <c>url</c>; null for build pushes, which have no page.
    ///
    /// <para>Captured at detection time because it is not cheaply recoverable
    /// later — the news endpoint pages backwards by date with no lookup by id
    /// (docs/spikes/update-signals.md §3). design-system.md §5.2's badge click
    /// opens this.</para>
    /// </summary>
    public string? Url { get; init; }

    public string? RawJson { get; init; }
}
