using Winnow.Core.Auth;
using Winnow.Core.Ingest;
using Winnow.Ingest.Steam.AccountPages;
using Xunit;

namespace Winnow.Tests.SteamAccount;

/// <summary>
/// What "this capture is incomplete" is allowed to be concluded from.
///
/// <para>Written against a live-run bug: a walk the user watched reach the last
/// licences page still reported both pages truncated, every time. The cause was
/// a count. A real licences page whose paginator reads "Showing licenses 1-100
/// of 979" renders 96 rows, measured 2026-08-29, so the advertised total includes
/// licences that never appear in the table. The old rule compared the two and
/// declared every complete capture partial; on the real account that produced
/// exactly the 957-against-979 the user saw.</para>
///
/// <para>The rule these tests fix in place: completeness is decided by the
/// controls that end the list, a paginator with no next link and a load-more
/// control that is no longer shown. A count mismatch is reported, never
/// concluded from.</para>
/// </summary>
public class SteamAccountPageTruncationTests
{
    private static SteamLicensesPageResult Licenses(string fixture)
        => SteamLicensesPageParser.Parse(SteamAccountPageFixtures.Read(fixture));

    private static SteamPurchaseHistoryPageResult History(string fixture)
        => SteamPurchaseHistoryPageParser.Parse(SteamAccountPageFixtures.Read(fixture));

    // ---- Licences -----------------------------------------------------------

    [Fact]
    public void The_last_licences_page_is_complete_even_though_the_advertised_total_is_higher()
    {
        var result = Licenses(SteamAccountPageFixtures.LicensesFinalPage);

        // No next link: the paginator itself says this is the end of the list.
        Assert.False(result.HasNextPage);
        Assert.False(result.IsTruncated);

        // And it says so while advertising a total the rows do not reach,
        // which is the exact shape that used to force a truncation verdict.
        Assert.Equal(979, result.TotalLicensesReported);
        Assert.True(result.RowsSeen < result.TotalLicensesReported);
    }

    [Fact]
    public void A_page_with_a_next_link_is_still_truncated()
    {
        var result = Licenses(SteamAccountPageFixtures.LicensesPage1);

        Assert.True(result.HasNextPage);
        Assert.True(result.IsTruncated);
    }

    [Fact]
    public void The_count_mismatch_is_reported_as_a_fact_rather_than_a_verdict()
    {
        var final = Licenses(SteamAccountPageFixtures.LicensesFinalPage);

        // Both are true of the same document at the same time, and that is the
        // point: Steam advertises more licences than it renders, and the capture
        // is complete anyway.
        Assert.True(final.ReportedTotalDiffersFromRowsSeen);
        Assert.False(final.IsTruncated);

        // RowsSeen counts rows the parser could not read as well as the ones it
        // could, so the mismatch it reports is against the table, not against
        // the parser's success rate.
        Assert.Equal(final.Rows.Count + final.SkippedRows, final.RowsSeen);
    }

    [Fact]
    public void A_page_with_no_paginator_at_all_reports_no_mismatch()
    {
        // An account under a hundred licences has nothing to page through, so
        // there is no advertised total to disagree with.
        var html = SteamAccountPageFixtures.Read(SteamAccountPageFixtures.LicensesFinalPage)
            .Replace("license_paginator_ctn", "not_a_paginator", StringComparison.Ordinal);

        var result = SteamLicensesPageParser.Parse(html);

        Assert.Equal(SteamAccountPageParseOutcome.Parsed, result.Outcome);
        Assert.Null(result.TotalLicensesReported);
        Assert.False(result.ReportedTotalDiffersFromRowsSeen);
        Assert.False(result.IsTruncated);
    }

    // ---- Purchase history ---------------------------------------------------

    [Fact]
    public void A_hidden_load_more_control_means_the_history_is_complete()
    {
        var result = History(SteamAccountPageFixtures.PurchaseHistoryExhausted);

        Assert.False(result.HasMoreToLoad);
        Assert.False(result.IsTruncated);
    }

    [Fact]
    public void A_load_more_control_hidden_by_the_area_around_it_also_means_complete()
    {
        // Same document, with the hiding moved from the control to its
        // container. Steam's script hides one or the other and the two look
        // identical to a user; a check that only reads the control's own style
        // calls the second one "more to load" and declares a finished capture
        // partial.
        const string hiddenButton = "class=\"btnv6_blue_hoverfade btn_medium\" style=\"display: none;\" onclick";
        const string shownButton = "class=\"btnv6_blue_hoverfade btn_medium\" onclick";
        const string area = "<div class=\"load_more_history_area\">";
        const string hiddenArea = "<div class=\"load_more_history_area\" style=\"display: none;\">";

        var source = SteamAccountPageFixtures.Read(SteamAccountPageFixtures.PurchaseHistoryExhausted);

        // The rewrite has to have something to rewrite, or this test passes for
        // the wrong reason.
        Assert.Contains(hiddenButton, source, StringComparison.Ordinal);
        Assert.Contains(area, source, StringComparison.Ordinal);

        var html = source
            .Replace(hiddenButton, shownButton, StringComparison.Ordinal)
            .Replace(area, hiddenArea, StringComparison.Ordinal);

        Assert.DoesNotContain(hiddenButton, html, StringComparison.Ordinal);
        Assert.Contains(hiddenArea, html, StringComparison.Ordinal);

        var result = SteamPurchaseHistoryPageParser.Parse(html);

        Assert.Equal(SteamAccountPageParseOutcome.Parsed, result.Outcome);
        Assert.NotEmpty(result.Rows);
        Assert.False(result.HasMoreToLoad);
        Assert.False(result.IsTruncated);
    }

    [Fact]
    public void A_visible_load_more_control_still_means_there_is_more()
    {
        var result = History(SteamAccountPageFixtures.PurchaseHistory);

        Assert.True(result.HasMoreToLoad);
        Assert.True(result.IsTruncated);
    }

    // ---- The authoritative signal -------------------------------------------

    [Fact]
    public void The_session_reports_completeness_from_what_it_watched_happen()
    {
        var pages = new SteamAccountPages
        {
            LicensesHtml = SteamAccountPageFixtures.Read(SteamAccountPageFixtures.LicensesFinalPage),
            HistoryHtml = SteamAccountPageFixtures.Read(SteamAccountPageFixtures.PurchaseHistoryExhausted),
            CapturedAt = DateTimeOffset.UtcNow,
        };

        var walked = SteamPageHarvestResult.Captured(
            pages,
            loadMoreClicks: 12,
            loadMoreStoppedBecause: SteamLoadMoreDecision.Exhausted,
            licensesPagesWalked: 9,
            licensesStoppedBecause: SteamLoadMoreDecision.Exhausted);

        Assert.True(walked.LicensesWalkedToEnd);
        Assert.True(walked.HistoryLoadedToEnd);

        // Every other reason for stopping leaves something unread, and none of
        // them may be mistaken for the one that does not.
        foreach (var stop in new[]
                 {
                     SteamLoadMoreDecision.ReachedCap,
                     SteamLoadMoreDecision.Stalled,
                     SteamLoadMoreDecision.Continue,
                 })
        {
            var stopped = SteamPageHarvestResult.Captured(pages, 12, stop, 9, stop);

            Assert.False(stopped.LicensesWalkedToEnd);
            Assert.False(stopped.HistoryLoadedToEnd);
        }

        // A run that never reached a page has no opinion either way.
        var untouched = SteamPageHarvestResult.Captured(pages, 0, null);

        Assert.False(untouched.LicensesWalkedToEnd);
        Assert.False(untouched.HistoryLoadedToEnd);
    }

    [Fact]
    public void A_cap_that_stopped_the_walk_is_not_completeness()
    {
        // The one case where the parser and the session disagree in the other
        // direction: a capped walk leaves a next link in the document, so both
        // say truncated, and they must keep saying it.
        var capped = SteamPageHarvestResult.Captured(
            new SteamAccountPages
            {
                LicensesHtml = SteamAccountPageFixtures.Read(SteamAccountPageFixtures.LicensesPage1),
                HistoryHtml = SteamAccountPageFixtures.Read(SteamAccountPageFixtures.PurchaseHistory),
                CapturedAt = DateTimeOffset.UtcNow,
            },
            loadMoreClicks: 100,
            loadMoreStoppedBecause: SteamLoadMoreDecision.ReachedCap,
            licensesPagesWalked: 50,
            licensesStoppedBecause: SteamLoadMoreDecision.ReachedCap);

        Assert.False(capped.LicensesWalkedToEnd);
        Assert.False(capped.HistoryLoadedToEnd);

        Assert.True(Licenses(SteamAccountPageFixtures.LicensesPage1).IsTruncated);
        Assert.True(History(SteamAccountPageFixtures.PurchaseHistory).IsTruncated);
    }
}
