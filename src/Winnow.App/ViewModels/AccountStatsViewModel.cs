using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Ingest.Steam.AccountPages;

namespace Winnow.App.ViewModels;

/// <summary>
/// One rendered line of a stats table: a label, an optional count, and an
/// optional money amount. Both numeric columns are strings because the view
/// model decides whether a figure may be shown at all — a mixed-currency
/// capture withholds every amount (see
/// <see cref="AccountStats.IsSingleCurrency"/>) while keeping every count.
/// </summary>
public sealed record AccountStatRow
{
    public required string Label { get; init; }

    /// <summary>A count, or a date on the capture-span rows. Empty renders nothing.</summary>
    public string CountText { get; init; } = string.Empty;

    /// <summary>Money, with the currency symbol as stored. Empty renders nothing.</summary>
    public string AmountText { get; init; } = string.Empty;
}

/// <summary>
/// The STATS screen: what the captured Steam account pages add up to. Reads
/// <see cref="IAccountStatsRepository"/> and nothing else (§5.1) — no ingest,
/// no enrichment, no import.
///
/// <para>Two rules from the read model are enforced here rather than in the
/// markup, because a binding cannot decide not to exist:</para>
/// <list type="number">
/// <item><description><b>No blended sums.</b> Every money figure is formatted
/// through <see cref="Money"/>, which returns the empty string whenever
/// <see cref="AccountStats.IsSingleCurrency"/> is false. Counts are unaffected:
/// a count is currency-free. The read model carries no per-currency totals, so
/// the honest per-currency presentation available is
/// <see cref="CurrencyRows"/> — which symbols appeared, and on how many
/// transactions.</description></item>
/// <item><description><b>Wallet credit is never spend.</b> The two wallet
/// slices are built into their own <see cref="WalletRows"/> collection and are
/// added into no other figure. Counting a top-up and the product it later paid
/// for would count the same money twice.</description></item>
/// </list>
///
/// <para>Amounts are grouped with <see cref="CultureInfo.InvariantCulture"/>.
/// The capture stores the symbol the page rendered and not the locale that
/// rendered it, so formatting the digits back in the user's own locale would be
/// a guess dressed as fidelity. The symbol is reproduced exactly; the digits are
/// grouped one way everywhere.</para>
/// </summary>
public partial class AccountStatsViewModel : ObservableObject
{
    private readonly IAccountStatsRepository _repository;
    private readonly string _source;

    /// <summary>The one symbol observed, or empty when there is none or several.</summary>
    private string _symbol = string.Empty;

    public AccountStatsViewModel(
        IAccountStatsRepository repository,
        string source = AccountFactSources.Steam)
    {
        _repository = repository;
        _source = source;
    }

    // ══ Copy ════════════════════════════════════════════════════════════════

    public string RailRow => AccountStatsCopy.RailRow;

    public string RailTooltip => AccountStatsCopy.RailTooltip;

    public string Title => AccountStatsCopy.Title;

    public string IntroMessage => AccountStatsCopy.Intro;

    public string EmptyMessage => AccountStatsCopy.EmptyMessage;

    public string MixedCurrencyHeading => AccountStatsCopy.MixedCurrencyHeading;

    public string MixedCurrencyMessage => AccountStatsCopy.MixedCurrencyMessage;

    public string SpendHeading => AccountStatsCopy.SpendHeading;

    public string SpendNote => AccountStatsCopy.SpendNote;

    public string YearHeading => AccountStatsCopy.YearHeading;

    public string YearNote => AccountStatsCopy.YearNote;

    public string KindHeading => AccountStatsCopy.KindHeading;

    public string GiftsNote => AccountStatsCopy.GiftsNote;

    public string RefundHeading => AccountStatsCopy.RefundHeading;

    public string RefundNote => AccountStatsCopy.RefundNote;

    public string BundleHeading => AccountStatsCopy.BundleHeading;

    public string BundleNote => AccountStatsCopy.BundleNote;

    public string DiscountHeading => AccountStatsCopy.DiscountHeading;

    public string DiscountNote => AccountStatsCopy.DiscountNote;

    public string BiggestHeading => AccountStatsCopy.BiggestHeading;

    public string BiggestNote => AccountStatsCopy.BiggestNote;

    public string BiggestIsBundleNote => AccountStatsCopy.BiggestIsBundleNote;

    public string WalletHeading => AccountStatsCopy.WalletHeading;

    public string WalletNote => AccountStatsCopy.WalletNote;

    public string LicenceHeading => AccountStatsCopy.LicenceHeading;

    public string LicenceNote => AccountStatsCopy.LicenceNote;

    public string CurrencyHeading => AccountStatsCopy.CurrencyHeading;

    public string CurrencyNote => AccountStatsCopy.CurrencyNote;

    public string CaptureHeading => AccountStatsCopy.CaptureHeading;

    public string CaptureNote => AccountStatsCopy.CaptureNote;

    public string ThirdPartyKeysNote => AccountStatsCopy.ThirdPartyKeysNote;

    // ══ State ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// False until a capture has been imported. Drives the empty state, which
    /// points at the PURCHASES screen rather than showing a table of zeroes.
    /// </summary>
    [ObservableProperty]
    public partial bool HasFacts { get; set; }

    /// <summary>
    /// The capture holds more than one currency symbol, or transactions with
    /// none. Every money figure is withheld while this is true.
    /// </summary>
    [ObservableProperty]
    public partial bool IsMixedCurrency { get; set; }

    /// <summary>Whether the per-currency table is worth drawing at all.</summary>
    [ObservableProperty]
    public partial bool ShowCurrencies { get; set; }

    [ObservableProperty]
    public partial bool ShowBiggest { get; set; }

    /// <summary>Withheld like any other amount when the capture is mixed-currency.</summary>
    [ObservableProperty]
    public partial string BiggestAmountText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowBiggestAmount { get; set; }

    /// <summary>The date the page displayed, or empty when the row carried none.</summary>
    [ObservableProperty]
    public partial string BiggestWhenText { get; set; } = string.Empty;

    /// <summary>The item names on the row, joined. Never a per-item price.</summary>
    [ObservableProperty]
    public partial string BiggestItemsText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowBiggestIsBundle { get; set; }

    // ══ Tables ══════════════════════════════════════════════════════════════

    /// <summary>Net, gross and refunded product spend.</summary>
    public ObservableCollection<AccountStatRow> SpendRows { get; } = [];

    /// <summary>
    /// Net spend per calendar year, oldest first, with the undated slice as its
    /// own trailing line rather than folded into a year.
    /// </summary>
    public ObservableCollection<AccountStatRow> YearRows { get; } = [];

    /// <summary>Purchases, gifts given, in-game purchases.</summary>
    public ObservableCollection<AccountStatRow> KindRows { get; } = [];

    /// <summary>Refunded purchases beside standalone reversal rows. Never summed.</summary>
    public ObservableCollection<AccountStatRow> RefundRows { get; } = [];

    /// <summary>Bundle totals. No per-item figure is derived (§4.7).</summary>
    public ObservableCollection<AccountStatRow> BundleRows { get; } = [];

    /// <summary>Paid and list totals on the rows that rendered a discount. Not savings.</summary>
    public ObservableCollection<AccountStatRow> DiscountRows { get; } = [];

    /// <summary>Wallet movement, reported apart from spend and added into nothing.</summary>
    public ObservableCollection<AccountStatRow> WalletRows { get; } = [];

    /// <summary>Licence counts by acquisition method. Packages, not games.</summary>
    public ObservableCollection<AccountStatRow> LicenceRows { get; } = [];

    /// <summary>Symbols observed, with the transactions that carried none.</summary>
    public ObservableCollection<AccountStatRow> CurrencyRows { get; } = [];

    /// <summary>Dates and counts framing the whole screen as a captured slice.</summary>
    public ObservableCollection<AccountStatRow> CaptureRows { get; } = [];

    // ══ Commands ════════════════════════════════════════════════════════════

    /// <summary>
    /// Recomputes every figure from the fact tables. Raised on open by the
    /// shell's rail command, the same way the Platforms and Purchases screens
    /// refresh; nothing here is cached across opens because the import screen
    /// can change the answer between them.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync(CancellationToken ct)
    {
        Apply(await _repository.GetAsync(_source, ct));
    }

    // ══ Projection ══════════════════════════════════════════════════════════

    private void Apply(AccountStats stats)
    {
        HasFacts = stats.HasAnything;
        IsMixedCurrency = !stats.IsSingleCurrency;
        _symbol = stats.Currencies.Count == 1 ? stats.Currencies[0].Symbol : string.Empty;

        BuildSpend(stats);
        BuildYears(stats);
        BuildKinds(stats);
        BuildRefunds(stats);
        BuildBundles(stats);
        BuildDiscounts(stats);
        BuildWallet(stats);
        BuildLicences(stats);
        BuildCurrencies(stats);
        BuildCapture(stats);
        BuildBiggest(stats);
    }

    private void BuildSpend(AccountStats stats)
    {
        Fill(SpendRows,
            Row(AccountStatsCopy.LabelNetSpend, stats.NetProductTransactionCount, stats.NetProductSpendCents),
            Row(AccountStatsCopy.LabelGrossSpend, stats.GrossProductTransactionCount, stats.GrossProductSpendCents),
            Row(AccountStatsCopy.LabelRefundedSpend, stats.RefundedProductTransactionCount, stats.RefundedProductSpendCents));
    }

    private void BuildYears(AccountStats stats)
    {
        var rows = stats.SpendByYear
            .Select(y => Row(
                y.Year.ToString(CultureInfo.InvariantCulture),
                y.TransactionCount,
                y.Cents))
            .ToList();

        // The undated slice is a line of its own. Guessing it into the nearest
        // year would be inventing the one fact the page did not supply.
        if (stats.UndatedNetTransactionCount > 0)
        {
            rows.Add(Row(
                AccountStatsCopy.LabelUndatedYear,
                stats.UndatedNetTransactionCount,
                stats.UndatedNetSpendCents));
        }

        Fill(YearRows, rows);
    }

    private void BuildKinds(AccountStats stats)
    {
        Fill(KindRows,
            Row(AccountStatsCopy.LabelPurchases, stats.Purchases),
            Row(AccountStatsCopy.LabelGiftsGiven, stats.GiftPurchases),
            Row(AccountStatsCopy.LabelInGamePurchases, stats.InGamePurchases));
    }

    private void BuildRefunds(AccountStats stats)
    {
        // Two different signals, side by side and never added: a flag on the
        // original purchase row, and a separate reversal row.
        Fill(RefundRows,
            Row(
                AccountStatsCopy.LabelRefundedPurchases,
                stats.RefundedProductTransactionCount,
                stats.RefundedProductSpendCents),
            Row(AccountStatsCopy.LabelRefundTransactions, stats.RefundTransactions));
    }

    private void BuildBundles(AccountStats stats)
        => Fill(BundleRows, Row(AccountStatsCopy.LabelBundlePurchases, stats.BundlePurchases));

    private void BuildDiscounts(AccountStats stats)
    {
        Fill(DiscountRows,
            Row(AccountStatsCopy.LabelDiscountedPurchases, stats.DiscountedPurchases),
            new AccountStatRow
            {
                Label = AccountStatsCopy.LabelDiscountListPrice,
                AmountText = Money(stats.DiscountedPurchaseListCents),
            });
    }

    private void BuildWallet(AccountStats stats)
    {
        Fill(WalletRows,
            Row(AccountStatsCopy.LabelWalletCreditBought, stats.WalletCreditPurchases),
            Row(AccountStatsCopy.LabelWalletCreditRedeemed, stats.WalletCreditRedemptions));
    }

    private void BuildLicences(AccountStats stats)
    {
        Fill(LicenceRows, stats.LicenseAcquisitions
            .Select(a => new AccountStatRow
            {
                Label = LicenceLabel(a.Kind),
                CountText = Count(a.Count),
            }));
    }

    private void BuildCurrencies(AccountStats stats)
    {
        var rows = stats.Currencies
            .Select(c => new AccountStatRow { Label = c.Symbol, CountText = Count(c.TransactionCount) })
            .ToList();

        if (stats.TransactionsWithoutCurrency > 0)
        {
            rows.Add(new AccountStatRow
            {
                Label = AccountStatsCopy.LabelNoCurrencySymbol,
                CountText = Count(stats.TransactionsWithoutCurrency),
            });
        }

        Fill(CurrencyRows, rows);

        // Worth drawing whenever there is more than one thing to say, and
        // always when the mixed-currency notice is up — that notice points here.
        ShowCurrencies = rows.Count > 1 || (rows.Count == 1 && IsMixedCurrency);
    }

    private void BuildCapture(AccountStats stats)
    {
        var rows = new List<AccountStatRow>
        {
            new() { Label = AccountStatsCopy.LabelTransactionsRead, CountText = Count(stats.TransactionCount) },
        };

        // A null date is an absent fact, so its row is absent too rather than
        // rendering a placeholder that looks like a value.
        AddDate(rows, AccountStatsCopy.LabelFirstTransaction, stats.FirstTransactionAt);
        AddDate(rows, AccountStatsCopy.LabelLastTransaction, stats.LastTransactionAt);

        if (stats.TransactionsWithoutDate > 0)
        {
            rows.Add(new AccountStatRow
            {
                Label = AccountStatsCopy.LabelTransactionsWithoutDate,
                CountText = Count(stats.TransactionsWithoutDate),
            });
        }

        rows.Add(new AccountStatRow
        {
            Label = AccountStatsCopy.LabelLicencesRead,
            CountText = Count(stats.LicenseCount),
        });

        AddDate(rows, AccountStatsCopy.LabelFirstLicence, stats.FirstLicenseAt);
        AddDate(rows, AccountStatsCopy.LabelLastLicence, stats.LastLicenseAt);

        if (stats.LicensesWithoutDate > 0)
        {
            rows.Add(new AccountStatRow
            {
                Label = AccountStatsCopy.LabelLicencesWithoutDate,
                CountText = Count(stats.LicensesWithoutDate),
            });
        }

        Fill(CaptureRows, rows);
    }

    private void BuildBiggest(AccountStats stats)
    {
        var biggest = stats.BiggestPurchase;

        ShowBiggest = biggest is not null;
        ShowBiggestIsBundle = biggest?.IsBundle ?? false;

        if (biggest is null)
        {
            BiggestAmountText = string.Empty;
            ShowBiggestAmount = false;
            BiggestWhenText = string.Empty;
            BiggestItemsText = string.Empty;
            return;
        }

        // The row's own symbol, not the capture's: this is one transaction, so
        // it can be stated in the currency it was actually charged in even when
        // the capture as a whole cannot be summed.
        BiggestAmountText = IsMixedCurrency && biggest.CurrencySymbol is null
            ? string.Empty
            : (biggest.CurrencySymbol ?? _symbol) + Amount(biggest.Cents);
        ShowBiggestAmount = BiggestAmountText.Length > 0;

        BiggestWhenText = biggest.OccurredAt is { } when ? Date(when) : string.Empty;
        BiggestItemsText = string.Join(", ", biggest.ItemNames);
    }

    // ══ Formatting ══════════════════════════════════════════════════════════

    /// <summary>
    /// Money with the symbol as stored, or the empty string when the capture
    /// mixes currencies. This is the single gate every amount passes through.
    /// </summary>
    private string Money(long cents)
        => IsMixedCurrency ? string.Empty : _symbol + Amount(cents);

    private static string Amount(long cents)
        => (cents / 100m).ToString("N2", CultureInfo.InvariantCulture);

    private static string Count(int count)
        => count.ToString("N0", CultureInfo.InvariantCulture);

    private static string Date(DateTime value)
        => value.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    private AccountStatRow Row(string label, AccountSpendSlice slice)
        => Row(label, slice.Count, slice.Cents);

    private AccountStatRow Row(string label, int count, long cents) => new()
    {
        Label = label,
        CountText = Count(count),
        AmountText = Money(cents),
    };

    private static void AddDate(List<AccountStatRow> rows, string label, DateTime? value)
    {
        if (value is { } date)
        {
            rows.Add(new AccountStatRow { Label = label, CountText = Date(date) });
        }
    }

    /// <summary>
    /// The licences page's own acquisition vocabulary. A null kind is a method
    /// the parser does not recognise: counted, never mapped by guess.
    /// </summary>
    private static string LicenceLabel(string? kind) => kind switch
    {
        SteamLicenseTypes.SteamStore => AccountStatsCopy.LabelLicenceSteamStore,
        SteamLicenseTypes.Complimentary => AccountStatsCopy.LabelLicenceComplimentary,
        SteamLicenseTypes.Gift => AccountStatsCopy.LabelLicenceGift,
        SteamLicenseTypes.Retail => AccountStatsCopy.LabelLicenceRetail,
        _ => AccountStatsCopy.LabelLicenceUnrecognised,
    };

    private static void Fill(ObservableCollection<AccountStatRow> target, params AccountStatRow[] rows)
        => Fill(target, (IEnumerable<AccountStatRow>)rows);

    private static void Fill(ObservableCollection<AccountStatRow> target, IEnumerable<AccountStatRow> rows)
    {
        target.Clear();
        foreach (var row in rows)
        {
            target.Add(row);
        }
    }
}
