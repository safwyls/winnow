namespace Winnow.Core.Domain;

/// <summary>
/// Which page-capture source a fact came from. Only Steam writes today; the
/// column exists so a second storefront's import does not have to migrate the
/// table.
/// </summary>
public static class AccountFactSources
{
    public const string Steam = "steam";
}

/// <summary>
/// Winnow's normalised vocabulary over the purchase-history page's free-text
/// type cell. <c>other</c> is the counted, never-guessed-at escape hatch for
/// transaction shapes Steam has not shown yet.
/// </summary>
public static class AccountTransactionKinds
{
    public const string Purchase = "purchase";
    public const string GiftPurchase = "gift_purchase";
    public const string InGamePurchase = "in_game_purchase";
    public const string Refund = "refund";

    /// <summary>
    /// Wallet credit the user PAID for. The row carries a total because money
    /// left the user's payment method.
    /// </summary>
    public const string WalletCreditPurchase = "wallet_credit_purchase";

    /// <summary>
    /// Wallet credit added by a redeemed code. The row carries only a balance
    /// change because what that code cost is not on this page.
    /// </summary>
    public const string WalletCreditRedemption = "wallet_credit_redemption";

    public const string Other = "other";

    /// <summary>
    /// The three kinds that represent money paid for a product. Every spend
    /// figure is computed over this subset.
    /// </summary>
    public static readonly IReadOnlyList<string> ProductSpend =
        [Purchase, GiftPurchase, InGamePurchase];
}

/// <summary>
/// One row of the purchase-history page as reported. This is a page-capture
/// fact, not an entity: identity is the whole reported content, and a later
/// capture re-reporting the same fact is a no-op (migration 0014).
/// </summary>
public sealed record AccountTransactionFact
{
    public long Id { get; init; }

    public required string Source { get; init; }

    public required string Kind { get; init; }

    /// <summary>The page's own free-text type cell, before normalisation to <see cref="Kind"/>.</summary>
    public required string TransactionTypeRaw { get; init; }

    public DateTime? OccurredAt { get; init; }

    /// <summary>
    /// Product names on the row. A bundle is N items under ONE price and the
    /// price is never split across them (§4.7).
    /// </summary>
    public required IReadOnlyList<string> ItemNames { get; init; }

    public string? Note { get; init; }

    /// <summary>As-displayed cents in the page's own currency, never converted.</summary>
    public long? TotalCents { get; init; }

    /// <summary>
    /// Present only on rows that rendered a discount wrapper. As-displayed
    /// cents, never converted.
    /// </summary>
    public long? ListPriceCents { get; init; }

    public int? DiscountPercent { get; init; }

    public long? WalletChangeCents { get; init; }

    /// <summary>The currency symbol as the page rendered it.</summary>
    public string? CurrencySymbol { get; init; }

    public string? PaymentKind { get; init; }

    /// <summary>
    /// The flag Steam puts on the ORIGINAL purchase row. This is a different
    /// signal from a <c>refund</c>-kind row, which is the separate reversal
    /// transaction; conflating them double-counts.
    /// </summary>
    public bool Refunded { get; init; }

    /// <summary>Presence only, never identity. No recipient name, persona or URL is stored.</summary>
    public bool GiftRecipientPresent { get; init; }

    public string? AppId { get; init; }

    /// <summary>The capture that first reported this fact.</summary>
    public required DateTime CapturedAt { get; init; }
}

/// <summary>
/// One row of the licences page as reported. <see cref="ItemName"/> is a
/// PACKAGE name (bundles, DLC, cosmetics), not an app name, so a licence count
/// is not a library size.
/// </summary>
public sealed record AccountLicenseFact
{
    public long Id { get; init; }

    public required string Source { get; init; }

    public required string ItemName { get; init; }

    public DateTime? AcquiredAt { get; init; }

    /// <summary>
    /// Null when the page's acquisition method text is not one the parser
    /// recognises. Counted, never mapped by guess.
    /// </summary>
    public string? AcquisitionKind { get; init; }

    public required string AcquisitionMethodRaw { get; init; }

    /// <summary>
    /// Present only for free/Complimentary licences, which are the only rows
    /// carrying a RemoveFreeLicense link.
    /// </summary>
    public string? PackageId { get; init; }

    /// <summary>The capture that first reported this fact.</summary>
    public required DateTime CapturedAt { get; init; }
}
