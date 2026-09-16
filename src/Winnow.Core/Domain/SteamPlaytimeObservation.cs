namespace Winnow.Core.Domain;

/// <summary>An immutable live Steam reading before household totals are combined.</summary>
public sealed record SteamPlaytimeObservation
{
    public long Id { get; init; }
    public required long OwnershipId { get; init; }
    public required string AccountRef { get; init; }
    public required string Source { get; init; }
    public long? PlaytimeMinutes { get; init; }
    public DateTime? LastPlayedAt { get; init; }
    public required DateTime ObservedAt { get; init; }
}
