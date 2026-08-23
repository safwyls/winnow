namespace Hoard.Core.Domain;

/// <summary>
/// Layer 3 of 4: this user's license for a <see cref="Release"/> on a store.
/// Timestamps are UTC.
/// </summary>
public sealed record Ownership
{
    public long Id { get; init; }
    public required long ReleaseId { get; init; }
    public required string Store { get; init; }
    public string? AccountRef { get; init; }
    public DateTime? AcquiredAt { get; init; }
    public string? LicenseType { get; init; }
    public long? PricePaidCents { get; init; }
    public string? PriceSource { get; init; }
    public string? InstallPath { get; init; }
    public bool Installed { get; init; }
}
