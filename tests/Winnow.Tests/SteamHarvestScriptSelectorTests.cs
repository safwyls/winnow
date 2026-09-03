using Winnow.Auth.WebView;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Pins the harvest scripts to selectors that were checked against real
/// signed-in account pages on 2026-08-29. These scripts run inside a browser and
/// nothing else can fail them, so the assertions are on the script text and on
/// the fixtures the selectors were derived from.
/// </summary>
public class SteamHarvestScriptSelectorTests
{
    [Fact]
    public void The_row_counter_no_longer_looks_for_an_id_that_is_not_on_the_page()
    {
        // 'store_transactions' is a fragment of the wallet-balance href in the
        // global header, not an element id. getElementById could only ever
        // return null.
        Assert.DoesNotContain(
            "getElementById('store_transactions')",
            SteamHarvestScripts.DefineHelpers,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_row_counter_uses_the_selectors_the_real_history_page_carries()
    {
        Assert.Contains(
            "table.wallet_history_table tbody tr.wallet_table_row",
            SteamHarvestScripts.DefineHelpers,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_history_fixture_carries_the_ids_and_classes_the_scripts_target()
    {
        var html = SteamAccountPageFixtures.Read(SteamAccountPageFixtures.PurchaseHistory);

        Assert.Contains("id=\"load_more_button\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"wallet_history_table\"", html, StringComparison.Ordinal);
        Assert.Contains("wallet_table_row", html, StringComparison.Ordinal);

        // The id the old script looked for is genuinely absent as an id.
        Assert.DoesNotContain("id=\"store_transactions\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_signed_in_probe_targets_a_marker_both_real_pages_carry()
    {
        Assert.Contains("account_pulldown", SteamHarvestScripts.SignedInProbe, StringComparison.Ordinal);

        foreach (var fixture in new[]
                 {
                     SteamAccountPageFixtures.LicensesPage1,
                     SteamAccountPageFixtures.PurchaseHistory,
                 })
        {
            Assert.Contains(
                "id=\"account_pulldown\"",
                SteamAccountPageFixtures.Read(fixture),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_licences_page_is_paginated_and_the_scripts_know_how_to_see_it()
    {
        var html = SteamAccountPageFixtures.Read(SteamAccountPageFixtures.LicensesPage1);

        // No load-more control on this page: it pages with a next link.
        Assert.DoesNotContain("load_more_button", html, StringComparison.Ordinal);
        Assert.Contains("license_paginator_next", html, StringComparison.Ordinal);

        Assert.Contains("license_paginator_next", SteamHarvestScripts.DefineHelpers, StringComparison.Ordinal);
        Assert.Contains("__winnowLicensesNext", SteamHarvestScripts.LicensesPaginatorProbe, StringComparison.Ordinal);
        Assert.Contains("__winnowLicensesTotal", SteamHarvestScripts.LicensesPaginatorProbe, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_script_is_a_self_contained_expression_that_catches_its_own_errors()
    {
        // The probes and actions answer with a shape the host can parse rather
        // than throwing into the page.
        foreach (var script in new[]
                 {
                     SteamHarvestScripts.SignedInProbe,
                     SteamHarvestScripts.LoadMoreProbe,
                     SteamHarvestScripts.ClickLoadMore,
                     SteamHarvestScripts.LicensesPaginatorProbe,
                     SteamHarvestScripts.BeginCapture,
                 })
        {
            Assert.StartsWith("(function () {", script, StringComparison.Ordinal);
            Assert.Contains("catch (e)", script, StringComparison.Ordinal);
        }

        // DefineHelpers only installs the functions the others call, and each of
        // those callers is already guarded.
        Assert.StartsWith("(function () {", SteamHarvestScripts.DefineHelpers, StringComparison.Ordinal);
    }
}
