namespace Winnow.Core.Domain;

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

    /// <summary>How this session was attributed to this game (<see cref="SessionAttributions"/>). Null means "not recorded".</summary>
    public string? AttributedBy { get; init; }

    /// <summary>Stable idempotency key for a monitored sitting. Null on manual and legacy sessions.</summary>
    public string? MonitorKey { get; init; }
}
