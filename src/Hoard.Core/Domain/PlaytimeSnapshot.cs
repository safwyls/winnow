namespace Hoard.Core.Domain;

/// <summary>
/// Longitudinal playtime history that storefronts discard: one cumulative
/// playtime reading per ownership per observation. Timestamps are UTC.
/// </summary>
public sealed record PlaytimeSnapshot
{
    public long Id { get; init; }
    public required long OwnershipId { get; init; }
    public required long PlaytimeMinutes { get; init; }
    public required DateTime ObservedAt { get; init; }
}
