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

    /// <summary>Rail row tooltip. No trailing period.</summary>
    public const string RailTooltip =
        "Spending and licence totals from Steam";

    /// <summary>Screen title, rendered in Display L. Sentence case, matching
    /// the settings surface's screen titles. Names the user's Steam account,
    /// not their game library.</summary>
    public const string Title = "Steam account";

    /// <summary>Introduction under the title. Totals of what was read, not
    /// of the account.</summary>
    public const string Intro =
        "Totals of the Steam account pages that were read, not of the whole account.";

    // ══ Empty state ═══════════════════════════════════════════════════════

    /// <summary>Shown when nothing has been imported. Points the user at
    /// the PURCHASES screen.</summary>
    public const string EmptyMessage =
        "Nothing imported yet. Import from the PURCHASES screen.";

    // ══ Mixed currency ════════════════════════════════════════════════════

    /// <summary>Heading for the mixed-currency notice. Sentence case.</summary>
    public const string MixedCurrencyHeading = "Mixed currencies";

    /// <summary>Shown when currency totals cannot be combined.</summary>
    public const string MixedCurrencyMessage =
        "Money totals are withheld. Only counts are shown.";

    // ══ Spend ═════════════════════════════════════════════════════════════

    /// <summary>Heading for the product-spend card. Sentence case.</summary>
    public const string SpendHeading = "Spend";

    /// <summary>What counts as spend. Wallet top-ups are excluded.</summary>
    public const string SpendNote =
        "Purchases, gifts given and in-game purchases. Wallet top-ups are not spend.";

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

    /// <summary>Undated rows are listed separately, never guessed into
    /// a year.</summary>
    public const string YearNote =
        "Undated rows are listed separately and never guessed into a year.";

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

    /// <summary>Recipients are counted, never named.</summary>
    public const string GiftsNote =
        "Recipients are counted, never named.";

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

    /// <summary>The two figures overlap and must not be added
    /// together.</summary>
    public const string RefundNote =
        "These two figures overlap. Do not add them together.";

    // ══ Bundles ═══════════════════════════════════════════════════════════

    /// <summary>Heading for the bundles card. Sentence case.</summary>
    public const string BundleHeading = "Bundles";

    /// <summary>Row label for purchases covering more than one item under a
    /// single price. Sentence case, left of a number.</summary>
    public const string LabelBundlePurchases = "Bundle purchases";

    /// <summary>No per-game price is computed or shown.</summary>
    public const string BundleNote = "No per-game price is shown.";

    // ══ Discounts ═════════════════════════════════════════════════════════

    /// <summary>Heading for the discounts card. Sentence case.</summary>
    public const string DiscountHeading = "Discounts";

    /// <summary>Row label for purchases whose row rendered a discount.
    /// Sentence case, left of a number.</summary>
    public const string LabelDiscountedPurchases = "Discounted purchases";

    /// <summary>Row label for the sum of list prices on discounted rows.
    /// Sentence case, left of a number.</summary>
    public const string LabelDiscountListPrice = "List price total";

    /// <summary>This is not a savings total.</summary>
    public const string DiscountNote = "This is not a savings total.";

    // ══ Biggest transaction ═══════════════════════════════════════════════

    /// <summary>Heading for the biggest-transaction card. Sentence
    /// case.</summary>
    public const string BiggestHeading = "Biggest transaction";

    /// <summary>Largest single transaction, not the most paid for one
    /// game.</summary>
    public const string BiggestNote =
        "Largest single transaction, not the most paid for one game.";

    /// <summary>One price covering several items, not split between
    /// them.</summary>
    public const string BiggestIsBundleNote =
        "One price covering several items, not split.";

    // ══ Wallet ════════════════════════════════════════════════════════════

    /// <summary>Heading for the wallet card. Sentence case.</summary>
    public const string WalletHeading = "Wallet";

    /// <summary>Row label for wallet credit the user paid for. Sentence
    /// case, left of a number.</summary>
    public const string LabelWalletCreditBought = "Wallet credit purchased";

    /// <summary>Row label for credit added by redeeming a code. Sentence
    /// case, left of a number.</summary>
    public const string LabelWalletCreditRedeemed = "Wallet credit redeemed";

    /// <summary>Wallet credit is never counted as spend.</summary>
    public const string WalletNote =
        "Wallet credit is never counted as spend.";

    // ══ Licences ══════════════════════════════════════════════════════════

    /// <summary>Heading for the licences card. Sentence case. British
    /// spelling per codebase convention.</summary>
    public const string LicenceHeading = "Licences";

    /// <summary>These count packages, not games.</summary>
    public const string LicenceNote =
        "These count packages, not games.";

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

    /// <summary>Nothing is converted.</summary>
    public const string CurrencyNote = "Nothing is converted.";

    /// <summary>Row label for transactions that carried no currency symbol
    /// at all. Sentence case, left of a number.</summary>
    public const string LabelNoCurrencySymbol = "No currency symbol";

    // ══ What this was read from ═══════════════════════════════════════════

    /// <summary>Heading for the capture-framing card. Sentence case. This
    /// card frames the entire screen as a captured slice.</summary>
    public const string CaptureHeading = "What this was read from";

    /// <summary>Importing more pages extends this screen.</summary>
    public const string CaptureNote =
        "Importing more pages from PURCHASES extends this.";

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

    /// <summary>Third-party key spending is not visible here.</summary>
    public const string ThirdPartyKeysNote =
        "Keys from third-party sellers (Humble, Fanatical and others) never "
        + "appear in Steam's spending pages, so that money is not here.";
}
