using Winnow.Ingest.Steam.AccountPages;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Locates the sanitized saved account pages copied to the test output
/// directory (see tests/fixtures/steam-account-pages/README.md).
/// </summary>
internal static class SteamAccountPageFixtures
{
    internal static string PathOf(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "fixtures", "steam-account-pages", fileName);

    internal static string Read(string fileName) => File.ReadAllText(PathOf(fileName));

    internal const string LicensesPage1 = "licenses-page1.html";
    internal const string LicensesFinalPage = "licenses-final-page.html";
    internal const string PurchaseHistory = "purchase-history.html";
    internal const string PurchaseHistoryExhausted = "purchase-history-exhausted.html";
    internal const string NotAnAccountPage = "not-an-account-page.html";
}

/// <summary>
/// Parser tests against the sanitized fixtures. Every row shape found on the
/// real pages has a case here.
/// </summary>
public class SteamAccountPageParserTests
{
    private static SteamLicensesPageResult Licenses(string fixture)
        => SteamLicensesPageParser.Parse(SteamAccountPageFixtures.Read(fixture));

    private static SteamPurchaseHistoryPageResult History(string fixture)
        => SteamPurchaseHistoryPageParser.Parse(SteamAccountPageFixtures.Read(fixture));

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    // ── licences ─────────────────────────────────────────────────────────────

    [Fact]
    public void Licenses_page_is_recognised_and_every_well_formed_row_is_read()
    {
        var result = Licenses(SteamAccountPageFixtures.LicensesPage1);

        Assert.Equal(SteamAccountPageParseOutcome.Parsed, result.Outcome);
        Assert.Equal(13, result.Rows.Count);

        // The fixture's last row has no acquisition cell. It is counted, never guessed.
        Assert.Equal(1, result.SkippedRows);
        Assert.DoesNotContain(result.Rows, r => r.ItemName.Contains("Orphaned", StringComparison.Ordinal));
    }

    [Fact]
    public void Licenses_maps_every_acquisition_method_seen_on_the_real_page()
    {
        var rows = Licenses(SteamAccountPageFixtures.LicensesPage1).Rows;

        Assert.Equal(7, rows.Count(r => r.LicenseType == SteamLicenseTypes.SteamStore));
        Assert.Equal(3, rows.Count(r => r.LicenseType == SteamLicenseTypes.Complimentary));
        Assert.Equal(2, rows.Count(r => r.LicenseType == SteamLicenseTypes.Gift));
    }

    [Fact]
    public void Licenses_counts_an_unknown_acquisition_method_and_never_invents_a_licence_type()
    {
        var result = Licenses(SteamAccountPageFixtures.LicensesPage1);

        var unknown = Assert.Single(result.Rows, r => r.AcquisitionMethod == "Retail Purchase");
        Assert.Null(unknown.LicenseType);
        Assert.False(unknown.HasKnownLicenseType);
        Assert.Equal(1, result.RowsWithUnmappedAcquisition);
    }

    [Fact]
    public void Licenses_reads_the_date_format_the_page_actually_uses()
    {
        var rows = Licenses(SteamAccountPageFixtures.LicensesPage1).Rows;

        Assert.Equal(Utc(2026, 8, 24), rows[0].AcquiredAtUtc);

        // Single-digit day, the other form the page renders.
        Assert.Contains(rows, r => r.AcquiredAtUtc == Utc(2026, 3, 9));
        Assert.All(rows, r => Assert.NotNull(r.AcquiredAtUtc));
        Assert.Equal(0, Licenses(SteamAccountPageFixtures.LicensesPage1).RowsWithUnparsedDate);
    }

    [Fact]
    public void Licenses_lifts_the_package_id_only_from_free_licence_rows()
    {
        var rows = Licenses(SteamAccountPageFixtures.LicensesPage1).Rows;

        var free = rows.Where(r => r.PackageId is not null).ToList();
        Assert.Equal(3, free.Count);
        Assert.All(free, r => Assert.Equal(SteamLicenseTypes.Complimentary, r.LicenseType));
        Assert.Contains(free, r => r.PackageId == "1000001");

        // A store purchase carries no identifier at all — this is the whole
        // matching problem, and it is asserted so a regression is visible.
        Assert.All(
            rows.Where(r => r.LicenseType == SteamLicenseTypes.SteamStore),
            r => Assert.Null(r.PackageId));
    }

    [Fact]
    public void Licenses_item_name_excludes_the_remove_control_text()
    {
        var rows = Licenses(SteamAccountPageFixtures.LicensesPage1).Rows;

        var free = rows.First(r => r.PackageId is not null);
        Assert.DoesNotContain("Remove", free.ItemName, StringComparison.Ordinal);
        Assert.StartsWith("Gravewright : Ascendant -", free.ItemName, StringComparison.Ordinal);
    }

    [Fact]
    public void Licenses_reports_the_paginator_total_and_that_the_capture_is_partial()
    {
        var result = Licenses(SteamAccountPageFixtures.LicensesPage1);

        Assert.Equal(979, result.TotalLicensesReported);
        Assert.True(result.HasNextPage);
        Assert.True(result.IsTruncated);
    }

    [Fact]
    public void Licenses_final_page_has_no_next_link_and_is_not_flagged_truncated_by_it()
    {
        var result = Licenses(SteamAccountPageFixtures.LicensesFinalPage);

        Assert.Equal(SteamAccountPageParseOutcome.Parsed, result.Outcome);
        Assert.False(result.HasNextPage);
        Assert.Equal(979, result.TotalLicensesReported);
    }

    // ── purchase history ─────────────────────────────────────────────────────

    [Fact]
    public void History_page_is_recognised_and_every_well_formed_row_is_read()
    {
        var result = History(SteamAccountPageFixtures.PurchaseHistory);

        Assert.Equal(SteamAccountPageParseOutcome.Parsed, result.Outcome);
        Assert.Equal(12, result.Rows.Count);
        Assert.Equal(1, result.SkippedRows);
    }

    [Fact]
    public void History_reads_a_discounted_purchase_as_price_paid_not_list_price()
    {
        var row = History(SteamAccountPageFixtures.PurchaseHistory).Rows[0];

        Assert.Equal(SteamTransactionTypes.Purchase, row.TransactionType);
        Assert.Equal(1349, row.Total?.Cents);
        Assert.Equal(1349, row.BasePrice?.Cents);
        Assert.Equal(1499, row.OriginalPrice?.Cents);
        Assert.Equal(-10, row.DiscountPercent);
        Assert.Equal("$", row.Total?.CurrencySymbol);
    }

    [Fact]
    public void History_reads_an_undiscounted_purchase()
    {
        var row = History(SteamAccountPageFixtures.PurchaseHistory).Rows[1];

        Assert.Equal(2499, row.Total?.Cents);
        Assert.Null(row.OriginalPrice);
        Assert.Null(row.DiscountPercent);
    }

    [Fact]
    public void History_marks_a_gift_purchase_by_its_type_not_by_the_recipient_link()
    {
        var row = Assert.Single(
            History(SteamAccountPageFixtures.PurchaseHistory).Rows,
            r => r.TransactionType == SteamTransactionTypes.GiftPurchase);

        Assert.Single(row.Items);
        Assert.Equal("Cinder & Bloom", row.Items[0]);

        // The recipient block lives inside the items cell and must not become an item.
        Assert.DoesNotContain(row.Items, i => i.Contains("Gift sent to", StringComparison.Ordinal));
    }

    [Fact]
    public void History_keeps_every_item_of_a_bundle_row_under_one_price()
    {
        var row = Assert.Single(
            History(SteamAccountPageFixtures.PurchaseHistory).Rows, r => r.IsMultiItem);

        Assert.Equal(6, row.Items.Count);
        Assert.Equal(895, row.Total?.Cents);
        Assert.True(row.IsProductRow);
    }

    [Fact]
    public void History_lifts_the_appid_only_from_an_in_game_purchase()
    {
        var result = History(SteamAccountPageFixtures.PurchaseHistory);

        var inGame = Assert.Single(
            result.Rows, r => r.TransactionType == SteamTransactionTypes.InGamePurchase);
        Assert.Equal("730", inGame.AppId);

        Assert.All(
            result.Rows.Where(r => r.TransactionType == SteamTransactionTypes.Purchase),
            r => Assert.Null(r.AppId));
    }

    [Fact]
    public void History_reads_a_wallet_top_up_as_a_note_row_with_a_wallet_change()
    {
        var row = Assert.Single(
            History(SteamAccountPageFixtures.PurchaseHistory).Rows,
            r => r.Note is not null && r.Note.Contains("Wallet Credit", StringComparison.Ordinal));

        Assert.False(row.IsProductRow);
        Assert.Empty(row.Items);
        Assert.Equal(1999, row.WalletChange?.Cents);
    }

    [Fact]
    public void History_reads_a_gift_card_redemption_that_has_no_type_and_no_prices()
    {
        var row = Assert.Single(
            History(SteamAccountPageFixtures.PurchaseHistory).Rows,
            r => r.Note == "Digital Gift Card Redemption");

        Assert.Equal(string.Empty, row.TransactionType);
        Assert.Null(row.Total);
        Assert.Equal(5000, row.WalletChange?.Cents);
    }

    [Fact]
    public void History_distinguishes_a_refunded_purchase_from_a_refund_transaction()
    {
        var rows = History(SteamAccountPageFixtures.PurchaseHistory).Rows;

        var refundedPurchases = rows.Where(r => r.Refunded).ToList();
        Assert.Equal(2, refundedPurchases.Count);
        Assert.All(refundedPurchases, r => Assert.Equal(SteamTransactionTypes.Purchase, r.TransactionType));

        var refundRow = Assert.Single(rows, r => r.TransactionType == SteamTransactionTypes.Refund);
        Assert.False(refundRow.Refunded);
    }

    [Fact]
    public void History_reads_a_grouped_thousands_amount_without_losing_a_factor_of_a_thousand()
    {
        var row = Assert.Single(
            History(SteamAccountPageFixtures.PurchaseHistory).Rows,
            r => r.Total?.Cents == 124900);

        Assert.Equal("Solder Queen", Assert.Single(row.Items));
        Assert.Equal(SteamTransactionTypes.Purchase, row.TransactionType);
    }

    [Fact]
    public void History_classifies_payment_without_carrying_the_card_fragment()
    {
        var rows = History(SteamAccountPageFixtures.PurchaseHistory).Rows;

        Assert.Contains(rows, r => r.PaymentKind == SteamPaymentKinds.Card);
        Assert.Contains(rows, r => r.PaymentKind == SteamPaymentKinds.Wallet);
        Assert.All(rows, r => Assert.DoesNotContain("**", r.PaymentKind ?? string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void History_flags_a_document_that_still_has_a_load_more_control()
    {
        Assert.True(History(SteamAccountPageFixtures.PurchaseHistory).HasMoreToLoad);
        Assert.True(History(SteamAccountPageFixtures.PurchaseHistory).IsTruncated);
    }

    [Fact]
    public void History_treats_a_hidden_load_more_control_as_exhausted()
    {
        var result = History(SteamAccountPageFixtures.PurchaseHistoryExhausted);

        Assert.Equal(SteamAccountPageParseOutcome.Parsed, result.Outcome);
        Assert.False(result.HasMoreToLoad);
        Assert.False(result.IsTruncated);
    }

    // ── refusal ──────────────────────────────────────────────────────────────

    [Fact]
    public void A_page_that_is_neither_account_page_fails_with_a_reason_and_no_rows()
    {
        var html = SteamAccountPageFixtures.Read(SteamAccountPageFixtures.NotAnAccountPage);

        var licenses = SteamLicensesPageParser.Parse(html);
        Assert.Equal(SteamAccountPageParseOutcome.NotRecognized, licenses.Outcome);
        Assert.NotNull(licenses.FailureReason);
        Assert.Empty(licenses.Rows);

        var history = SteamPurchaseHistoryPageParser.Parse(html);
        Assert.Equal(SteamAccountPageParseOutcome.NotRecognized, history.Outcome);
        Assert.NotNull(history.FailureReason);
        Assert.Empty(history.Rows);
    }

    [Fact]
    public void The_wrong_page_in_the_right_slot_is_refused_rather_than_half_read()
    {
        var licensesHtml = SteamAccountPageFixtures.Read(SteamAccountPageFixtures.LicensesPage1);

        Assert.Equal(
            SteamAccountPageParseOutcome.NotRecognized,
            SteamPurchaseHistoryPageParser.Parse(licensesHtml).Outcome);
    }

    [Fact]
    public void Absent_and_blank_documents_are_absent_not_unrecognised()
    {
        Assert.Equal(SteamAccountPageParseOutcome.Absent, SteamLicensesPageParser.Parse(null).Outcome);
        Assert.Equal(SteamAccountPageParseOutcome.Absent, SteamLicensesPageParser.Parse("   ").Outcome);
        Assert.Equal(SteamAccountPageParseOutcome.Absent, SteamPurchaseHistoryPageParser.Parse(null).Outcome);
    }

    [Fact]
    public void Truncated_html_does_not_throw()
    {
        var html = SteamAccountPageFixtures.Read(SteamAccountPageFixtures.PurchaseHistory);
        var cut = html[..(html.Length / 2)];

        var result = SteamPurchaseHistoryPageParser.Parse(cut);

        // AngleSharp repairs the tree; the parser either reads what survived or
        // refuses. Both are fine. Throwing is not.
        Assert.NotEqual(SteamAccountPageParseOutcome.Absent, result.Outcome);
    }

    // ── value parsing ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("$13.49", 1349)]
    [InlineData("$0.00", 0)]
    [InlineData("-$19.99", -1999)]
    [InlineData("+$50.00", 5000)]
    [InlineData("$1,249.00", 124900)]
    [InlineData("$1,249", 124900)]
    [InlineData("14,99€", 1499)]
    [InlineData("£1.00", 100)]
    public void Money_parses_the_shapes_the_pages_render(string text, long cents)
        => Assert.Equal(cents, SteamPageValues.TryParseMoney(text)?.Cents);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Free")]
    public void Money_declines_what_is_not_an_amount(string text)
        => Assert.Null(SteamPageValues.TryParseMoney(text));

    [Theory]
    [InlineData("Aug 24, 2026", 2026, 8, 24)]
    [InlineData("Mar 9, 2026", 2026, 3, 9)]
    [InlineData("Apr 3, 2024", 2024, 4, 3)]
    public void Dates_parse_the_format_the_pages_render(string text, int y, int m, int d)
        => Assert.Equal(new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Utc), SteamPageValues.TryParseDateUtc(text));

    [Theory]
    [InlineData("24/08/2026")]
    [InlineData("2026-08-24")]
    [InlineData("")]
    public void Dates_that_are_not_this_locale_are_declined_rather_than_guessed(string text)
        => Assert.Null(SteamPageValues.TryParseDateUtc(text));
}
