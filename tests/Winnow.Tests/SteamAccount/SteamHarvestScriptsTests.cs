using Winnow.Auth.WebView;
using Xunit;

namespace Winnow.Tests.SteamAccount;

/// <summary>
/// The parts of the in-page licences walk that nothing else can catch.
///
/// <para>A WebView2 control cannot be created in a unit test, so the walk itself
/// is unobservable here. What is observable is the script, and two of the things
/// in it are load-bearing in a way that a later simplification would silently
/// undo: the walk merges the fetched page's <em>rows</em> without a second header
/// row, and it replaces the paginator rather than leaving the first page's in
/// place. Get the second one wrong and every complete walk still parses as a
/// truncated document, with no other symptom.</para>
/// </summary>
public class SteamHarvestScriptsTests
{
    [Fact]
    public void The_walk_replaces_the_paginator_it_followed()
    {
        // The parser decides truncation from a.license_paginator_next and the
        // "Showing X-Y of Z" span. The last page merged has to be the one whose
        // paginator survives, or a document holding every licence reports itself
        // as partial.
        Assert.Contains("license_paginator_ctn", SteamHarvestScripts.LicensesWalkHelpers, StringComparison.Ordinal);
        Assert.Contains("replaceChild", SteamHarvestScripts.LicensesWalkHelpers, StringComparison.Ordinal);
    }

    [Fact]
    public void The_walk_merges_rows_into_the_table_the_parser_recognises()
    {
        // table.account_table carrying a th.license_date_col is exactly how
        // SteamLicensesPageParser finds the table, verified 2026-08-29. The walk
        // has to find the same one in both documents, and must not carry a
        // second header row into it.
        Assert.Contains("table.account_table", SteamHarvestScripts.LicensesWalkHelpers, StringComparison.Ordinal);
        Assert.Contains("th.license_date_col", SteamHarvestScripts.LicensesWalkHelpers, StringComparison.Ordinal);
        Assert.Contains("querySelector('th')", SteamHarvestScripts.LicensesWalkHelpers, StringComparison.Ordinal);
    }

    [Fact]
    public void The_walk_asks_steam_for_its_own_next_page()
    {
        // Same-origin, with the session's cookies, uncached. Anything else would
        // either be refused by the browser or hand back a stale page.
        Assert.Contains("credentials: 'include'", SteamHarvestScripts.LicensesWalkHelpers, StringComparison.Ordinal);
        Assert.Contains("cache: 'no-store'", SteamHarvestScripts.LicensesWalkHelpers, StringComparison.Ordinal);

        // The next URL comes from the page's own paginator link, never from
        // anything this side constructs.
        Assert.Contains("__winnowLicensesNext", SteamHarvestScripts.LicensesWalkHelpers, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_script_answers_rather_than_throwing_into_the_page()
    {
        // A script that raises inside Steam's own handlers could take the page
        // down with it, and the host has no way to tell that apart from a page
        // that simply has nothing to report.
        foreach (var script in new[]
                 {
                     SteamHarvestScripts.SignedInProbe,
                     SteamHarvestScripts.LoadMoreProbe,
                     SteamHarvestScripts.ClickLoadMore,
                     SteamHarvestScripts.LicensesPaginatorProbe,
                     SteamHarvestScripts.LicensesWalkHelpers,
                     SteamHarvestScripts.FetchNextLicensesPage,
                     SteamHarvestScripts.LicensesWalkState,
                     SteamHarvestScripts.BeginCapture,
                     SteamHarvestScripts.Chunk(0, 1024),
                 })
        {
            Assert.Contains("catch", script, StringComparison.Ordinal);
        }
    }
}
