namespace Winnow.Core.Queries;

/// <summary>A count and a total for one transaction kind or cross-cut.</summary>
public sealed record AccountSpendSlice(int Count, long Cents)
{
    public static readonly AccountSpendSlice Empty = new(0, 0);
}

/// <summary>Net spend for one calendar year, from the page's own display date.</summary>
public sealed record AccountSpendYear(int Year, int TransactionCount, long Cents);

/// <summary>One currency symbol observed in the capture, with a transaction count.</summary>
public sealed record AccountCurrencyUse(string Symbol, int TransactionCount);

/// <summary>
/// Count of licences by the page's own acquisition vocabulary. A null
/// <see cref="Kind"/> groups methods the parser does not recognise.
/// </summary>
public sealed record AccountLicenseAcquisition(string? Kind, int Count);

/// <summary>
/// The largest single TRANSACTION, not the largest price paid for a game. A
/// bundle row is one transaction covering N items; <see cref="IsBundle"/>
/// flags it.
/// </summary>
public sealed record AccountBiggestPurchase
{
    public required long Cents { get; init; }

    public DateTime? OccurredAt { get; init; }

    public required IReadOnlyList<string> ItemNames { get; init; }

    public required int ItemCount { get; init; }

    public string? CurrencySymbol { get; init; }

    public bool IsBundle => ItemCount > 1;
}

/// <summary>
/// Read model for the account stats surface. Every figure is a QUERY, never a
/// stored aggregate, following the same discipline as §6.1's derived buckets
/// and for the same reason: what counts as "spend" gets retuned, and stored
/// values rot.
///
/// <para>Three caveats apply to every member:</para>
/// <list type="number">
/// <item><description>Every amount is as-displayed cents from one locale
/// sample. Nothing is converted; <see cref="IsSingleCurrency"/> is how a
/// caller checks the totals are not a mix.</description></item>
/// <item><description>A captured page may be a partial view of the account.
/// The licences page paginates and the history page load-mores. Every figure
/// is "of what was captured", not "of the account".</description></item>
/// <item><description>Third-party keys (Humble, Fanatical) never appear in
/// Steam's spending data at all, which per §4.7 is exactly the population
/// with the largest libraries. Nothing here can see that
/// money.</description></item>
/// </list>
/// </summary>
public sealed record AccountStats
{
    public required string Source { get; init; }

    public int TransactionCount { get; init; }

    public int LicenseCount { get; init; }

    /// <summary>
    /// Every purchase, gift purchase and in-game purchase the capture
    /// reported, refunded ones included.
    /// </summary>
    public long GrossProductSpendCents { get; init; }

    /// <inheritdoc cref="GrossProductSpendCents"/>
    public int GrossProductTransactionCount { get; init; }

    /// <summary>The subset Steam flagged reversed on the original purchase row.</summary>
    public long RefundedProductSpendCents { get; init; }

    /// <inheritdoc cref="RefundedProductSpendCents"/>
    public int RefundedProductTransactionCount { get; init; }

    /// <summary>Gross minus refunded. Not stored.</summary>
    public long NetProductSpendCents => GrossProductSpendCents - RefundedProductSpendCents;

    /// <inheritdoc cref="NetProductSpendCents"/>
    public int NetProductTransactionCount =>
        GrossProductTransactionCount - RefundedProductTransactionCount;

    /// <summary>
    /// Net spend grouped by the page's own display date, which is
    /// day-resolution and rendered in the account's locale. A row whose date
    /// the parser declined lands in <see cref="UndatedNetSpendCents"/>
    /// instead of being guessed into a year.
    /// </summary>
    public IReadOnlyList<AccountSpendYear> SpendByYear { get; init; } = [];

    /// <summary>Net spend on rows the parser could not date.</summary>
    public long UndatedNetSpendCents { get; init; }

    /// <inheritdoc cref="UndatedNetSpendCents"/>
    public int UndatedNetTransactionCount { get; init; }

    /// <summary>Single-item, non-refunded purchases. The core spend slice.</summary>
    public AccountSpendSlice Purchases { get; init; } = AccountSpendSlice.Empty;

    /// <summary>
    /// Gifts GIVEN, not received. Their value is money the user spent on
    /// somebody else, refunded rows excluded.
    /// </summary>
    public AccountSpendSlice GiftPurchases { get; init; } = AccountSpendSlice.Empty;

    /// <summary>
    /// Money spent inside a game, refunded rows excluded. Attributable to an
    /// app only on rows that carried an appid.
    /// </summary>
    public AccountSpendSlice InGamePurchases { get; init; } = AccountSpendSlice.Empty;

    /// <summary>
    /// Standalone reversal rows. A refund and the purchase it reverses are two
    /// rows on the page, and only one capture may contain both. Reported
    /// beside the refunded-purchase figure and never subtracted a second time.
    /// </summary>
    public AccountSpendSlice RefundTransactions { get; init; } = AccountSpendSlice.Empty;

    /// <summary>
    /// Money reaches Steam either as a direct payment for a product or as a
    /// wallet top-up that later pays for products. Counting both would count
    /// the same money twice, so wallet credit is reported here and NEVER
    /// added into the spend figures.
    /// </summary>
    public AccountSpendSlice WalletCreditPurchases { get; init; } = AccountSpendSlice.Empty;

    /// <summary>
    /// Credit added by a redeemed code. What the code cost is not on this
    /// page at all.
    /// </summary>
    public AccountSpendSlice WalletCreditRedemptions { get; init; } = AccountSpendSlice.Empty;

    /// <summary>
    /// The total is a real fact; the per-item split is not, and is never
    /// computed (§4.7). Divide-by-N and market-weighted split are both
    /// defensible and both wrong.
    /// </summary>
    public AccountSpendSlice BundlePurchases { get; init; } = AccountSpendSlice.Empty;

    /// <summary>
    /// Only rows that rendered a discount wrapper carry a list price, so this
    /// is emphatically NOT "total savings". Most purchases carry no list price
    /// at all.
    /// </summary>
    public AccountSpendSlice DiscountedPurchases { get; init; } = AccountSpendSlice.Empty;

    /// <summary>Sum of list prices on the discounted rows. See <see cref="DiscountedPurchases"/>.</summary>
    public long DiscountedPurchaseListCents { get; init; }

    /// <summary>
    /// The largest single TRANSACTION. A bundle row is one transaction
    /// covering N items; see <see cref="AccountBiggestPurchase.IsBundle"/>.
    /// </summary>
    public AccountBiggestPurchase? BiggestPurchase { get; init; }

    /// <summary>The span the capture covers, so a caller can see it is looking at a slice.</summary>
    public DateTime? FirstTransactionAt { get; init; }

    /// <inheritdoc cref="FirstTransactionAt"/>
    public DateTime? LastTransactionAt { get; init; }

    public int TransactionsWithoutDate { get; init; }

    /// <summary>
    /// Currency symbols observed, so a caller can tell whether summing was
    /// meaningful. No conversion is attempted anywhere.
    /// </summary>
    public IReadOnlyList<AccountCurrencyUse> Currencies { get; init; } = [];

    /// <summary>Transactions that carried no currency symbol at all.</summary>
    public int TransactionsWithoutCurrency { get; init; }

    /// <summary>True when all transactions share one currency (or none have one).</summary>
    public bool IsSingleCurrency => Currencies.Count <= 1 && TransactionsWithoutCurrency == 0;

    /// <summary>
    /// Counts by the licences page's own acquisition vocabulary (Steam Store,
    /// Complimentary, Gift/Guest Pass), with a null Kind for methods the
    /// parser does not recognise. These count PACKAGES, not games.
    /// </summary>
    public IReadOnlyList<AccountLicenseAcquisition> LicenseAcquisitions { get; init; } = [];

    public int LicensesWithUnrecognizedMethod { get; init; }

    public int LicensesWithoutDate { get; init; }

    /// <summary>The span the licence capture covers.</summary>
    public DateTime? FirstLicenseAt { get; init; }

    /// <inheritdoc cref="FirstLicenseAt"/>
    public DateTime? LastLicenseAt { get; init; }

    public bool HasAnything => TransactionCount > 0 || LicenseCount > 0;

    public static AccountStats Empty(string source) => new() { Source = source };
}
