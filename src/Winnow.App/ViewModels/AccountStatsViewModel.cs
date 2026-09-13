using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Ingest.Steam.AccountPages;

namespace Winnow.App.ViewModels;

/// <summary>A labelled count and currency-specific amount in a detailed table.</summary>
public sealed record AccountStatRow
{
    public required string Label { get; init; }

    /// <summary>A count, or a date on the capture-span rows. Empty renders nothing.</summary>
    public string CountText { get; init; } = string.Empty;

    /// <summary>Money, with the currency symbol as stored. Empty renders nothing.</summary>
    public string AmountText { get; init; } = string.Empty;
}

/// <summary>Projects captured facts without blending currencies or overlapping accounts.</summary>
public partial class AccountStatsViewModel : ObservableObject
{
    private readonly IAccountStatsRepository _repository;
    private readonly string _source;

    /// <summary>The one symbol observed, or empty when there is none or several.</summary>
    private string _symbol = string.Empty;
    [ObservableProperty] public partial int DashboardVersion { get; set; }
    private AccountStats? _stats;
    private bool _moneyAvailable;
    public ObservableCollection<string> CurrencyOptions { get; } = [];
    public ObservableCollection<AccountCurrencySummary> CurrencySummaries { get; } = [];
    [ObservableProperty] public partial string? SelectedCurrency { get; set; }
    [ObservableProperty] public partial IReadOnlyList<AccountChartItem> YearChart { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<AccountChartItem> KindChart { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<AccountChartItem> LicenceChart { get; set; } = [];
    [ObservableProperty] public partial string KindChartNote { get; set; } = string.Empty;
    [ObservableProperty] public partial string AverageSpendInsight { get; set; } = string.Empty;
    [ObservableProperty] public partial string SpendInsight { get; set; } = string.Empty;
    partial void OnSelectedCurrencyChanged(string? value) { if (_stats is not null) ProjectCurrency(_stats); }

    private string _accountScopeNote = string.Empty;
    private bool _ambiguousAccountOverlap;

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

    public string IntroMessage => AccountStatsCopy.Intro + _accountScopeNote;

    public string EmptyMessage => AccountStatsCopy.EmptyMessage;

    public string MixedCurrencyHeading => AccountStatsCopy.MixedCurrencyHeading;

    public string MixedCurrencyMessage => AccountStatsCopy.MixedCurrencyMessage;

    public string SpendHeading => AccountStatsCopy.SpendHeading;

    public string SpendNote => AccountStatsCopy.SpendNote;

    public string SummaryHeading => "From captured purchases";
    public string SummaryNote => "Product transactions with recorded prices only. Wallet credit and standalone refund rows are excluded. Percentages count transactions, not games or money; missing-price rows and uncaptured pages are outside these figures.";
    public string NetSpendLabel => "Net spend with recorded prices";
    public string RefundedShareLabel => "Purchases refunded";
    public string BundleShareLabel => "Kept purchases in bundles";

    [ObservableProperty]
    public partial string NetSpendValue { get; set; } = "Not available";

    [ObservableProperty]
    public partial string RefundedShare { get; set; } = "Not available";

    [ObservableProperty]
    public partial string BundleShare { get; set; } = "Not available";

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
    /// The capture needs separate currency totals or contains currencyless records.
    /// </summary>
    [ObservableProperty]
    public partial bool IsMixedCurrency { get; set; }

    /// <summary>Whether the per-currency table is worth drawing at all.</summary>
    [ObservableProperty]
    public partial bool ShowCurrencies { get; set; }

    [ObservableProperty]
    public partial bool ShowBiggest { get; set; }

    /// <summary>The largest transaction within the selected currency.</summary>
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
        // Microsoft.Data.Sqlite completes its async reads synchronously. The
        // account's lifetime aggregates must not occupy the UI dispatcher.
        var stats = await Task.Run(() => _repository.GetAsync(_source, ct), ct);
        ct.ThrowIfCancellationRequested();
        Apply(stats);
    }

    // ══ Projection ══════════════════════════════════════════════════════════

    private void Apply(AccountStats stats)
    {
        _accountScopeNote = stats.KnownAccountCount > 0
            ? $" Totals include {stats.KnownAccountCount:N0} identified Steam {(stats.KnownAccountCount == 1 ? "account" : "accounts")}."
            : string.Empty;
        if (stats.UnknownAccountFactCount > 0)
            _accountScopeNote += stats.KnownAccountCount > 0
                ? " Records with an unknown account may overlap identified captures. Money totals are withheld; counts describe captured records."
                : " Account identity was not recorded for these saved-file or legacy records.";
        _ambiguousAccountOverlap = stats.KnownAccountCount > 0 && stats.UnknownAccountFactCount > 0;
        OnPropertyChanged(nameof(IntroMessage));
        HasFacts = stats.HasAnything;
        IsMixedCurrency = !stats.IsSingleCurrency;
        _stats = stats;
        _symbol = stats.Currencies.Count == 1 ? stats.Currencies[0].Symbol : string.Empty;

        NetSpendValue = stats.GrossProductTransactionCount > 0 && Money(stats.NetProductSpendCents) is { Length: > 0 } money
            ? money : "Not available";
        RefundedShare = Percentage(stats.RefundedProductTransactionCount, stats.GrossProductTransactionCount);
        BundleShare = Percentage(stats.BundlePurchases.Count, stats.NetProductTransactionCount);

        CurrencyOptions.Clear();
        CurrencySummaries.Clear();
        IReadOnlyList<AccountStats> groups = stats.CurrencyGroups.Count > 0 ? stats.CurrencyGroups : stats.Currencies.Count == 1 && stats.IsSingleCurrency ? [stats] : [];
        foreach (var group in groups)
        {
            var symbol = group.Currencies[0].Symbol;
            CurrencyOptions.Add(symbol);
            string Format(long value) => _ambiguousAccountOverlap || group.GrossProductTransactionCount == 0 ? "Not available" : symbol + Amount(value);
            CurrencySummaries.Add(new(symbol, Format(group.NetProductSpendCents), Format(group.GrossProductSpendCents), Format(group.RefundedProductSpendCents)));
        }
        var selection = SelectedCurrency;
        SelectedCurrency = selection is not null && CurrencyOptions.Contains(selection) ? selection : CurrencyOptions.FirstOrDefault();
        ProjectCurrency(stats);
        BuildLicences(stats);
        LicenceChart = stats.LicenseAcquisitions.OrderByDescending(x => x.Count)
            .Select(x => new AccountChartItem(LicenceLabel(x.Kind), x.Count, Count(x.Count), "Azure")).ToArray();
        BuildCurrencies(stats);
        BuildCapture(stats);
        DashboardVersion++;
    }

    private void ProjectCurrency(AccountStats root)
    {
        var stats = root.CurrencyGroups.FirstOrDefault(x => x.Currencies[0].Symbol == SelectedCurrency) ?? root;
        _symbol = stats.Currencies.Count == 1 ? stats.Currencies[0].Symbol : string.Empty;
        _moneyAvailable = stats.IsSingleCurrency && !_ambiguousAccountOverlap;
        NetSpendValue = stats.GrossProductTransactionCount > 0 && Money(stats.NetProductSpendCents) is { Length: > 0 } money ? money : "Not available";
        YearChart = _moneyAvailable ? stats.SpendByYear.Select(x => new AccountChartItem(x.Year.ToString(CultureInfo.InvariantCulture), x.Cents, Money(x.Cents), "Azure")).ToArray() : [];
        var kinds = new[] {
            new AccountChartItem("Purchases", stats.Purchases.Cents, Money(stats.Purchases.Cents), "Azure"),
            new AccountChartItem("Gifts bought for others", stats.GiftPurchases.Cents, Money(stats.GiftPurchases.Cents), "TextDim"),
            new AccountChartItem("In-game purchases", stats.InGamePurchases.Cents, Money(stats.InGamePurchases.Cents), "Text")
        };
        var negativeKinds = kinds.Any(x => x.Value < 0);
        KindChart = _moneyAvailable && !negativeKinds ? kinds.Where(x => x.Value > 0).ToArray() : [];
        KindChartNote = negativeKinds && _moneyAvailable
            ? "Some categories have negative totals. Their signed amounts are shown in the spending breakdown."
            : "No positive net spending to chart yet.";
        var peak = stats.SpendByYear.OrderByDescending(x => x.Cents).FirstOrDefault();
        SpendInsight = _moneyAvailable && peak is not null
            ? $"Highest recorded year: {peak.Year} · {Money(peak.Cents)}" : "No dated spending with a known currency yet.";
        AverageSpendInsight = _moneyAvailable && stats.NetProductTransactionCount > 0
            ? $"Average kept transaction: {_symbol}{(stats.NetProductSpendCents / 100m / stats.NetProductTransactionCount).ToString("N2", CultureInfo.InvariantCulture)} · bundles count as one transaction"
            : "No kept transactions with recorded prices to average.";
        BuildSpend(stats);
        BuildYears(stats);
        BuildKinds(stats);
        BuildRefunds(stats);
        BuildBundles(stats);
        BuildDiscounts(stats);
        BuildWallet(stats);
        BuildBiggest(stats);
        DashboardVersion++;
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
        BiggestAmountText = _ambiguousAccountOverlap || (!_moneyAvailable && biggest.CurrencySymbol is null)
            ? string.Empty
            : (biggest.CurrencySymbol ?? _symbol) + Amount(biggest.Cents);
        ShowBiggestAmount = BiggestAmountText.Length > 0;

        BiggestWhenText = biggest.OccurredAt is { } when ? Date(when) : string.Empty;
        BiggestItemsText = string.Join(", ", biggest.ItemNames);
    }

    // ══ Formatting ══════════════════════════════════════════════════════════

    /// <summary>
    /// Money in the selected currency, withheld when account captures may overlap.
    /// </summary>
    private string Money(long cents)
        => !_moneyAvailable ? string.Empty : _symbol + Amount(cents);

    private string Percentage(int numerator, int denominator)
        => _ambiguousAccountOverlap || denominator <= 0 || numerator < 0 || numerator > denominator
            ? "Not available"
            : (100m * numerator / denominator).ToString("0.#", CultureInfo.CurrentCulture) + "%";

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
