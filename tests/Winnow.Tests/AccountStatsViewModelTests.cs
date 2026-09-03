using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The STATS screen's view model, driven by a fake
/// <see cref="IAccountStatsRepository"/> so each rule can be posed as a shape of
/// read model rather than as a database to seed.
///
/// <para>Three of these tests are about a number NOT being rendered. That is the
/// screen's whole discipline: a blended sum across currencies, a wallet top-up
/// folded into spend, and an undated row guessed into a year are all numbers the
/// data can produce and the interface must refuse.</para>
/// </summary>
public sealed class AccountStatsViewModelTests
{
    private const string Steam = AccountFactSources.Steam;

    private static AccountStatsViewModel Create(AccountStats stats)
        => new(new FakeAccountStatsRepository { Stats = stats });

    private static AccountStatRow Row(AccountStatsViewModel vm, string label)
        => vm.SpendRows.Concat(vm.YearRows).Concat(vm.KindRows).Concat(vm.RefundRows)
            .Concat(vm.BundleRows).Concat(vm.DiscountRows).Concat(vm.WalletRows)
            .Concat(vm.LicenceRows).Concat(vm.CurrencyRows).Concat(vm.CaptureRows)
            .Single(r => r.Label == label);

    // ── Empty ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Nothing imported: the screen says so and points at the screen that
    /// produces the facts, rather than drawing a table of zeroes that reads
    /// like an account with no spending on it.
    /// </summary>
    [Fact]
    public async Task With_no_facts_the_screen_is_an_empty_state_and_not_a_table_of_zeroes()
    {
        var vm = Create(AccountStats.Empty(Steam));

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.False(vm.HasFacts);
        Assert.Contains("PURCHASES", vm.EmptyMessage, StringComparison.Ordinal);

        // Nothing to say about a capture that does not exist.
        Assert.False(vm.ShowBiggest);
        Assert.False(vm.ShowCurrencies);
        Assert.Empty(vm.YearRows);
        Assert.Empty(vm.LicenceRows);

        // The spend table still exists — it is the shape of the screen — but the
        // empty state is what the user sees, and its figures are honest zeroes
        // rather than absent ones.
        Assert.Equal("0.00", Row(vm, AccountStatsCopy.LabelNetSpend).AmountText);
    }

    /// <summary>A licence capture with no purchase history is still a capture.</summary>
    [Fact]
    public async Task Licences_alone_are_enough_to_leave_the_empty_state()
    {
        var vm = Create(AccountStats.Empty(Steam) with
        {
            LicenseCount = 4,
            LicenseAcquisitions = [new AccountLicenseAcquisition("steam_store", 4)],
        });

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.True(vm.HasFacts);
        Assert.Equal("4", Row(vm, AccountStatsCopy.LabelLicenceSteamStore).CountText);
    }

    // ── Mixed currency ───────────────────────────────────────────────────────

    /// <summary>
    /// The rule the data layer states and the interface has to keep: two
    /// currencies cannot be added, so no total is rendered at all. Counts
    /// survive, because a count is currency-free, and the per-currency table
    /// says what was actually seen.
    /// </summary>
    [Fact]
    public async Task A_mixed_currency_capture_renders_no_money_at_all_and_keeps_its_counts()
    {
        var vm = Create(new AccountStats
        {
            Source = Steam,
            TransactionCount = 5,
            GrossProductSpendCents = 9_900,
            GrossProductTransactionCount = 5,
            SpendByYear = [new AccountSpendYear(2024, 3, 5_000)],
            Purchases = new AccountSpendSlice(4, 7_400),
            WalletCreditPurchases = new AccountSpendSlice(1, 2_500),
            BiggestPurchase = new AccountBiggestPurchase
            {
                Cents = 4_000,
                ItemNames = ["Disco Elysium"],
                ItemCount = 1,
                CurrencySymbol = "£",
            },
            Currencies = [new AccountCurrencyUse("$", 3), new AccountCurrencyUse("£", 2)],
        });

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.True(vm.IsMixedCurrency);

        // Not one blended sum anywhere: spend, per-year, per-kind and wallet.
        Assert.Equal(string.Empty, Row(vm, AccountStatsCopy.LabelNetSpend).AmountText);
        Assert.Equal(string.Empty, Row(vm, AccountStatsCopy.LabelGrossSpend).AmountText);
        Assert.Equal(string.Empty, Row(vm, "2024").AmountText);
        Assert.Equal(string.Empty, Row(vm, AccountStatsCopy.LabelPurchases).AmountText);
        Assert.Equal(
            string.Empty,
            Row(vm, AccountStatsCopy.LabelWalletCreditBought).AmountText);
        Assert.All(
            vm.SpendRows.Concat(vm.YearRows).Concat(vm.KindRows).Concat(vm.WalletRows),
            r => Assert.Equal(string.Empty, r.AmountText));

        // Counts are unaffected — they are what the screen still honestly holds.
        Assert.Equal("5", Row(vm, AccountStatsCopy.LabelGrossSpend).CountText);
        Assert.Equal("3", Row(vm, "2024").CountText);
        Assert.Equal("4", Row(vm, AccountStatsCopy.LabelPurchases).CountText);

        // The per-currency figures the read model can support: which symbols,
        // on how many transactions. It carries no per-currency totals, so this
        // is the whole of what an honest breakdown can say.
        Assert.True(vm.ShowCurrencies);
        Assert.Equal("3", Row(vm, "$").CountText);
        Assert.Equal("2", Row(vm, "£").CountText);

        // One transaction still has a currency of its own, so the biggest
        // single row can be stated — in the symbol it was charged in.
        Assert.Equal("£40.00", vm.BiggestAmountText);
    }

    /// <summary>
    /// One symbol is not enough on its own: a transaction that carried no
    /// symbol at all is a second unknown currency for summing purposes, which
    /// is exactly what <see cref="AccountStats.IsSingleCurrency"/> says.
    /// </summary>
    [Fact]
    public async Task One_symbol_plus_a_symbol_less_row_is_still_mixed()
    {
        var vm = Create(new AccountStats
        {
            Source = Steam,
            TransactionCount = 3,
            GrossProductSpendCents = 3_000,
            GrossProductTransactionCount = 3,
            Currencies = [new AccountCurrencyUse("$", 2)],
            TransactionsWithoutCurrency = 1,
        });

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.True(vm.IsMixedCurrency);
        Assert.Equal(string.Empty, Row(vm, AccountStatsCopy.LabelGrossSpend).AmountText);
        Assert.True(vm.ShowCurrencies);
        Assert.Equal("1", Row(vm, AccountStatsCopy.LabelNoCurrencySymbol).CountText);
    }

    /// <summary>One currency: the totals are real, and are rendered with its symbol.</summary>
    [Fact]
    public async Task A_single_currency_capture_renders_its_totals_with_the_symbol_as_stored()
    {
        var vm = Create(new AccountStats
        {
            Source = Steam,
            TransactionCount = 12,
            GrossProductSpendCents = 123_456,
            GrossProductTransactionCount = 12,
            RefundedProductSpendCents = 1_999,
            RefundedProductTransactionCount = 1,
            Currencies = [new AccountCurrencyUse("€", 12)],
        });

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.False(vm.IsMixedCurrency);
        Assert.Equal("€1,234.56", Row(vm, AccountStatsCopy.LabelGrossSpend).AmountText);
        Assert.Equal("€19.99", Row(vm, AccountStatsCopy.LabelRefundedSpend).AmountText);

        // Net is gross minus refunded, computed by the read model and never stored.
        Assert.Equal("€1,214.57", Row(vm, AccountStatsCopy.LabelNetSpend).AmountText);
        Assert.Equal("11", Row(vm, AccountStatsCopy.LabelNetSpend).CountText);

        // One symbol, nothing to disambiguate: the table would be a restatement.
        Assert.False(vm.ShowCurrencies);
    }

    // ── Wallet credit is not spend ───────────────────────────────────────────

    /// <summary>
    /// Wallet credit is money that reached Steam by a different door. It gets
    /// its own rows and is added into nothing: gross spend here is the product
    /// figure the read model reported, with the top-up nowhere inside it.
    /// </summary>
    [Fact]
    public async Task Wallet_credit_is_its_own_fact_and_never_joins_the_spend_figures()
    {
        var vm = Create(new AccountStats
        {
            Source = Steam,
            TransactionCount = 6,
            GrossProductSpendCents = 5_000,
            GrossProductTransactionCount = 4,
            Purchases = new AccountSpendSlice(4, 5_000),
            WalletCreditPurchases = new AccountSpendSlice(1, 2_000),
            WalletCreditRedemptions = new AccountSpendSlice(1, 1_000),
            Currencies = [new AccountCurrencyUse("$", 6)],
        });

        await vm.RefreshCommand.ExecuteAsync(null);

        // The two wallet facts, stated as themselves.
        Assert.Equal("$20.00", Row(vm, AccountStatsCopy.LabelWalletCreditBought).AmountText);
        Assert.Equal("$10.00", Row(vm, AccountStatsCopy.LabelWalletCreditRedeemed).AmountText);

        // And absent from every spend figure: $50.00, not $70.00 or $80.00.
        Assert.Equal("$50.00", Row(vm, AccountStatsCopy.LabelGrossSpend).AmountText);
        Assert.Equal("$50.00", Row(vm, AccountStatsCopy.LabelNetSpend).AmountText);
        Assert.Equal("$50.00", Row(vm, AccountStatsCopy.LabelPurchases).AmountText);

        // The wallet rows live in their own collection, so no spend table can
        // pick one up by accident.
        Assert.DoesNotContain(
            vm.SpendRows.Concat(vm.KindRows),
            r => r.Label == AccountStatsCopy.LabelWalletCreditBought);
        Assert.Equal(2, vm.WalletRows.Count);
    }

    // ── Per-year, with the undated slice ─────────────────────────────────────

    /// <summary>
    /// The years the page dated, oldest first, and then the rows it did not —
    /// on their own line. An undated row is spend; it is simply not spend in a
    /// year Winnow is entitled to name, so it is never folded into one.
    /// </summary>
    [Fact]
    public async Task The_year_table_lists_the_undated_slice_as_its_own_line()
    {
        var vm = Create(new AccountStats
        {
            Source = Steam,
            TransactionCount = 9,
            SpendByYear =
            [
                new AccountSpendYear(2019, 2, 3_000),
                new AccountSpendYear(2023, 4, 8_050),
            ],
            UndatedNetSpendCents = 1_500,
            UndatedNetTransactionCount = 3,
            Currencies = [new AccountCurrencyUse("$", 9)],
        });

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(
            ["2019", "2023", AccountStatsCopy.LabelUndatedYear],
            vm.YearRows.Select(r => r.Label));

        Assert.Equal("$30.00", vm.YearRows[0].AmountText);
        Assert.Equal("$80.50", vm.YearRows[1].AmountText);

        // The undated line carries its own money and its own count, and no year
        // above it moved to absorb them.
        Assert.Equal("$15.00", vm.YearRows[2].AmountText);
        Assert.Equal("3", vm.YearRows[2].CountText);
    }

    /// <summary>No undated rows, no undated line: it is not a permanent fixture.</summary>
    [Fact]
    public async Task With_every_row_dated_the_year_table_has_no_extra_line()
    {
        var vm = Create(new AccountStats
        {
            Source = Steam,
            TransactionCount = 2,
            SpendByYear = [new AccountSpendYear(2021, 2, 4_000)],
            Currencies = [new AccountCurrencyUse("$", 2)],
        });

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(["2021"], vm.YearRows.Select(r => r.Label));
    }

    // ── Bundles, discounts, the biggest row ──────────────────────────────────

    /// <summary>
    /// The bundle total is shown and nothing per-item is derived from it; the
    /// biggest transaction says it is a bundle rather than letting the reader
    /// take it for the price of one game.
    /// </summary>
    [Fact]
    public async Task A_bundle_shows_its_total_and_flags_itself_rather_than_being_split()
    {
        var vm = Create(new AccountStats
        {
            Source = Steam,
            TransactionCount = 1,
            BundlePurchases = new AccountSpendSlice(1, 12_000),
            BiggestPurchase = new AccountBiggestPurchase
            {
                Cents = 12_000,
                OccurredAt = new DateTime(2023, 11, 24, 0, 0, 0, DateTimeKind.Utc),
                ItemNames = ["Portal", "Portal 2", "Half-Life 2"],
                ItemCount = 3,
                CurrencySymbol = "$",
            },
            Currencies = [new AccountCurrencyUse("$", 1)],
        });

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("$120.00", Row(vm, AccountStatsCopy.LabelBundlePurchases).AmountText);

        Assert.True(vm.ShowBiggest);
        Assert.True(vm.ShowBiggestIsBundle);
        Assert.Equal("$120.00", vm.BiggestAmountText);
        Assert.Equal("24 Nov 2023", vm.BiggestWhenText);
        Assert.Equal("Portal, Portal 2, Half-Life 2", vm.BiggestItemsText);

        // The three item names are listed; no per-item price is anywhere.
        Assert.DoesNotContain("40.00", vm.BiggestItemsText, StringComparison.Ordinal);
    }

    /// <summary>
    /// A capture that dated nothing renders no span rows rather than a
    /// placeholder that looks like a value, and says how many rows had no date.
    /// </summary>
    [Fact]
    public async Task An_absent_date_is_an_absent_row()
    {
        var vm = Create(new AccountStats
        {
            Source = Steam,
            TransactionCount = 2,
            TransactionsWithoutDate = 2,
            Currencies = [new AccountCurrencyUse("$", 2)],
        });

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.DoesNotContain(vm.CaptureRows, r => r.Label == AccountStatsCopy.LabelFirstTransaction);
        Assert.DoesNotContain(vm.CaptureRows, r => r.Label == AccountStatsCopy.LabelLastTransaction);
        Assert.Equal("2", Row(vm, AccountStatsCopy.LabelTransactionsWithoutDate).CountText);
    }

    // ── Refresh on open ──────────────────────────────────────────────────────

    /// <summary>
    /// The rail row is the refresh. Every figure is a query rather than a
    /// stored aggregate and the PURCHASES screen can change the answer between
    /// two opens, so opening STATS recomputes — and the command is awaited, not
    /// fired and forgotten.
    /// </summary>
    [Fact]
    public async Task Opening_the_screen_from_the_rail_recomputes_the_figures()
    {
        using var db = new TempDatabase();
        var repository = new FakeAccountStatsRepository
        {
            Stats = AccountStats.Empty(Steam),
        };
        var stats = new AccountStatsViewModel(repository);
        var shell = Shell(db, stats);

        Assert.Equal(0, repository.Reads);

        await shell.ToggleAccountStatsCommand.ExecuteAsync(null);

        Assert.True(shell.IsAccountStatsVisible);
        Assert.False(shell.IsLibraryVisible);
        Assert.False(shell.IsFeedVisible);
        Assert.Equal(1, repository.Reads);
        Assert.Equal(Steam, repository.LastSource);

        // A second capture landed while the screen was elsewhere. Closing and
        // reopening has to show it, which is the whole reason the refresh is on
        // open rather than on construction.
        repository.Stats = AccountStats.Empty(Steam) with { LicenseCount = 3 };

        await shell.ToggleAccountStatsCommand.ExecuteAsync(null);
        Assert.False(shell.IsAccountStatsVisible);
        Assert.Equal(1, repository.Reads);

        await shell.ToggleAccountStatsCommand.ExecuteAsync(null);
        Assert.True(shell.IsAccountStatsVisible);
        Assert.Equal(2, repository.Reads);
        Assert.True(stats.HasFacts);
    }

    /// <summary>Selecting another rail row leaves STATS, like every other screen.</summary>
    [Fact]
    public async Task Another_rail_row_leaves_the_stats_screen()
    {
        using var db = new TempDatabase();
        var shell = Shell(db, DetachedAccountStats.Create());

        await shell.ToggleAccountStatsCommand.ExecuteAsync(null);
        Assert.True(shell.IsAccountStatsVisible);

        shell.ShowLibraryCommand.Execute(null);

        Assert.False(shell.IsAccountStatsVisible);
        Assert.True(shell.IsLibraryVisible);
    }

    private static MergeQueueViewModel MergeQueue(TempDatabase db)
    {
        var releases = new ReleaseRepository(db.Factory);
        var links = new IdentityLinkRepository(db.Factory);
        var refusals = new ExpansionRefusalRepository(db.Factory);

        return new MergeQueueViewModel(
            new MergeCandidateRepository(db.Factory),
            releases,
            new WorkRepository(db.Factory),
            links,
            new OwnershipRepository(db.Factory),
            new LibraryExpansionScan(releases, links, refusals),
            refusals,
            new LibraryQueryRepository(db.Factory));
    }

    private static MainWindowViewModel Shell(TempDatabase db, AccountStatsViewModel stats)
        => new(
            new LibraryViewModel(
                new LibraryQueryRepository(db.Factory),
                new OwnershipRepository(db.Factory),
                new ReleaseRepository(db.Factory),
                new WorkRepository(db.Factory),
                new UpdateEventRepository(db.Factory),
                covers: null),
            MergeQueue(db),
            DetachedStores.Create(),
            DetachedAppearance.Create(),
            DetachedFeed.Create(),
            stats);
}

/// <summary>
/// Returns whatever read model the test hands it, and counts the reads so
/// "refreshes on open" can be asserted as a fact about calls rather than
/// inferred from a rendered figure.
/// </summary>
internal sealed class FakeAccountStatsRepository : IAccountStatsRepository
{
    public AccountStats Stats { get; set; } = AccountStats.Empty(AccountFactSources.Steam);

    public int Reads { get; private set; }

    public string? LastSource { get; private set; }

    public Task<AccountStats> GetAsync(string source, CancellationToken ct = default)
    {
        Reads++;
        LastSource = source;
        return Task.FromResult(Stats);
    }
}

/// <summary>
/// A STATS screen for tests that need one only because
/// <see cref="MainWindowViewModel"/> requires it. Backed by a repository that
/// reports an empty capture, so nothing is read and nothing is claimed.
/// </summary>
internal static class DetachedAccountStats
{
    public static AccountStatsViewModel Create() => new(new FakeAccountStatsRepository());
}
