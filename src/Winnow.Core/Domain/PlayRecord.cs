namespace Winnow.Core.Domain;

/// <summary>
/// Layer 4 of 4: an observation of cumulative playtime for an
/// <see cref="Ownership"/>, as reported by a source at a point in time.
/// Timestamps are UTC.
/// </summary>
public sealed record PlayRecord
{
    public long Id { get; init; }
    public required long OwnershipId { get; init; }
    public required long PlaytimeMinutes { get; init; }
    public DateTime? LastPlayedAt { get; init; }
    public required string Source { get; init; }
    public required DateTime ObservedAt { get; init; }
}
