using Winnow.Core.Auth;
using Winnow.Core.Ingest;
using Xunit;

namespace Winnow.Tests.SteamAccount;

/// <summary>
/// The contract the embedded session and the saved-file route both produce, and
/// the parser consumes.
///
/// <para>Two things are pinned here. That a partial capture is representable —
/// one page is worth parsing and must not have to masquerade as two — and that
/// neither the pages nor the result will render their contents into a string.
/// The purchase-history document says what the user bought and what they paid;
/// the type is what stops it reaching a log line by accident.</para>
/// </summary>
public class SteamAccountPagesContractTests
{
    private const string LicensesHtml = "<html><body>Half-Life 2, retail key, 2004</body></html>";
    private const string HistoryHtml = "<html><body>Half-Life 2 &mdash; &pound;19.99</body></html>";

    private static readonly DateTimeOffset CapturedAt = new(2026, 8, 28, 9, 30, 0, TimeSpan.Zero);

    private static SteamAccountPages Both() => new()
    {
        LicensesHtml = LicensesHtml,
        HistoryHtml = HistoryHtml,
        CapturedAt = CapturedAt,
    };

    [Fact]
    public void A_complete_capture_carries_both_documents_and_when_it_happened()
    {
        var pages = Both();

        Assert.True(pages.HasLicenses);
        Assert.True(pages.HasHistory);
        Assert.True(pages.IsComplete);
        Assert.False(pages.IsEmpty);
        Assert.Equal(CapturedAt, pages.CapturedAt);

        Assert.Equal(LicensesHtml, pages.Html(SteamAccountPageKind.Licenses));
        Assert.Equal(HistoryHtml, pages.Html(SteamAccountPageKind.PurchaseHistory));
    }

    [Fact]
    public void The_embedded_session_and_the_saved_files_produce_the_same_shape()
    {
        // The whole reason this type is in Core rather than in either route: the
        // parser is written once, against this, and cannot tell which ran.
        Assert.Equal(SteamAccountPageSource.EmbeddedSession, Both().Source);

        var saved = Both() with { Source = SteamAccountPageSource.SavedFile };

        Assert.Equal(SteamAccountPageSource.SavedFile, saved.Source);
        Assert.Equal(Both().LicensesHtml, saved.LicensesHtml);
        Assert.Equal(Both().HistoryHtml, saved.HistoryHtml);
    }

    [Fact]
    public void A_page_at_a_time_is_representable()
    {
        // Capture is sequential — one page is read, then the other — and a run
        // that only ever gets one still has something the parser can use.
        var empty = new SteamAccountPages { CapturedAt = CapturedAt };

        Assert.True(empty.IsEmpty);
        Assert.False(empty.IsComplete);
        Assert.Null(empty.Html(SteamAccountPageKind.Licenses));

        var half = empty.With(SteamAccountPageKind.Licenses, LicensesHtml);

        Assert.True(half.HasLicenses);
        Assert.False(half.HasHistory);
        Assert.False(half.IsComplete);
        Assert.False(half.IsEmpty);

        var whole = half.With(SteamAccountPageKind.PurchaseHistory, HistoryHtml);

        Assert.True(whole.IsComplete);

        // The originals are untouched: a record, not a buffer.
        Assert.False(half.HasHistory);
        Assert.True(empty.IsEmpty);
    }

    [Fact]
    public void Whitespace_is_not_a_page()
    {
        var pages = new SteamAccountPages { LicensesHtml = "   ", CapturedAt = CapturedAt };

        Assert.False(pages.HasLicenses);
        Assert.True(pages.IsEmpty);
    }

    [Fact]
    public void Byte_counts_are_utf8_and_are_the_only_measure_of_a_document()
    {
        var pages = Both();

        Assert.Equal(
            System.Text.Encoding.UTF8.GetByteCount(LicensesHtml),
            pages.ByteCount(SteamAccountPageKind.Licenses));

        Assert.Equal(
            pages.ByteCount(SteamAccountPageKind.Licenses) + pages.ByteCount(SteamAccountPageKind.PurchaseHistory),
            pages.TotalByteCount);

        Assert.Equal(0, new SteamAccountPages { CapturedAt = CapturedAt }.TotalByteCount);
    }

    [Fact]
    public void Neither_the_pages_nor_the_result_render_their_contents()
    {
        var pages = Both();
        var result = SteamPageHarvestResult.Captured(pages, loadMoreClicks: 4, SteamLoadMoreDecision.Exhausted);

        foreach (var rendered in new[] { pages.ToString(), result.ToString() })
        {
            Assert.DoesNotContain("Half-Life", rendered, StringComparison.Ordinal);
            Assert.DoesNotContain("19.99", rendered, StringComparison.Ordinal);
            Assert.Contains("redacted", rendered, StringComparison.Ordinal);
            Assert.Contains("bytes", rendered, StringComparison.Ordinal);
        }
    }

    // ---- The harvest result -------------------------------------------------

    [Fact]
    public void A_complete_run_reports_how_far_the_history_was_expanded()
    {
        var result = SteamPageHarvestResult.Captured(Both(), loadMoreClicks: 12, SteamLoadMoreDecision.Exhausted);

        Assert.Equal(SteamPageHarvestOutcome.Captured, result.Outcome);
        Assert.True(result.HasPages);
        Assert.Equal(12, result.LoadMoreClicks);
        Assert.Equal(SteamLoadMoreDecision.Exhausted, result.LoadMoreStoppedBecause);
        Assert.Null(result.Detail);
    }

    [Fact]
    public void A_capped_run_says_so_so_the_parser_knows_the_page_is_truncated()
    {
        var result = SteamPageHarvestResult.Captured(Both(), loadMoreClicks: 500, SteamLoadMoreDecision.ReachedCap);

        // The difference between "this is the whole account" and "this is as much
        // of it as we were willing to click for" is not something the parser can
        // work out from the HTML.
        Assert.Equal(SteamLoadMoreDecision.ReachedCap, result.LoadMoreStoppedBecause);
    }

    [Fact]
    public void The_licences_walk_is_reported_the_same_way_the_history_clicks_are()
    {
        var complete = SteamPageHarvestResult.Captured(
            Both(),
            loadMoreClicks: 3,
            loadMoreStoppedBecause: SteamLoadMoreDecision.Exhausted,
            licensesPagesWalked: 9,
            licensesStoppedBecause: SteamLoadMoreDecision.Exhausted);

        Assert.Equal(9, complete.LicensesPagesWalked);
        Assert.Equal(SteamLoadMoreDecision.Exhausted, complete.LicensesStoppedBecause);

        // Nine further pages plus the first is a 979-licence account read whole.
        // The same fields say the opposite just as clearly.
        var capped = SteamPageHarvestResult.Captured(
            Both(), 3, SteamLoadMoreDecision.Exhausted, 50, SteamLoadMoreDecision.ReachedCap);

        Assert.Equal(SteamLoadMoreDecision.ReachedCap, capped.LicensesStoppedBecause);
    }

    [Fact]
    public void A_caller_that_ignores_the_licences_walk_still_compiles_and_reads_sensibly()
    {
        // The two licences fields are optional on the factories so that a caller
        // written before the paginator was known keeps working. Their defaults
        // say "no walk happened", which is true of such a caller.
        var result = SteamPageHarvestResult.Captured(Both(), loadMoreClicks: 1, SteamLoadMoreDecision.Exhausted);

        Assert.Equal(0, result.LicensesPagesWalked);
        Assert.Null(result.LicensesStoppedBecause);
    }

    [Fact]
    public void A_partial_run_still_hands_over_what_it_got()
    {
        var half = new SteamAccountPages { LicensesHtml = LicensesHtml, CapturedAt = CapturedAt };
        var result = SteamPageHarvestResult.Partial(half, "the window was closed", 0, null);

        Assert.Equal(SteamPageHarvestOutcome.Partial, result.Outcome);
        Assert.True(result.HasPages);
        Assert.Equal(LicensesHtml, result.Pages?.LicensesHtml);
        Assert.Equal("the window was closed", result.Detail);
    }

    [Fact]
    public void Every_failure_carries_a_reason_and_no_pages()
    {
        var failures = new (SteamPageHarvestResult Result, SteamPageHarvestOutcome Expected)[]
        {
            (SteamPageHarvestResult.Cancelled("closed"), SteamPageHarvestOutcome.Cancelled),
            (SteamPageHarvestResult.Unavailable("no runtime"), SteamPageHarvestOutcome.Unavailable),
            (SteamPageHarvestResult.Failed("nothing read"), SteamPageHarvestOutcome.Failed),
            (SteamPageHarvestResult.NoSession("nobody signed in"), SteamPageHarvestOutcome.NoSession),
        };

        // Every one of these is an ordinary answer, not an exception, and every
        // one of them says why in a line that is safe to show a user.
        foreach (var (result, expected) in failures)
        {
            Assert.Equal(expected, result.Outcome);
            Assert.Null(result.Pages);
            Assert.False(result.HasPages);
            Assert.NotNull(result.Detail);
            Assert.Equal(0, result.LoadMoreClicks);
        }
    }

    [Fact]
    public void A_request_will_not_run_without_consent_being_recorded()
    {
        // Required, and boolean, so a caller cannot construct a request that
        // omits the question. The harvester's refusal is the other half; this is
        // the half that makes forgetting impossible rather than merely
        // detectable.
        var request = new SteamPageHarvestRequest { ConsentGranted = true };

        Assert.True(request.ConsentGranted);
        Assert.Equal(TimeSpan.FromMinutes(15), request.Timeout);
        Assert.Equal(SteamAccountPagePolicy.DefaultMaxLoadMoreClicks, request.MaxLoadMoreClicks);
        Assert.Equal(SteamAccountPagePolicy.DefaultMaxLicensesPages, request.MaxLicensesPages);
    }
}
