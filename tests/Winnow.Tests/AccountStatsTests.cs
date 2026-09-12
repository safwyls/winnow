using Microsoft.Extensions.Logging.Abstractions;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Ingest;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Ingest.Steam.AccountPages;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The account stats surface, end to end: the sanitised fixture pages go through
/// the real importer and real repositories against a real migrated database. Pins
/// two things: the exact numbers each stat produces from the fixture, and that
/// importing the same capture twice moves none of them.
/// </summary>
public sealed class AccountStatsTests : IDisposable
{
    private const string Steam = AccountFactSources.Steam;

    private readonly TempDatabase _db = new();
    private readonly AccountFactRepository _facts;
    private readonly AccountStatsRepository _stats;

    public AccountStatsTests()
    {
        _facts = new AccountFactRepository(_db.Factory);
        _stats = new AccountStatsRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    private SteamAccountPageImportService Service() => new(
        new OwnershipRepository(_db.Factory),
        new ReleaseRepository(_db.Factory),
        _facts,
        new AccountAcquisitionRepository(_db.Factory),
        _db.Factory,
        new LibrarySyncGate(),
        NullLogger<SteamAccountPageImportService>.Instance);

    private static SteamAccountPages Pages(bool licenses = true, bool history = true) => new()
    {
        CapturedAt = new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero),
        Source = SteamAccountPageSource.SavedFile,
        LicensesHtml = licenses
            ? SteamAccountPageFixtures.Read(SteamAccountPageFixtures.LicensesPage1)
            : null,
        HistoryHtml = history
            ? SteamAccountPageFixtures.Read(SteamAccountPageFixtures.PurchaseHistory)
            : null,
    };

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private async Task<AccountStats> ImportAndReadAsync()
    {
        await Service().ImportAsync(Pages());
        return await _stats.GetAsync(Steam);
    }

    // AccountStats holds lists, and record equality compares those by reference,
    // so two reads of an unchanged database are never Equal. Every stat is
    // compared instead — which is also what "the numbers did not move" means.
    private static void AssertSameStats(AccountStats expected, AccountStats actual)
    {
        Assert.Equal(expected.TransactionCount, actual.TransactionCount);
        Assert.Equal(expected.LicenseCount, actual.LicenseCount);
        Assert.Equal(expected.GrossProductSpendCents, actual.GrossProductSpendCents);
        Assert.Equal(expected.GrossProductTransactionCount, actual.GrossProductTransactionCount);
        Assert.Equal(expected.RefundedProductSpendCents, actual.RefundedProductSpendCents);
        Assert.Equal(expected.RefundedProductTransactionCount, actual.RefundedProductTransactionCount);
        Assert.Equal(expected.NetProductSpendCents, actual.NetProductSpendCents);
        Assert.Equal(expected.SpendByYear, actual.SpendByYear);
        Assert.Equal(expected.UndatedNetSpendCents, actual.UndatedNetSpendCents);
        Assert.Equal(expected.UndatedNetTransactionCount, actual.UndatedNetTransactionCount);
        Assert.Equal(expected.Purchases, actual.Purchases);
        Assert.Equal(expected.GiftPurchases, actual.GiftPurchases);
        Assert.Equal(expected.InGamePurchases, actual.InGamePurchases);
        Assert.Equal(expected.RefundTransactions, actual.RefundTransactions);
        Assert.Equal(expected.WalletCreditPurchases, actual.WalletCreditPurchases);
        Assert.Equal(expected.WalletCreditRedemptions, actual.WalletCreditRedemptions);
        Assert.Equal(expected.BundlePurchases, actual.BundlePurchases);
        Assert.Equal(expected.DiscountedPurchases, actual.DiscountedPurchases);
        Assert.Equal(expected.DiscountedPurchaseListCents, actual.DiscountedPurchaseListCents);
        Assert.Equal(expected.BiggestPurchase?.Cents, actual.BiggestPurchase?.Cents);
        Assert.Equal(expected.BiggestPurchase?.OccurredAt, actual.BiggestPurchase?.OccurredAt);
        Assert.Equal(expected.BiggestPurchase?.ItemNames, actual.BiggestPurchase?.ItemNames);
        Assert.Equal(expected.FirstTransactionAt, actual.FirstTransactionAt);
        Assert.Equal(expected.LastTransactionAt, actual.LastTransactionAt);
        Assert.Equal(expected.TransactionsWithoutDate, actual.TransactionsWithoutDate);
        Assert.Equal(expected.Currencies, actual.Currencies);
        Assert.Equal(expected.TransactionsWithoutCurrency, actual.TransactionsWithoutCurrency);
        Assert.Equal(expected.LicenseAcquisitions, actual.LicenseAcquisitions);
        Assert.Equal(expected.LicensesWithUnrecognizedMethod, actual.LicensesWithUnrecognizedMethod);
        Assert.Equal(expected.LicensesWithoutDate, actual.LicensesWithoutDate);
        Assert.Equal(expected.FirstLicenseAt, actual.FirstLicenseAt);
        Assert.Equal(expected.LastLicenseAt, actual.LastLicenseAt);
    }

    // ── the pass records every parsed row, matched or not ────────────────────

    [Fact]
    public async Task Every_parsed_row_is_recorded_even_though_nothing_is_owned()
    {
        var report = await Service().ImportAsync(Pages());

        Assert.Equal(0, report.OwnershipsFilled);
        Assert.Equal(12, report.TransactionFactsRecorded);
        Assert.Equal(13, report.LicenseFactsRecorded);

        var stats = await _stats.GetAsync(Steam);
        Assert.Equal(12, stats.TransactionCount);
        Assert.Equal(13, stats.LicenseCount);
        Assert.True(stats.HasAnything);
    }

    [Fact]
    public async Task An_unrecognised_document_records_no_facts()
    {
        var report = await Service().ImportAsync(new SteamAccountPages
        {
            CapturedAt = DateTimeOffset.UtcNow,
            LicensesHtml = SteamAccountPageFixtures.Read(SteamAccountPageFixtures.NotAnAccountPage),
        });

        Assert.Equal(0, report.TransactionFactsRecorded);
        Assert.Equal(0, report.LicenseFactsRecorded);
        Assert.False((await _stats.GetAsync(Steam)).HasAnything);
    }

    [Fact]
    public async Task An_empty_database_answers_with_zeroes_rather_than_nulls()
    {
        var stats = await _stats.GetAsync(Steam);

        Assert.False(stats.HasAnything);
        Assert.Equal(0, stats.TransactionCount);
        Assert.Equal(0, stats.NetProductSpendCents);
        Assert.Empty(stats.SpendByYear);
        Assert.Empty(stats.Currencies);
        Assert.Empty(stats.LicenseAcquisitions);
        Assert.Null(stats.BiggestPurchase);
        Assert.Null(stats.FirstTransactionAt);
        Assert.True(stats.IsSingleCurrency);
    }

    // ── spend ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Gross_spend_counts_every_product_transaction_and_refunds_come_off_it()
    {
        var stats = await ImportAndReadAsync();

        // 13.49 + 24.99 + 19.99 gift + 8.95 bundle + 19.99 in-game
        // + 29.74 refunded + 7.49 refunded + 1249.00 + 9.99
        Assert.Equal(9, stats.GrossProductTransactionCount);
        Assert.Equal(138363, stats.GrossProductSpendCents);

        Assert.Equal(2, stats.RefundedProductTransactionCount);
        Assert.Equal(3723, stats.RefundedProductSpendCents);

        Assert.Equal(7, stats.NetProductTransactionCount);
        Assert.Equal(134640, stats.NetProductSpendCents);
    }

    [Fact]
    public async Task Wallet_credit_is_reported_beside_spend_and_never_added_into_it()
    {
        var stats = await ImportAndReadAsync();

        // Credit the user bought: one $19.99 top-up.
        Assert.Equal(new AccountSpendSlice(1, 1999), stats.WalletCreditPurchases);

        // Credit redeemed from a gift card: $50.00, and what it cost is not on
        // this page.
        Assert.Equal(new AccountSpendSlice(1, 5000), stats.WalletCreditRedemptions);

        // Neither is inside the spend figure — the in-game purchase that the
        // top-up paid for is already counted there, so adding the top-up would
        // count the same money twice.
        Assert.Equal(134640, stats.NetProductSpendCents);
    }

    [Fact]
    public async Task Spend_per_year_sums_to_the_net_figure()
    {
        var stats = await ImportAndReadAsync();

        Assert.Equal(
            new[]
            {
                new AccountSpendYear(2025, 2, 126899),
                new AccountSpendYear(2026, 5, 7741),
            },
            stats.SpendByYear);

        Assert.Equal(
            stats.NetProductSpendCents,
            stats.SpendByYear.Sum(y => y.Cents) + stats.UndatedNetSpendCents);

        Assert.Equal(0, stats.UndatedNetTransactionCount);
        Assert.Equal(0, stats.TransactionsWithoutDate);
    }

    [Fact]
    public async Task Each_transaction_kind_is_its_own_slice()
    {
        var stats = await ImportAndReadAsync();

        Assert.Equal(new AccountSpendSlice(5, 130642), stats.Purchases);
        Assert.Equal(new AccountSpendSlice(1, 1999), stats.GiftPurchases);
        Assert.Equal(new AccountSpendSlice(1, 1999), stats.InGamePurchases);

        // The reversal row. It overlaps the two purchases flagged refunded — the
        // fixture carries the reversal for one of them only — so it is reported
        // separately and never subtracted a second time.
        Assert.Equal(new AccountSpendSlice(1, 2974), stats.RefundTransactions);
        Assert.Equal(2974, stats.RefundTransactions.Cents);
        Assert.Equal(3723, stats.RefundedProductSpendCents);
    }

    [Fact]
    public async Task A_bundle_total_is_a_fact_and_its_per_item_split_is_not_offered()
    {
        var stats = await ImportAndReadAsync();

        // Six companion items under one $8.95 price.
        Assert.Equal(new AccountSpendSlice(1, 895), stats.BundlePurchases);

        // Nothing on the read model divides that by six, and nothing names an
        // individual item's price.
        Assert.Equal(895, stats.BundlePurchases.Cents);
    }

    [Fact]
    public async Task The_biggest_single_purchase_is_a_transaction_not_a_game_price()
    {
        var stats = await ImportAndReadAsync();

        var biggest = Assert.IsType<AccountBiggestPurchase>(stats.BiggestPurchase);
        Assert.Equal(124900, biggest.Cents);
        Assert.Equal(Utc(2025, 10, 18), biggest.OccurredAt);
        Assert.Equal(new[] { "Solder Queen" }, biggest.ItemNames);
        Assert.Equal(1, biggest.ItemCount);
        Assert.False(biggest.IsBundle);
        Assert.Equal("$", biggest.CurrencySymbol);
    }

    [Fact]
    public async Task Discount_rows_report_what_was_paid_and_what_was_listed_separately()
    {
        var stats = await ImportAndReadAsync();

        // Only the two non-refunded rows that rendered a discount wrapper: the
        // rest of the library carries no list price at all.
        Assert.Equal(new AccountSpendSlice(2, 2244), stats.DiscountedPurchases);
        Assert.Equal(2693, stats.DiscountedPurchaseListCents);
    }

    [Fact]
    public async Task The_span_the_capture_covers_is_reported_alongside_the_totals()
    {
        var stats = await ImportAndReadAsync();

        Assert.Equal(Utc(2025, 5, 16), stats.FirstTransactionAt);
        Assert.Equal(Utc(2026, 8, 24), stats.LastTransactionAt);
        Assert.Equal(Utc(2023, 2, 2), stats.FirstLicenseAt);
        Assert.Equal(Utc(2026, 8, 24), stats.LastLicenseAt);
    }

    // ── currency ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Currency_is_reported_as_observed_and_never_converted()
    {
        var stats = await ImportAndReadAsync();

        Assert.Equal(new[] { new AccountCurrencyUse("$", 12) }, stats.Currencies);
        Assert.Equal(0, stats.TransactionsWithoutCurrency);
        Assert.True(stats.IsSingleCurrency);
    }

    [Fact]
    public async Task A_second_currency_makes_the_totals_declare_themselves_unmixed()
    {
        await Service().ImportAsync(Pages());

        await _facts.TryAppendAsync(new AccountTransactionFact
        {
            Source = Steam,
            Kind = AccountTransactionKinds.Purchase,
            TransactionTypeRaw = "Purchase",
            OccurredAt = Utc(2024, 4, 4),
            ItemNames = ["Elsewhere Edition"],
            TotalCents = 2000,
            CurrencySymbol = "£",
            CapturedAt = Utc(2026, 8, 29),
        });

        var stats = await _stats.GetAsync(Steam);

        Assert.Equal(
            new[] { new AccountCurrencyUse("$", 12), new AccountCurrencyUse("£", 1) },
            stats.Currencies);
        Assert.False(stats.IsSingleCurrency);
    }

    [Theory]
    [InlineData("$19.99 credit", "$credit")]
    [InlineData("$19.99 CREDIT", "$CREDIT")]
    [InlineData("$19.99\u00a0credit", "$credit")]
    public async Task Dollar_credit_shares_dollars_without_changing_stored_facts(string text, string rawSymbol)
    {
        var parsed = Assert.IsType<SteamMoney>(SteamPageValues.TryParseMoney(text));
        Assert.Equal(rawSymbol, parsed.CurrencySymbol);
        var purchase = Transaction("$", 1999) with { Refunded = true };
        var refund = Transaction(parsed.CurrencySymbol, parsed.Cents) with
        {
            Kind = AccountTransactionKinds.Refund, TransactionTypeRaw = "Refund",
        };
        await _facts.TryAppendAsync(purchase);
        await _facts.TryAppendAsync(refund);
        var stats = await _stats.GetAsync(Steam);
        var dollars = Assert.Single(stats.CurrencyGroups);
        Assert.True(stats.IsSingleCurrency);
        Assert.Equal("$", dollars.CurrencySymbol);
        Assert.Equal(new[] { new AccountCurrencyUse("$", 2) }, stats.Currencies);
        Assert.Equal(1999, dollars.GrossProductSpendCents);
        Assert.Equal(1999, dollars.RefundedProductSpendCents);
        Assert.Equal(0, dollars.NetProductSpendCents);
        Assert.Equal(new AccountSpendSlice(1, 1999), dollars.RefundTransactions);
        Assert.Null(await _facts.TryAppendAsync(refund));
        Assert.Equal(rawSymbol, Assert.Single(await _facts.GetTransactionsAsync(Steam),
            f => f.Kind == AccountTransactionKinds.Refund).CurrencySymbol);
    }

    [Fact]
    public async Task Every_monetary_breakdown_and_biggest_purchase_is_scoped_to_its_currency()
    {
        await Service().ImportAsync(Pages());
        var dollarBaseline = await _stats.GetAsync(Steam);
        await _facts.TryAppendAsync(Transaction("GBP", 2000) with
        {
            ItemNames = ["Bundle one", "Bundle two"], ListPriceCents = 3000,
        });
        await _facts.TryAppendAsync(Transaction("GBP", 400) with { Kind = AccountTransactionKinds.GiftPurchase });
        await _facts.TryAppendAsync(Transaction("GBP", 300) with { Kind = AccountTransactionKinds.InGamePurchase });
        await _facts.TryAppendAsync(Transaction("GBP", 500) with { Refunded = true });
        await _facts.TryAppendAsync(Transaction("GBP", 500) with { Kind = AccountTransactionKinds.Refund });
        await _facts.TryAppendAsync(Transaction("GBP", 600) with { Kind = AccountTransactionKinds.WalletCreditPurchase });
        await _facts.TryAppendAsync(Transaction("GBP", null) with
        {
            Kind = AccountTransactionKinds.WalletCreditRedemption, WalletChangeCents = 700,
        });
        await _facts.TryAppendAsync(Transaction("GBP", 100) with { OccurredAt = null });
        var stats = await _stats.GetAsync(Steam);
        Assert.False(stats.IsSingleCurrency);
        Assert.Null(stats.BiggestPurchase);
        var dollars = Assert.Single(stats.CurrencyGroups, g => g.CurrencySymbol == "$");
        AssertSameStats(dollarBaseline, dollars);
        var pounds = Assert.Single(stats.CurrencyGroups, g => g.CurrencySymbol == "GBP");
        Assert.Equal(3300, pounds.GrossProductSpendCents);
        Assert.Equal(500, pounds.RefundedProductSpendCents);
        Assert.Equal(2800, pounds.NetProductSpendCents);
        Assert.Equal(new[] { new AccountSpendYear(2024, 3, 2700) }, pounds.SpendByYear);
        Assert.Equal(100, pounds.UndatedNetSpendCents);
        Assert.Equal(1, pounds.UndatedNetTransactionCount);
        Assert.Equal(new AccountSpendSlice(2, 2100), pounds.Purchases);
        Assert.Equal(new AccountSpendSlice(1, 400), pounds.GiftPurchases);
        Assert.Equal(new AccountSpendSlice(1, 300), pounds.InGamePurchases);
        Assert.Equal(new AccountSpendSlice(1, 500), pounds.RefundTransactions);
        Assert.Equal(new AccountSpendSlice(1, 600), pounds.WalletCreditPurchases);
        Assert.Equal(new AccountSpendSlice(1, 700), pounds.WalletCreditRedemptions);
        Assert.Equal(new AccountSpendSlice(1, 2000), pounds.BundlePurchases);
        Assert.Equal(new AccountSpendSlice(1, 2000), pounds.DiscountedPurchases);
        Assert.Equal(3000, pounds.DiscountedPurchaseListCents);
        Assert.Equal(2000, pounds.BiggestPurchase?.Cents);
        Assert.Equal("GBP", pounds.BiggestPurchase?.CurrencySymbol);
        Assert.True(pounds.BiggestPurchase?.IsBundle);
        Assert.All(stats.CurrencyGroups, g => Assert.Empty(g.CurrencyGroups));
    }

    [Fact]
    public async Task Unknown_currency_keeps_its_counts_without_polluting_known_currency_totals()
    {
        await _facts.TryAppendAsync(Transaction("$", 1000));
        await _facts.TryAppendAsync(Transaction(null, 900000));
        await _facts.TryAppendAsync(Transaction("", 800000));
        await _facts.TryAppendAsync(Transaction("CDN$", 2000));
        var stats = await _stats.GetAsync(Steam);
        Assert.Equal(4, stats.TransactionCount);
        Assert.Equal(2, stats.TransactionsWithoutCurrency);
        Assert.False(stats.IsSingleCurrency);
        Assert.Null(stats.BiggestPurchase);
        Assert.Equal(2, stats.CurrencyGroups.Count);
        Assert.Equal(1000, Assert.Single(stats.CurrencyGroups, g => g.CurrencySymbol == "$").NetProductSpendCents);
        Assert.Equal(2000, Assert.Single(stats.CurrencyGroups, g => g.CurrencySymbol == "CDN$").NetProductSpendCents);
    }

    [Fact]
    public async Task Currency_groups_retain_global_account_overlap_evidence()
    {
        await _facts.TryAppendAsync(Transaction("$", 1000) with { AccountRef = "76561198000000001" });
        await _facts.TryAppendAsync(Transaction("GBP", 2000));
        var stats = await _stats.GetAsync(Steam);
        Assert.All(stats.CurrencyGroups, group =>
        {
            Assert.Equal(1, group.KnownAccountCount);
            Assert.Equal(1, group.UnknownAccountFactCount);
        });
    }

    [Fact]
    public async Task Unknown_product_total_does_not_use_partial_wallet_payment_as_the_price()
    {
        await _facts.TryAppendAsync(Transaction("$", 1000));
        await _facts.TryAppendAsync(Transaction("$", null) with { WalletChangeCents = -300 });
        var dollars = Assert.Single((await _stats.GetAsync(Steam)).CurrencyGroups);
        Assert.Equal(1000, dollars.NetProductSpendCents);
        Assert.Equal(new AccountSpendSlice(2, 1000), dollars.Purchases);
    }

    [Fact]
    public async Task Currency_read_enlists_without_committing_or_disposing_the_callers_transaction()
    {
        using (var scope = _db.Factory.Begin())
        {
            await _facts.TryAppendAsync(Transaction("$", 1000));
            var stats = await _stats.GetAsync(Steam);
            Assert.Equal(1000, Assert.Single(stats.CurrencyGroups).NetProductSpendCents);
            await _facts.TryAppendAsync(Transaction("GBP", 2000));
            Assert.Equal(2, (await _stats.GetAsync(Steam)).CurrencyGroups.Count);
        }
        Assert.False((await _stats.GetAsync(Steam)).HasAnything);
    }

    private static AccountTransactionFact Transaction(string? currency, long? cents) => new()
    {
        Source = Steam, Kind = AccountTransactionKinds.Purchase, TransactionTypeRaw = "Purchase",
        OccurredAt = Utc(2024, 4, 4), ItemNames = ["Example game"], TotalCents = cents,
        CurrencySymbol = currency, CapturedAt = Utc(2026, 8, 29),
    };

    // ── licences ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Licence_acquisition_is_broken_down_by_the_page_s_own_vocabulary()
    {
        var stats = await ImportAndReadAsync();

        Assert.Equal(
            new[]
            {
                new AccountLicenseAcquisition(SteamLicenseTypes.SteamStore, 7),
                new AccountLicenseAcquisition(SteamLicenseTypes.Complimentary, 3),
                new AccountLicenseAcquisition(SteamLicenseTypes.Gift, 2),

                // "Retail Purchase" is not a method this parser recognises, so it
                // is counted as unrecognised rather than mapped by guess.
                new AccountLicenseAcquisition(null, 1),
            },
            stats.LicenseAcquisitions);

        Assert.Equal(1, stats.LicensesWithUnrecognizedMethod);
        Assert.Equal(0, stats.LicensesWithoutDate);
        Assert.Equal(13, stats.LicenseAcquisitions.Sum(a => a.Count));
    }

    [Fact]
    public async Task A_licences_only_capture_still_answers_the_licence_half()
    {
        await Service().ImportAsync(Pages(history: false));

        var stats = await _stats.GetAsync(Steam);

        Assert.Equal(13, stats.LicenseCount);
        Assert.Equal(0, stats.TransactionCount);
        Assert.Equal(0, stats.NetProductSpendCents);
        Assert.Null(stats.BiggestPurchase);
    }

    // ── identity: a fact recorded twice is still one fact ────────────────────

    [Fact]
    public async Task A_second_import_of_the_same_pages_changes_no_stat()
    {
        var first = await ImportAndReadAsync();

        var second = await Service().ImportAsync(Pages());

        Assert.Equal(0, second.TransactionFactsRecorded);
        Assert.Equal(12, second.TransactionFactsAlreadyRecorded);
        Assert.Equal(0, second.LicenseFactsRecorded);
        Assert.Equal(13, second.LicenseFactsAlreadyRecorded);

        AssertSameStats(first, await _stats.GetAsync(Steam));
    }

    [Fact]
    public async Task A_third_import_still_changes_no_stat()
    {
        var first = await ImportAndReadAsync();

        await Service().ImportAsync(Pages());
        await Service().ImportAsync(Pages());

        AssertSameStats(first, await _stats.GetAsync(Steam));
        Assert.Equal(12, (await _facts.GetTransactionsAsync(Steam)).Count);
        Assert.Equal(13, (await _facts.GetLicensesAsync(Steam)).Count);
    }

    [Fact]
    public async Task A_capture_taken_on_a_later_day_re_reports_the_same_facts_and_adds_none()
    {
        await Service().ImportAsync(Pages());

        await Service().ImportAsync(new SteamAccountPages
        {
            CapturedAt = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Source = SteamAccountPageSource.SavedFile,
            LicensesHtml = SteamAccountPageFixtures.Read(SteamAccountPageFixtures.LicensesPage1),
            HistoryHtml = SteamAccountPageFixtures.Read(SteamAccountPageFixtures.PurchaseHistory),
        });

        var transactions = await _facts.GetTransactionsAsync(Steam);
        Assert.Equal(12, transactions.Count);

        // Provenance stays with the capture that first reported the fact.
        Assert.All(transactions, t => Assert.Equal(Utc(2026, 8, 29), t.CapturedAt));
    }

    [Fact]
    public async Task A_transaction_that_differs_in_one_reported_value_is_a_different_fact()
    {
        var fact = new AccountTransactionFact
        {
            Source = Steam,
            Kind = AccountTransactionKinds.Purchase,
            TransactionTypeRaw = "Purchase",
            OccurredAt = Utc(2026, 3, 1),
            ItemNames = ["Harrow Deep"],
            TotalCents = 1000,
            CurrencySymbol = "$",
            CapturedAt = Utc(2026, 8, 29),
        };

        Assert.NotNull(await _facts.TryAppendAsync(fact));
        Assert.Null(await _facts.TryAppendAsync(fact));

        // A different amount is a different fact and survives.
        Assert.NotNull(await _facts.TryAppendAsync(fact with { TotalCents = 1001 }));

        // A null total is distinguishable from any amount, which a plain UNIQUE
        // index over the nullable column could not do.
        Assert.NotNull(await _facts.TryAppendAsync(fact with { TotalCents = null }));
        Assert.Null(await _facts.TryAppendAsync(fact with { TotalCents = null }));

        Assert.Equal(3, (await _facts.GetTransactionsAsync(Steam)).Count);
    }

    [Fact]
    public async Task A_licence_that_differs_in_one_reported_value_is_a_different_fact()
    {
        var fact = new AccountLicenseFact
        {
            Source = Steam,
            ItemName = "Harrow Deep",
            AcquiredAt = Utc(2024, 1, 5),
            AcquisitionKind = SteamLicenseTypes.Complimentary,
            AcquisitionMethodRaw = "Complimentary",
            PackageId = "1000003",
            CapturedAt = Utc(2026, 8, 29),
        };

        Assert.NotNull(await _facts.TryAppendAsync(fact));
        Assert.Null(await _facts.TryAppendAsync(fact));

        Assert.NotNull(await _facts.TryAppendAsync(fact with { AcquiredAt = null }));
        Assert.Null(await _facts.TryAppendAsync(fact with { AcquiredAt = null }));

        Assert.NotNull(await _facts.TryAppendAsync(fact with { PackageId = null }));

        Assert.Equal(3, (await _facts.GetLicensesAsync(Steam)).Count);
    }

    // ── what is deliberately not stored ──────────────────────────────────────

    [Fact]
    public async Task A_gift_row_records_that_a_recipient_existed_and_nothing_about_who()
    {
        await Service().ImportAsync(Pages());

        var transactions = await _facts.GetTransactionsAsync(Steam);
        var gift = Assert.Single(transactions, t => t.Kind == AccountTransactionKinds.GiftPurchase);

        Assert.True(gift.GiftRecipientPresent);
        Assert.Equal(new[] { "Cinder & Bloom" }, gift.ItemNames);

        // The fixture's recipient persona is "GiftRecipient". Nothing stored
        // carries it, and no column exists that could.
        Assert.All(transactions, t =>
        {
            Assert.DoesNotContain("GiftRecipient", string.Join(" ", t.ItemNames), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("GiftRecipient", t.Note ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task A_card_row_records_the_payment_kind_and_no_fragment_of_the_card()
    {
        await Service().ImportAsync(Pages());

        var transactions = await _facts.GetTransactionsAsync(Steam);

        Assert.Contains(transactions, t => t.PaymentKind == SteamPaymentKinds.Card);
        Assert.Contains(transactions, t => t.PaymentKind == SteamPaymentKinds.Wallet);
        Assert.All(transactions, t => Assert.DoesNotContain(
            "**", t.PaymentKind ?? string.Empty, StringComparison.Ordinal));
    }
}
