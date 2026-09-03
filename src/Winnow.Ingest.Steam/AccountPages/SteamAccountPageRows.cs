namespace Winnow.Ingest.Steam.AccountPages;

/// <summary>
/// Maps the free-text acquisition method from the licences page to a normalised
/// vocabulary value. Three methods were observed: "Steam Store", "Complimentary",
/// "Gift/Guest Pass". An unrecognised method is left null, counted, and never
/// mapped by guess. Verified 2026-08-29.
/// </summary>
public static class SteamLicenseTypes
{
    public const string SteamStore = "steam_store";
    public const string Complimentary = "complimentary";
    public const string Gift = "gift";
    public const string Retail = "retail";

    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Steam Store"] = SteamStore,
        ["Complimentary"] = Complimentary,
        ["Gift/Guest Pass"] = Gift,
        ["Gift"] = Gift,
        ["Guest Pass"] = Gift,
        ["Retail"] = Retail,
        ["Retail Key"] = Retail,
    };

    /// <summary>Returns the normalised licence type for a known acquisition method, or null for an unrecognised one.</summary>
    public static string? Map(string? acquisitionMethod)
        => acquisitionMethod is not null && Known.TryGetValue(acquisitionMethod, out var mapped) ? mapped : null;
}

/// <summary>
/// One row from the licences page (<c>store.steampowered.com/account/licenses/</c>).
///
/// <para>The licences page carries no appid for ordinary rows. A package id
/// exists only for free/Complimentary licences, extracted from a
/// <c>RemoveFreeLicense()</c> javascript href. Everything else is a name only,
/// and those names are PACKAGE names (bundles, DLC, cosmetics), not app names.
/// Matching downstream is therefore name-based and the match rate is modest by
/// construction.</para>
/// </summary>
public sealed record SteamLicenseRow
{
    /// <summary>One-based position in the table, for diagnostics.</summary>
    public required int RowIndex { get; init; }

    /// <summary>The package name as rendered, not an app name. Used for title-based matching.</summary>
    public required string ItemName { get; init; }

    /// <summary>When the licence was acquired, UTC midnight, or null when the date cell could not be parsed.</summary>
    public DateTime? AcquiredAtUtc { get; init; }

    /// <summary>The raw acquisition-method text from the page (e.g. "Steam Store", "Complimentary").</summary>
    public required string AcquisitionMethod { get; init; }

    /// <summary>The normalised licence type, or null when the acquisition method was not recognised.</summary>
    public string? LicenseType { get; init; }

    /// <summary>Steam package id extracted from a <c>RemoveFreeLicense()</c> href, or null for non-free rows.</summary>
    public string? PackageId { get; init; }

    /// <summary>Whether the acquisition method mapped to a known licence type.</summary>
    public bool HasKnownLicenseType => LicenseType is not null;
}

/// <summary>
/// Coarse classification of the payment-method cell on the purchase-history page.
///
/// <para>The cell renders the card issuer and last digits of the card. Winnow has
/// no use for either, so <see cref="Classify"/> keeps only the kind and discards
/// the text: nothing downstream can log a card fragment it never received.</para>
/// </summary>
public static class SteamPaymentKinds
{
    public const string Wallet = "wallet";
    public const string Card = "card";
    public const string PayPal = "paypal";
    public const string Other = "other";

    /// <summary>Returns a coarse kind (wallet, card, paypal, other) for the payment cell, or null when the cell is empty.</summary>
    public static string? Classify(string? paymentCellText)
    {
        var text = SteamPageValues.Collapse(paymentCellText);
        if (text.Length == 0)
        {
            return null;
        }

        if (text.Contains("Wallet", StringComparison.OrdinalIgnoreCase))
        {
            return Wallet;
        }

        if (text.Contains("PayPal", StringComparison.OrdinalIgnoreCase))
        {
            return PayPal;
        }

        // "<issuer> **<digits>" is how every card renders.
        return text.Contains("**", StringComparison.Ordinal) ? Card : Other;
    }
}

/// <summary>
/// The transaction-type strings the purchase-history parser reads from the type
/// cell. Only <see cref="Purchase"/> is eligible for price attribution; the
/// others are excluded for specific reasons documented on the importer.
/// </summary>
public static class SteamTransactionTypes
{
    public const string Purchase = "Purchase";
    public const string GiftPurchase = "Gift Purchase";
    public const string InGamePurchase = "In-Game Purchase";
    public const string Refund = "Refund";
}

/// <summary>
/// One row from the purchase-history page
/// (<c>store.steampowered.com/account/history/</c>).
///
/// <para>Appids appear only on in-game purchase rows, inside the onclick
/// help-wizard URL. Ordinary purchases use <c>HelpWithTransaction</c> with no
/// appid, so product identification is name-based for the rows the importer
/// actually writes prices from.</para>
///
/// <para>Two distinct refund signals exist and must not be conflated: a refunded
/// purchase carries <c>wht_item_refunded</c>/<c>wht_refunded</c> classes while
/// its type text still reads "Purchase"; separately, a "Refund" type row is the
/// reversal transaction.</para>
/// </summary>
public sealed record SteamPurchaseRow
{
    /// <summary>One-based position in the table, for diagnostics.</summary>
    public required int RowIndex { get; init; }

    /// <summary>When the purchase happened, UTC midnight, or null when the date cell could not be parsed.</summary>
    public DateTime? PurchasedAtUtc { get; init; }

    /// <summary>Product names from the items cell. A bundle is N items under one price.</summary>
    public required IReadOnlyList<string> Items { get; init; }

    /// <summary>Non-product text from the items cell (e.g. wallet redemption prose), or null for product rows.</summary>
    public string? Note { get; init; }

    /// <summary>The transaction type text (e.g. "Purchase", "Gift Purchase", "Refund").</summary>
    public required string TransactionType { get; init; }

    /// <summary>Coarse payment method from <see cref="SteamPaymentKinds.Classify"/>, or null.</summary>
    public string? PaymentKind { get; init; }

    /// <summary>The total amount paid. For a discounted item this is the discounted price.</summary>
    public SteamMoney? Total { get; init; }

    /// <summary>The base (or discounted) price from <c>wht_base_price</c>, or null.</summary>
    public SteamMoney? BasePrice { get; init; }

    /// <summary>The pre-discount price from <c>wht_original_price</c>, when a discount wrapper is present.</summary>
    public SteamMoney? OriginalPrice { get; init; }

    /// <summary>The discount percentage from <c>wht_discount_pct</c>, when present.</summary>
    public int? DiscountPercent { get; init; }

    /// <summary>Wallet balance change, or null.</summary>
    public SteamMoney? WalletChange { get; init; }

    /// <summary>Whether this row is marked as refunded via <c>wht_item_refunded</c> or <c>wht_refunded</c> classes.</summary>
    public bool Refunded { get; init; }

    /// <summary>Appid from the onclick help-wizard URL, present only on in-game purchase rows.</summary>
    public string? AppId { get; init; }

    /// <summary>Whether this row covers more than one product. A bundle row's price is never split across its items.</summary>
    public bool IsMultiItem => Items.Count > 1;

    /// <summary>Whether this row has at least one product name, as opposed to a wallet movement or a note-only row.</summary>
    public bool IsProductRow => Items.Count > 0;
}
