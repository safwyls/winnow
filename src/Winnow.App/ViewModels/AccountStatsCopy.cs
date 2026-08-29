namespace Winnow.App.ViewModels;

/// <summary>
/// User-facing copy for the Steam account stats screen. All strings in one
/// file so the honesty caveats and framing can be reviewed together. Every
/// figure is computed from captured pages, never from the account's lifetime,
/// and the copy must never let a number read as account-lifetime truth.
/// </summary>
public static class AccountStatsCopy
{
    // ══ Rail and header ═══════════════════════════════════════════════════

    /// <summary>Rail row label. Uppercase, matching the other Display S caps
    /// rows in the rail.</summary>
    public const string RailRow = "STATS";

    /// <summary>Rail row tooltip. Same register as the other rail tooltips,
    /// no trailing period.</summary>
    public const string RailTooltip =
        "Spending totals and licence counts from the account pages Winnow has read";

    /// <summary>Screen title, rendered in Display L. Sentence case, matching
    /// the settings surface's screen titles. Names the user's Steam account,
    /// not their game library.</summary>
    public const string Title = "Steam account";

    /// <summary>Introduction under the title. States the source (captured
    /// pages) and the partiality caveat that governs the entire screen:
    /// these are totals of what was read, not of the account.</summary>
    public const string Intro =
        "Every figure here is computed from the Steam account pages Winnow has "
        + "read. A capture can be partial, so these are totals of what was read, "
        + "not of the account.";

    // ══ Empty state ═══════════════════════════════════════════════════════

    /// <summary>Shown when no transactions and no licences have ever been
    /// imported. A direction, not a mood: points the user at the PURCHASES
    /// screen (§7: empty states are directions).</summary>
    public const string EmptyMessage =
        "No transactions or licences have been imported. Import them from the "
        + "PURCHASES screen, which reads your Steam account pages.";

    // ══ Mixed currency ════════════════════════════════════════════════════

    /// <summary>Heading for the mixed-currency notice. Sentence case.</summary>
    public const string MixedCurrencyHeading = "Mixed currencies";

    /// <summary>Shown when the capture holds transactions in more than one
    /// currency or transactions with no currency symbol. Explains that
    /// money totals are withheld and only counts are shown, per
    /// <see cref="Core.Queries.AccountStats.IsSingleCurrency"/>.</summary>
    public const string MixedCurrencyMessage =
        "The capture holds transactions in more than one currency, or "
        + "transactions with no currency symbol at all. Winnow does not add "
        + "different currencies together or convert between them, so every money "
        + "total on this screen is withheld and only counts are shown. The "
        + "per-currency table below lists which symbols appeared and on how many "
        + "transactions.";

    // ══ Spend ═════════════════════════════════════════════════════════════

    /// <summary>Heading for the product-spend card. Sentence case.</summary>
    public const string SpendHeading = "Spend";

    /// <summary>Defines what counts as spend and what does not. Wallet
    /// top-ups are excluded and reported in their own section.</summary>
    public const string SpendNote =
        "Spend is money paid for a product: ordinary purchases, gifts bought "
        + "for other people, and purchases made inside a game. Wallet top-ups "
        + "are not spend; they are reported separately below.";

    /// <summary>Row label for net spend (gross minus refunded). Sentence
    /// case, sits left of a number.</summary>
    public const string LabelNetSpend = "Net spend";

    /// <summary>Row label for gross spend: every product transaction read,
    /// refunded ones included. Sentence case, left of a number.</summary>
    public const string LabelGrossSpend = "Gross spend";

    /// <summary>Row label for the refunded subset: purchases Steam flagged
    /// as reversed on the original purchase row. Sentence case, left of a
    /// number.</summary>
    public const string LabelRefundedSpend = "Refunded";

    // ══ Spend by year ═════════════════════════════════════════════════════

    /// <summary>Heading for the per-year breakdown. Sentence case.</summary>
    public const string YearHeading = "Spend by year";

    /// <summary>States that the year comes from the page's own display date
    /// (day resolution) and that undated rows are listed separately rather
    /// than guessed into a year.</summary>
    public const string YearNote =
        "The year comes from the date the page displayed, which is day "
        + "resolution. Rows the parser could not date are listed separately "
        + "and are never guessed into a year.";

    /// <summary>Row label for net spend on undated rows. Short, sits in the
    /// year column where a year would normally appear.</summary>
    public const string LabelUndatedYear = "No date on page";

    // ══ Where the money went ══════════════════════════════════════════════

    /// <summary>Heading for the spend-by-kind breakdown. Sentence case.</summary>
    public const string KindHeading = "Where the money went";

    /// <summary>Row label for single-item, non-refunded purchases. Sentence
    /// case, left of a number.</summary>
    public const string LabelPurchases = "Purchases";

    /// <summary>Row label for gifts the user bought for somebody else. Never
    /// gifts received. Sentence case, left of a number.</summary>
    public const string LabelGiftsGiven = "Gifts bought for others";

    /// <summary>Row label for money spent inside a game rather than on one.
    /// Sentence case, left of a number.</summary>
    public const string LabelInGamePurchases = "In-game purchases";

    /// <summary>States that Winnow records the existence of a gift
    /// recipient, never the identity. This is a design decision: no name,
    /// persona or profile link is read from the page.</summary>
    public const string GiftsNote =
        "Winnow records that a gift had a recipient, not who the recipient "
        + "was. No name, persona or profile link is read from the page.";

    // ══ Refunds ═══════════════════════════════════════════════════════════

    /// <summary>Heading for the refunds card. Sentence case.</summary>
    public const string RefundHeading = "Refunds";

    /// <summary>Row label for purchase rows Steam flagged reversed. This is
    /// the same figure as <see cref="LabelRefundedSpend"/>, shown here
    /// beside its partner. Sentence case, left of a number.</summary>
    public const string LabelRefundedPurchases = "Refunded purchases";

    /// <summary>Row label for standalone reversal rows, which are separate
    /// rows on the page from the purchase they reverse. Sentence case, left
    /// of a number.</summary>
    public const string LabelRefundTransactions = "Refund transactions";

    /// <summary>Explains that a refund and its purchase are two rows, one
    /// capture may hold either or both, and the two figures should not be
    /// added together because reversal rows are never subtracted a second
    /// time.</summary>
    public const string RefundNote =
        "A refund and the purchase it reverses are two different rows on the "
        + "page, and a capture may hold either or both. The two figures are "
        + "reported side by side; reversal rows are never subtracted a second "
        + "time, so these two numbers should not be added together.";

    // ══ Bundles ═══════════════════════════════════════════════════════════

    /// <summary>Heading for the bundles card. Sentence case.</summary>
    public const string BundleHeading = "Bundles";

    /// <summary>Row label for purchases covering more than one item under a
    /// single price. Sentence case, left of a number.</summary>
    public const string LabelBundlePurchases = "Bundle purchases";

    /// <summary>States that the bundle total is a real fact but the per-game
    /// split is not and is never computed. Per
    /// <see cref="Core.Queries.AccountStats.BundlePurchases"/> and
    /// §4.7.</summary>
    public const string BundleNote =
        "A bundle's total price is a real fact; what each game inside it cost "
        + "is not. Dividing by the number of items and weighting by market "
        + "price are both defensible and both wrong, so no per-game price is "
        + "shown.";

    // ══ Discounts ═════════════════════════════════════════════════════════

    /// <summary>Heading for the discounts card. Sentence case.</summary>
    public const string DiscountHeading = "Discounts";

    /// <summary>Row label for purchases whose row rendered a discount.
    /// Sentence case, left of a number.</summary>
    public const string LabelDiscountedPurchases = "Discounted purchases";

    /// <summary>Row label for the sum of list prices on discounted rows.
    /// Sentence case, left of a number.</summary>
    public const string LabelDiscountListPrice = "List price total";

    /// <summary>THE LOAD-BEARING CAVEAT. Only rows that rendered a discount
    /// carry a list price; most carry none. This figure is emphatically not
    /// total savings. Per
    /// <see cref="Core.Queries.AccountStats.DiscountedPurchases"/>.</summary>
    public const string DiscountNote =
        "Only rows that rendered a discount carry a list price at all, and "
        + "most purchases carry none. This is the difference on the rows that "
        + "happened to show a before price. It is not a total-savings figure.";

    // ══ Biggest transaction ═══════════════════════════════════════════════

    /// <summary>Heading for the biggest-transaction card. Sentence
    /// case.</summary>
    public const string BiggestHeading = "Biggest transaction";

    /// <summary>States that this is the largest single transaction by price,
    /// not the most ever paid for one game. Per
    /// <see cref="Core.Queries.AccountBiggestPurchase"/>.</summary>
    public const string BiggestNote =
        "This is the largest single transaction by price, not the most ever "
        + "paid for one game. A bundle is one transaction covering several "
        + "items.";

    /// <summary>Shown only when the biggest transaction is a bundle. States
    /// that it covered several items under one price and that the price is
    /// not split between them.</summary>
    public const string BiggestIsBundleNote =
        "This transaction covered several items under one price. The price is "
        + "not split between them.";

    // ══ Wallet ════════════════════════════════════════════════════════════

    /// <summary>Heading for the wallet card. Sentence case.</summary>
    public const string WalletHeading = "Wallet";

    /// <summary>Row label for wallet credit the user paid for. Sentence
    /// case, left of a number.</summary>
    public const string LabelWalletCreditBought = "Wallet credit purchased";

    /// <summary>Row label for credit added by redeeming a code. Sentence
    /// case, left of a number.</summary>
    public const string LabelWalletCreditRedeemed = "Wallet credit redeemed";

    /// <summary>Explains the double-counting risk: wallet top-ups and
    /// direct product payments can cover the same money, so wallet credit
    /// is never part of the spend figures. Per
    /// <see cref="Core.Queries.AccountStats.WalletCreditPurchases"/>.</summary>
    public const string WalletNote =
        "Money reaches Steam either as a direct payment for a product or as a "
        + "wallet top-up that later pays for products. Counting both would "
        + "count the same money twice, so wallet credit is reported here as "
        + "its own fact and is never part of the spend figures above. What a "
        + "redeemed code cost is not on the page.";

    // ══ Licences ══════════════════════════════════════════════════════════

    /// <summary>Heading for the licences card. Sentence case. British
    /// spelling per codebase convention.</summary>
    public const string LicenceHeading = "Licences";

    /// <summary>States that these count packages, not games, so a licence
    /// count is not a library size. The breakdown uses the licenses page's
    /// own acquisition vocabulary.</summary>
    public const string LicenceNote =
        "These count packages, not games: a package can be a bundle, a DLC, "
        + "or a cosmetic, so a licence count is not a library size. The "
        + "breakdown uses the licenses page's own vocabulary for how each "
        + "package was acquired.";

    /// <summary>Row label for licences acquired through the Steam Store.
    /// Sentence case, left of a number.</summary>
    public const string LabelLicenceSteamStore = "Steam Store";

    /// <summary>Row label for free or complimentary licences. Sentence case,
    /// left of a number.</summary>
    public const string LabelLicenceComplimentary = "Complimentary";

    /// <summary>Row label for licences acquired as a gift or guest pass.
    /// Sentence case, left of a number.</summary>
    public const string LabelLicenceGift = "Gift or guest pass";

    /// <summary>Row label for licences activated with a retail key. Sentence
    /// case, left of a number.</summary>
    public const string LabelLicenceRetail = "Retail key";

    /// <summary>Row label for licences whose acquisition method text the
    /// parser does not recognise. Counted, never guessed at. Sentence case,
    /// left of a number.</summary>
    public const string LabelLicenceUnrecognised = "Unrecognised";

    // ══ Currencies ════════════════════════════════════════════════════════

    /// <summary>Heading for the per-currency table. Sentence case.</summary>
    public const string CurrencyHeading = "Currencies";

    /// <summary>States that the table lists which currency symbols appeared
    /// and on how many transactions, and that nothing is converted.</summary>
    public const string CurrencyNote =
        "Which currency symbols appeared and on how many transactions. Every "
        + "amount is stored exactly as the page displayed it; nothing is "
        + "converted.";

    /// <summary>Row label for transactions that carried no currency symbol
    /// at all. Sentence case, left of a number.</summary>
    public const string LabelNoCurrencySymbol = "No currency symbol";

    // ══ What this was read from ═══════════════════════════════════════════

    /// <summary>Heading for the capture-framing card. Sentence case. This
    /// card frames the entire screen as a captured slice.</summary>
    public const string CaptureHeading = "What this was read from";

    /// <summary>States that the dates and counts say which stretch the
    /// capture covers, and that importing more pages extends it. Points at
    /// the PURCHASES screen as the source.</summary>
    public const string CaptureNote =
        "The dates and counts below say which stretch of the account the "
        + "capture covers. Importing more pages from the PURCHASES screen "
        + "extends it.";

    /// <summary>Row label for total transactions read. Sentence case, left
    /// of a number.</summary>
    public const string LabelTransactionsRead = "Transactions read";

    /// <summary>Row label for the earliest transaction date in the capture.
    /// Sentence case, left of a date.</summary>
    public const string LabelFirstTransaction = "First transaction";

    /// <summary>Row label for the latest transaction date in the capture.
    /// Sentence case, left of a date.</summary>
    public const string LabelLastTransaction = "Last transaction";

    /// <summary>Row label for transactions the parser could not date.
    /// Sentence case, left of a count.</summary>
    public const string LabelTransactionsWithoutDate = "Transactions without date";

    /// <summary>Row label for total licences read. Sentence case, left of a
    /// count.</summary>
    public const string LabelLicencesRead = "Licences read";

    /// <summary>Row label for the earliest licence date in the capture.
    /// Sentence case, left of a date.</summary>
    public const string LabelFirstLicence = "First licence";

    /// <summary>Row label for the latest licence date in the capture.
    /// Sentence case, left of a date.</summary>
    public const string LabelLastLicence = "Last licence";

    /// <summary>Row label for licences the parser could not date. Sentence
    /// case, left of a count.</summary>
    public const string LabelLicencesWithoutDate = "Licences without date";

    /// <summary>States that third-party keys (Humble, Fanatical and others)
    /// are activated on Steam but never appear in Steam's spending pages, so
    /// that money is invisible here. Neutral statement of a limit, not an
    /// apology.</summary>
    public const string ThirdPartyKeysNote =
        "Keys bought from third-party sellers, such as Humble Bundle and "
        + "Fanatical, are activated on Steam but never appear in Steam's own "
        + "spending pages. That money is not visible to any figure here.";
}
