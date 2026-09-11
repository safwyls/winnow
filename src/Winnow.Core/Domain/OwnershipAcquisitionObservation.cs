namespace Winnow.Core.Domain;

/// <summary>Acquisition evidence for one captured account; null is an explicitly unknown account.</summary>
public sealed record OwnershipAcquisitionObservation
{
    public long Id { get; init; }
    public required long OwnershipId { get; init; }
    public string? AccountRef { get; init; }
    public DateTime? AcquiredAt { get; init; }
    public string? LicenseType { get; init; }
    public long? PricePaidCents { get; init; }
    public string? PriceSource { get; init; }
    public required string Source { get; init; }
    public required DateTime CapturedAt { get; init; }
}
