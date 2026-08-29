namespace Winnow.Core.Domain;

/// <summary>
/// A fill-only write to an existing ownership row. Every assignment is
/// COALESCE(stored, incoming): a column that already holds a value keeps it,
/// because the account pages are one source among several and newest is not best.
///
/// <para><see cref="PriceSource"/> moves with <see cref="PricePaidCents"/>
/// rather than being COALESCEd on its own, because a source label describing a
/// price that came from somewhere else is worse than no label.</para>
/// </summary>
/// <param name="OwnershipId">The ownership row to fill.</param>
/// <param name="AcquiredAt">When the licence was acquired, or null to leave the stored value.</param>
/// <param name="LicenseType">Normalised licence type, or null to leave the stored value.</param>
/// <param name="PricePaidCents">Price paid in cents, or null to leave the stored value.</param>
/// <param name="PriceSource">Where the price came from. Written only when <paramref name="PricePaidCents"/> is non-null.</param>
public sealed record OwnershipAcquisitionFill(
    long OwnershipId,
    DateTime? AcquiredAt,
    string? LicenseType,
    long? PricePaidCents,
    string? PriceSource)
{
    /// <summary>Whether at least one column carries a value to offer.</summary>
    public bool HasAnythingToWrite =>
        AcquiredAt is not null || LicenseType is not null || PricePaidCents is not null;
}

/// <summary>Labels identifying where a price came from, stored alongside the price so a value can always be traced to its origin.</summary>
public static class PriceSources
{
    /// <summary>Price derived from the Steam account purchase-history page.</summary>
    public const string SteamAccountHistory = "steam_account_history";
}
