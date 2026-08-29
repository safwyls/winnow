using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Auth;
using Winnow.Core.Ingest;
using Winnow.Ingest.Steam.AccountPages;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The M5 import screen, driven by a fake harvester, a fake picker and a fake
/// importer. Four things are pinned here, and each of them is a promise the
/// ROADMAP's §4.7 amendment made in words:
///
/// <list type="bullet">
/// <item>consent is granted by the command a person pressed and by nothing else;</item>
/// <item>every harvest outcome has a sentence, and two of the six are facts rather than faults;</item>
/// <item>a page that showed the screen only part of the account says so;</item>
/// <item>the counts are the honest ones, including why rows were left alone.</item>
/// </list>
/// </summary>
public class SteamAccountImportViewModelTests
{
    private static SteamAccountImportViewModel Create(
        FakeSteamPageHarvester? harvester = null,
        FakeSteamAccountPageImport? import = null,
        FakeSteamPageFilePicker? picker = null)
        => new(
            import ?? new FakeSteamAccountPageImport(),
            new SteamAccountPageFileLoader(),
            picker ?? new FakeSteamPageFilePicker(),
            harvester);

    private static SteamAccountPages Captured(bool licenses = true, bool history = true) => new()
    {
        CapturedAt = DateTimeOffset.UnixEpoch,
        Source = SteamAccountPageSource.EmbeddedSession,
        LicensesHtml = licenses ? "<html>licenses</html>" : null,
        HistoryHtml = history ? "<html>history</html>" : null,
    };

    // ══ Consent ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Opening the screen asks a question that opens no window and does no IO.
    /// A status read that could start a Steam sign-in would be the worst
    /// possible surprise on the one screen whose job is to explain itself first.
    /// </summary>
    [Fact]
    public async Task Opening_the_screen_asks_what_can_run_here_and_starts_nothing()
    {
        var harvester = new FakeSteamPageHarvester();
        var vm = Create(harvester);

        await vm.RefreshCommand.ExecuteAsync(null);
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, harvester.AvailabilityChecks);
        Assert.Equal(0, harvester.HarvestCalls);
        Assert.Empty(harvester.Requests);
        Assert.True(vm.SignInRouteAvailable);
        Assert.False(vm.ShowSignInUnavailable);
        Assert.False(vm.HasResult);
    }

    /// <summary>
    /// The amendment's first condition rests on this: the request that opens a
    /// browser carries a flag the mechanism may not grant itself. It is set in
    /// the body of the command, and there is no other path in the type that
    /// constructs a request at all.
    /// </summary>
    [Fact]
    public async Task Consent_is_granted_by_the_command_and_by_nothing_else()
    {
        var harvester = new FakeSteamPageHarvester();
        var vm = Create(harvester);

        // Everything short of pressing the button: the availability probe, and
        // reading every string the screen draws.
        await vm.RefreshCommand.ExecuteAsync(null);
        _ = vm.Title + vm.IntroMessage + vm.RailRow + vm.RailTooltip
            + vm.SignInRouteHeading + vm.SignInRouteExplanation + vm.SignInRouteButtonText
            + vm.SavedPagesRouteHeading + vm.SavedPagesRouteExplanation + vm.SavedPagesHintMessage;

        Assert.Empty(harvester.Requests);

        await vm.ImportFromSignInCommand.ExecuteAsync(null);

        var request = Assert.Single(harvester.Requests);
        Assert.True(request.ConsentGranted);
    }

    /// <summary>The saved-file route never touches the browser at all.</summary>
    [Fact]
    public async Task The_saved_file_route_never_asks_the_browser_for_anything()
    {
        var harvester = new FakeSteamPageHarvester();
        var picker = new FakeSteamPageFilePicker(SteamAccountPageFixtures.PathOf(
            SteamAccountPageFixtures.LicensesPage1));
        var vm = Create(harvester, picker: picker);

        await vm.ImportFromSavedPagesCommand.ExecuteAsync(null);

        Assert.Equal(0, harvester.HarvestCalls);
        Assert.Empty(harvester.Requests);
    }

    // ══ The six outcomes ════════════════════════════════════════════════════

    [Fact]
    public async Task Both_pages_captured_reports_it_and_runs_the_import()
    {
        var import = new FakeSteamAccountPageImport();
        var vm = Create(
            new FakeSteamPageHarvester
            {
                Result = SteamPageHarvestResult.Captured(Captured(), loadMoreClicks: 3, null),
            },
            import);

        await vm.ImportFromSignInCommand.ExecuteAsync(null);

        Assert.Equal(SteamAccountImportCopy.OutcomeCaptured, vm.NoticeMessage);
        Assert.True(vm.ShowNotice);
        Assert.False(vm.ShowProblem);
        Assert.True(vm.HasResult);
        Assert.Equal(1, import.Calls);
    }

    /// <summary>One page is a partial run and still worth importing.</summary>
    [Fact]
    public async Task One_page_of_two_still_imports_what_arrived()
    {
        var import = new FakeSteamAccountPageImport();
        var vm = Create(
            new FakeSteamPageHarvester
            {
                Result = SteamPageHarvestResult.Partial(
                    Captured(history: false), "the history page never rendered", 0, null),
            },
            import);

        await vm.ImportFromSignInCommand.ExecuteAsync(null);

        Assert.Equal(SteamAccountImportCopy.OutcomePartial, vm.NoticeMessage);
        Assert.False(vm.ShowProblem);
        Assert.Equal(1, import.Calls);
        Assert.True(vm.HasResult);
    }

    /// <summary>
    /// Closing the window is a decision, not a fault. It lands in the neutral
    /// notice and never wears the attention edge.
    /// </summary>
    [Fact]
    public async Task Closing_the_window_is_a_fact_and_not_a_problem()
    {
        var import = new FakeSteamAccountPageImport();
        var vm = Create(
            new FakeSteamPageHarvester { Result = SteamPageHarvestResult.Cancelled() },
            import);

        await vm.ImportFromSignInCommand.ExecuteAsync(null);

        Assert.Equal(SteamAccountImportCopy.OutcomeCancelled, vm.NoticeMessage);
        Assert.False(vm.ShowProblem);
        Assert.Null(vm.ProblemMessage);
        Assert.False(vm.HasResult);
        Assert.Equal(0, import.Calls);
    }

    /// <summary>
    /// Nobody signed in, so Steam never rendered an account page. The remedy is
    /// to sign in, so this is a fact too — reporting it as a failure would send
    /// the user hunting for a fault that is not there.
    /// </summary>
    [Fact]
    public async Task Nobody_signing_in_is_a_fact_and_not_a_problem()
    {
        var vm = Create(new FakeSteamPageHarvester { Result = SteamPageHarvestResult.NoSession() });

        await vm.ImportFromSignInCommand.ExecuteAsync(null);

        Assert.Equal(SteamAccountImportCopy.OutcomeNoSession, vm.NoticeMessage);
        Assert.False(vm.ShowProblem);
        Assert.False(vm.HasResult);
    }

    /// <summary>
    /// No WebView2 runtime. The screen says so and points at the other route,
    /// which reads the same two pages — the amendment's third condition is that
    /// declining this route costs convenience and nothing else.
    /// </summary>
    [Fact]
    public async Task No_embedded_browser_says_so_and_the_other_route_is_unaffected()
    {
        var import = new FakeSteamAccountPageImport();
        var picker = new FakeSteamPageFilePicker(SteamAccountPageFixtures.PathOf(
            SteamAccountPageFixtures.PurchaseHistory));
        var vm = Create(
            new FakeSteamPageHarvester
            {
                Available = false,
                Result = SteamPageHarvestResult.Unavailable("no WebView2 runtime"),
            },
            import,
            picker);

        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.False(vm.SignInRouteAvailable);
        Assert.True(vm.ShowSignInUnavailable);

        await vm.ImportFromSignInCommand.ExecuteAsync(null);
        Assert.Equal(SteamAccountImportCopy.OutcomeUnavailable, vm.NoticeMessage);
        Assert.False(vm.ShowProblem);

        // And the other column still works, on the same machine, in the same
        // session, into the same importer.
        await vm.ImportFromSavedPagesCommand.ExecuteAsync(null);
        Assert.Equal(1, import.Calls);
        Assert.True(vm.HasResult);
    }

    /// <summary>The one outcome of the six that is genuinely a fault.</summary>
    [Fact]
    public async Task A_session_that_produced_nothing_is_the_one_outcome_that_is_a_problem()
    {
        var vm = Create(new FakeSteamPageHarvester { Result = SteamPageHarvestResult.Failed("empty") });

        await vm.ImportFromSignInCommand.ExecuteAsync(null);

        Assert.Equal(SteamAccountImportCopy.OutcomeFailed, vm.ProblemMessage);
        Assert.True(vm.ShowProblem);
        Assert.False(vm.ShowNotice);
        Assert.False(vm.HasResult);
    }

    /// <summary>
    /// A host that registered no harvester is the same state as a machine that
    /// cannot run one, and it must not throw on the way to saying so.
    /// </summary>
    [Fact]
    public async Task A_screen_with_no_harvester_registered_says_the_route_cannot_run()
    {
        var vm = Create(harvester: null);

        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.False(vm.SignInRouteAvailable);

        await vm.ImportFromSignInCommand.ExecuteAsync(null);
        Assert.Equal(SteamAccountImportCopy.OutcomeUnavailable, vm.NoticeMessage);
        Assert.False(vm.ShowProblem);
    }

    // ══ Busy state ══════════════════════════════════════════════════════════

    /// <summary>
    /// One route in flight holds both buttons. Two concurrent passes would race
    /// each other into the same ownership rows.
    /// </summary>
    [Fact]
    public async Task A_route_in_flight_holds_both_buttons()
    {
        var harvester = new FakeSteamPageHarvester { Gate = new TaskCompletionSource() };
        var vm = Create(harvester);

        var running = vm.ImportFromSignInCommand.ExecuteAsync(null);

        Assert.True(vm.IsBusy);
        Assert.True(vm.ShowSignInBusy);
        Assert.False(vm.ImportFromSignInCommand.CanExecute(null));
        Assert.False(vm.ImportFromSavedPagesCommand.CanExecute(null));

        harvester.Gate.SetResult();
        await running;

        Assert.False(vm.IsBusy);
        Assert.True(vm.ImportFromSavedPagesCommand.CanExecute(null));
    }

    // ══ The result summary ══════════════════════════════════════════════════

    /// <summary>
    /// Every number on this screen is Plex Mono with tabular figures, which is
    /// group separators and no decimals rather than a bare ToString.
    /// </summary>
    [Fact]
    public async Task Counts_are_grouped_and_in_the_order_the_question_is_asked()
    {
        var import = new FakeSteamAccountPageImport
        {
            Report = new SteamAccountPageImportReport
            {
                LicensesOutcome = SteamAccountPageParseOutcome.Parsed,
                HistoryOutcome = SteamAccountPageParseOutcome.Parsed,
                LicenseRowsParsed = 1247,
                HistoryRowsParsed = 318,
                AcquisitionsMatched = 906,
                PricesMatched = 214,
                OwnershipsFilled = 871,
                OwnershipsAlreadyComplete = 35,
            },
        };

        var vm = Create(new FakeSteamPageHarvester
        {
            Result = SteamPageHarvestResult.Captured(Captured(), 0, null),
        }, import);

        await vm.ImportFromSignInCommand.ExecuteAsync(null);

        Assert.Equal(
            new[]
            {
                SteamAccountImportCopy.LabelLicencesFound,
                SteamAccountImportCopy.LabelPurchasesFound,
                SteamAccountImportCopy.LabelMatched,
                SteamAccountImportCopy.LabelPricesMatched,
                SteamAccountImportCopy.LabelGamesUpdated,
                SteamAccountImportCopy.LabelAlreadyComplete,
            },
            vm.Counts.Select(c => c.Label));

        Assert.Equal("1,247", vm.Counts[0].Value);
        Assert.Equal("318", vm.Counts[1].Value);
        Assert.Equal("906", vm.Counts[2].Value);
        Assert.Equal("214", vm.Counts[3].Value);
        Assert.Equal("871", vm.Counts[4].Value);
        Assert.Equal("35", vm.Counts[5].Value);

        Assert.False(vm.ShowNothingApplied);
    }

    /// <summary>
    /// The skip breakdown is why the numbers do not add up, so it names the
    /// real reasons — and only the ones that actually happened. A table of
    /// zeroes explains nothing and buries the two lines that matter.
    /// </summary>
    [Fact]
    public async Task Only_the_skip_reasons_that_happened_are_listed()
    {
        var import = new FakeSteamAccountPageImport
        {
            Report = new SteamAccountPageImportReport
            {
                LicensesOutcome = SteamAccountPageParseOutcome.Parsed,
                HistoryOutcome = SteamAccountPageParseOutcome.Parsed,
                LicenseRowsParsed = 10,
                OwnershipsFilled = 4,
                SkippedBundleRows = 6,
                SkippedRefundedRows = 2,
                SkippedAmbiguousTitle = 1,
                // Every other skip counter is zero and must not draw a row.
            },
        };

        var vm = Create(new FakeSteamPageHarvester
        {
            Result = SteamPageHarvestResult.Captured(Captured(), 0, null),
        }, import);

        await vm.ImportFromSignInCommand.ExecuteAsync(null);

        Assert.True(vm.ShowSkipped);
        Assert.Equal(
            new[]
            {
                SteamAccountImportCopy.SkipBundles,
                SteamAccountImportCopy.SkipRefunds,
                SteamAccountImportCopy.SkipAmbiguous,
            },
            vm.Skipped.Select(s => s.Label));
        Assert.Equal(new[] { "6", "2", "1" }, vm.Skipped.Select(s => s.Value));
    }

    /// <summary>
    /// A pass that read everything and wrote nothing is a normal outcome on the
    /// second run, so it is stated rather than left as six zeroes to interpret.
    /// </summary>
    [Fact]
    public async Task A_pass_that_filled_nothing_says_so_rather_than_showing_bare_zeroes()
    {
        var import = new FakeSteamAccountPageImport
        {
            Report = new SteamAccountPageImportReport
            {
                LicensesOutcome = SteamAccountPageParseOutcome.Parsed,
                LicenseRowsParsed = 40,
                AcquisitionsMatched = 40,
                OwnershipsAlreadyComplete = 40,
                OwnershipsFilled = 0,
            },
        };

        var vm = Create(new FakeSteamPageHarvester
        {
            Result = SteamPageHarvestResult.Captured(Captured(), 0, null),
        }, import);

        await vm.ImportFromSignInCommand.ExecuteAsync(null);

        Assert.True(vm.ShowNothingApplied);
        Assert.False(vm.ShowProblem);
    }

    /// <summary>
    /// A page Steam has redesigned is a problem with a reason attached, not a
    /// silent zero — that is the whole point of the parser reporting NotRecognized.
    /// </summary>
    [Fact]
    public async Task An_unrecognised_page_reports_the_parser_s_reason()
    {
        var import = new FakeSteamAccountPageImport
        {
            Report = new SteamAccountPageImportReport
            {
                LicensesOutcome = SteamAccountPageParseOutcome.NotRecognized,
                LicensesFailureReason = "no licenses table",
                HistoryOutcome = SteamAccountPageParseOutcome.Absent,
            },
        };

        var vm = Create(new FakeSteamPageHarvester
        {
            Result = SteamPageHarvestResult.Captured(Captured(), 0, null),
        }, import);

        await vm.ImportFromSignInCommand.ExecuteAsync(null);

        Assert.True(vm.ShowProblem);
        Assert.Contains("no licenses table", vm.ProblemMessage!, StringComparison.Ordinal);
        Assert.True(vm.HasResult);
    }

    // ══ Truncation — the matrix, route × page × stop reason ═════════════════
    //
    // The rule the review enforced: the FACT of truncation is never suppressed
    // on either route. Only the REMEDY is route-specific, because only the
    // saved-file route's user can click "load more" before saving, and only the
    // embedded route has a walk that can hit a ceiling of its own.

    /// <summary>
    /// The measured trap: a saved history page renders only its first screen of
    /// transactions until "load more" is clicked, so a file saved straight away
    /// is a partial account. The screen has to say so or the user reads a small
    /// number as a small history.
    /// </summary>
    [Fact]
    public async Task A_saved_history_page_that_stopped_at_the_first_screen_says_so()
    {
        var vm = await SavedFileRun(new SteamAccountPageImportReport
        {
            HistoryOutcome = SteamAccountPageParseOutcome.Parsed,
            HistoryRowsParsed = 12,
            HistoryTruncated = true,
        });

        Assert.True(vm.ShowHistoryTruncation);
        Assert.Equal(SteamAccountImportCopy.HistoryTruncatedNotice, vm.HistoryTruncationMessage);
    }

    /// <summary>
    /// The regression this test was rewritten for. It previously asserted that
    /// the embedded route says NOTHING about a truncated document, which
    /// suppressed the fact along with the remedy: a walk that finished while
    /// Steam's own page still advertises more rows saw part of the account, and
    /// a complete-looking result over a partial account is the one dishonest
    /// thing this screen can do. It now asserts that the fact IS reported and
    /// that the saved-file remedy — advice this route's user cannot act on — is
    /// not.
    /// </summary>
    [Fact]
    public async Task The_sign_in_route_reports_truncation_without_the_saved_file_remedy()
    {
        var import = new FakeSteamAccountPageImport
        {
            Report = new SteamAccountPageImportReport
            {
                HistoryOutcome = SteamAccountPageParseOutcome.Parsed,
                HistoryTruncated = true,
                LicensesOutcome = SteamAccountPageParseOutcome.Parsed,
                LicensesTruncated = true,
            },
        };

        var vm = Create(new FakeSteamPageHarvester
        {
            Result = SteamPageHarvestResult.Captured(
                Captured(), 0, SteamLoadMoreDecision.Exhausted,
                licensesPagesWalked: 3, licensesStoppedBecause: SteamLoadMoreDecision.Exhausted),
        }, import);

        await vm.ImportFromSignInCommand.ExecuteAsync(null);

        Assert.True(vm.HasResult);

        // The fact, on both pages.
        Assert.True(vm.ShowHistoryTruncation);
        Assert.True(vm.ShowLicensesTruncation);
        Assert.Equal(SteamAccountImportCopy.SignInHistoryIncompleteNotice, vm.HistoryTruncationMessage);
        Assert.Equal(SteamAccountImportCopy.SignInLicensesIncompleteNotice, vm.LicensesTruncationMessage);

        // Never the other route's advice.
        Assert.NotEqual(SteamAccountImportCopy.HistoryTruncatedNotice, vm.HistoryTruncationMessage);
        Assert.NotEqual(SteamAccountImportCopy.LicensesTruncatedNotice, vm.LicensesTruncationMessage);
    }

    /// <summary>
    /// A stalled licences walk: a page fetch produced no new rows, so the run
    /// holds part of the list. This is the case the review found reported by
    /// nothing at all — <c>LicensesStoppedBecause</c> was consumed by no code.
    /// </summary>
    [Fact]
    public async Task A_stalled_licences_walk_says_the_run_did_not_get_everything()
    {
        var import = new FakeSteamAccountPageImport
        {
            Report = new SteamAccountPageImportReport
            {
                LicensesOutcome = SteamAccountPageParseOutcome.Parsed,
                LicenseRowsParsed = 300,
                LicensesTruncated = true,
            },
        };

        var vm = Create(new FakeSteamPageHarvester
        {
            Result = SteamPageHarvestResult.Captured(
                Captured(), 0, SteamLoadMoreDecision.Exhausted,
                licensesPagesWalked: 3, licensesStoppedBecause: SteamLoadMoreDecision.Stalled),
        }, import);

        await vm.ImportFromSignInCommand.ExecuteAsync(null);

        Assert.Equal(SteamAccountImportCopy.SignInLicensesIncompleteNotice, vm.LicensesTruncationMessage);
    }

    /// <summary>
    /// The licences walk hit Winnow's own ceiling. A different fact from a walk
    /// that ran out of road, and it gets its own sentence: Winnow stopped, not
    /// Steam.
    /// </summary>
    [Fact]
    public async Task A_licences_walk_that_hit_the_ceiling_says_Winnow_stopped()
    {
        var vm = Create(new FakeSteamPageHarvester
        {
            Result = SteamPageHarvestResult.Captured(
                Captured(), 0, SteamLoadMoreDecision.Exhausted,
                licensesPagesWalked: 50, licensesStoppedBecause: SteamLoadMoreDecision.ReachedCap),
        });

        await vm.ImportFromSignInCommand.ExecuteAsync(null);

        Assert.Equal(SteamAccountImportCopy.SignInLicensesReachedCapNotice, vm.LicensesTruncationMessage);

        // The history walk was fine, so its own note stays away. The two pages
        // are reported independently.
        Assert.False(vm.ShowHistoryTruncation);
    }

    /// <summary>
    /// The history's own ceiling, and the symmetric partner of the licences case
    /// above. Rare — the cap is 100 clicks — but a run that saw part of the
    /// account and said nothing would be the one dishonest number on the screen.
    /// </summary>
    [Fact]
    public async Task A_history_walk_that_hit_the_ceiling_says_Winnow_stopped()
    {
        var vm = Create(new FakeSteamPageHarvester
        {
            Result = SteamPageHarvestResult.Captured(
                Captured(), loadMoreClicks: 100, SteamLoadMoreDecision.ReachedCap,
                licensesPagesWalked: 2, licensesStoppedBecause: SteamLoadMoreDecision.Exhausted),
        });

        await vm.ImportFromSignInCommand.ExecuteAsync(null);

        Assert.Equal(SteamAccountImportCopy.SignInHistoryReachedCapNotice, vm.HistoryTruncationMessage);
        Assert.False(vm.ShowLicensesTruncation);
    }

    /// <summary>A stalled history walk, symmetric with the licences one.</summary>
    [Fact]
    public async Task A_stalled_history_walk_says_the_run_did_not_get_everything()
    {
        var vm = Create(new FakeSteamPageHarvester
        {
            Result = SteamPageHarvestResult.Captured(
                Captured(), loadMoreClicks: 4, SteamLoadMoreDecision.Stalled,
                licensesPagesWalked: 1, licensesStoppedBecause: SteamLoadMoreDecision.Exhausted),
        });

        await vm.ImportFromSignInCommand.ExecuteAsync(null);

        Assert.Equal(SteamAccountImportCopy.SignInHistoryIncompleteNotice, vm.HistoryTruncationMessage);
    }

    /// <summary>
    /// A walk that ran out of road on both pages, over a document the parser is
    /// happy with. Nothing was missed, so nothing is claimed.
    /// </summary>
    [Fact]
    public async Task A_session_that_walked_both_pages_out_says_nothing()
    {
        var import = new FakeSteamAccountPageImport
        {
            Report = new SteamAccountPageImportReport
            {
                LicensesOutcome = SteamAccountPageParseOutcome.Parsed,
                HistoryOutcome = SteamAccountPageParseOutcome.Parsed,
                LicenseRowsParsed = 979,
                HistoryRowsParsed = 412,
            },
        };

        var vm = Create(new FakeSteamPageHarvester
        {
            Result = SteamPageHarvestResult.Captured(
                Captured(), loadMoreClicks: 7, SteamLoadMoreDecision.Exhausted,
                licensesPagesWalked: 9, licensesStoppedBecause: SteamLoadMoreDecision.Exhausted),
        }, import);

        await vm.ImportFromSignInCommand.ExecuteAsync(null);

        Assert.False(vm.ShowHistoryTruncation);
        Assert.False(vm.ShowLicensesTruncation);
    }

    /// <summary>The paginator said outright that this is one page of several.</summary>
    [Fact]
    public async Task A_licenses_page_the_paginator_calls_partial_says_the_page_paginates()
    {
        var vm = await SavedFileRun(new SteamAccountPageImportReport
        {
            LicensesOutcome = SteamAccountPageParseOutcome.Parsed,
            LicenseRowsParsed = 100,
            LicensesReportedTotal = 1247,
            LicensesTruncated = true,
        });

        Assert.Equal(SteamAccountImportCopy.LicensesTruncatedNotice, vm.LicensesTruncationMessage);
    }

    /// <summary>
    /// The paginator was not captured and the count stops dead on the page
    /// boundary. Silence here would let a 1,247-game account read as 100.
    /// </summary>
    [Fact]
    public async Task A_licence_count_that_stops_on_the_page_boundary_says_the_page_paginates()
    {
        var vm = await SavedFileRun(new SteamAccountPageImportReport
        {
            LicensesOutcome = SteamAccountPageParseOutcome.Parsed,
            LicenseRowsParsed = 98,
            LicenseRowsSkippedByParser = 2,
            LicensesReportedTotal = null,
            LicensesTruncated = false,
        });

        Assert.True(vm.ShowLicensesTruncation);
    }

    /// <summary>
    /// The page-boundary heuristic belongs to the saved route alone. The
    /// embedded walk reports what it did, so a hundred rows there is a fact
    /// rather than a guess, and inventing a warning over it would be a claim
    /// about the user's library that nothing supports.
    /// </summary>
    [Fact]
    public async Task The_page_boundary_guess_is_never_applied_to_a_walked_session()
    {
        var import = new FakeSteamAccountPageImport
        {
            Report = new SteamAccountPageImportReport
            {
                LicensesOutcome = SteamAccountPageParseOutcome.Parsed,
                LicenseRowsParsed = 100,
                LicensesReportedTotal = null,
                LicensesTruncated = false,
            },
        };

        var vm = Create(new FakeSteamPageHarvester
        {
            Result = SteamPageHarvestResult.Captured(
                Captured(), 0, SteamLoadMoreDecision.Exhausted,
                licensesPagesWalked: 1, licensesStoppedBecause: SteamLoadMoreDecision.Exhausted),
        }, import);

        await vm.ImportFromSignInCommand.ExecuteAsync(null);

        Assert.False(vm.ShowLicensesTruncation);
    }

    /// <summary>
    /// A small account read whole. Warning here would be a claim about the
    /// user's library that the evidence does not support.
    /// </summary>
    [Fact]
    public async Task A_licences_page_read_whole_says_nothing_about_pagination()
    {
        var vm = await SavedFileRun(new SteamAccountPageImportReport
        {
            LicensesOutcome = SteamAccountPageParseOutcome.Parsed,
            LicenseRowsParsed = 42,
            LicensesReportedTotal = 42,
            LicensesTruncated = false,
        });

        Assert.False(vm.ShowLicensesTruncation);
        Assert.False(vm.ShowHistoryTruncation);
    }

    // ══ The picker ══════════════════════════════════════════════════════════

    [Fact]
    public async Task Dismissing_the_picker_changes_nothing()
    {
        var import = new FakeSteamAccountPageImport();
        var vm = Create(import: import, picker: new FakeSteamPageFilePicker());

        await vm.ImportFromSavedPagesCommand.ExecuteAsync(null);

        Assert.Equal(SteamAccountImportCopy.NothingPicked, vm.NoticeMessage);
        Assert.False(vm.ShowProblem);
        Assert.False(vm.HasResult);
        Assert.Equal(0, import.Calls);
    }

    /// <summary>
    /// Files that are not the two account pages are named individually, because
    /// "nothing was imported" over a list of four picked files is not a report.
    /// </summary>
    [Fact]
    public async Task Files_that_are_not_account_pages_are_named_and_nothing_is_imported()
    {
        var import = new FakeSteamAccountPageImport();
        var vm = Create(
            import: import,
            picker: new FakeSteamPageFilePicker(
                SteamAccountPageFixtures.PathOf(SteamAccountPageFixtures.NotAnAccountPage)));

        await vm.ImportFromSavedPagesCommand.ExecuteAsync(null);

        Assert.True(vm.ShowPickedFiles);
        var row = Assert.Single(vm.PickedFiles);
        Assert.Equal(SteamAccountImportCopy.FileNotRecognized, row.Outcome);
        Assert.True(row.IsProblem);
        Assert.Equal(SteamAccountImportCopy.NothingRecognized, vm.NoticeMessage);
        Assert.Equal(0, import.Calls);
    }

    /// <summary>
    /// The likeliest way to produce a second file of one page kind is saving
    /// licences pages 1 and 2 and picking both — in which case page 2 was not
    /// read, and the user's whole reason for picking two files went unmet. It
    /// gets a sentence, not just a label in a narrow column.
    /// </summary>
    [Fact]
    public async Task A_second_file_of_a_page_kind_already_read_says_what_happened()
    {
        var vm = Create(picker: new FakeSteamPageFilePicker(
            SteamAccountPageFixtures.PathOf(SteamAccountPageFixtures.LicensesPage1),
            SteamAccountPageFixtures.PathOf(SteamAccountPageFixtures.LicensesFinalPage)));

        await vm.ImportFromSavedPagesCommand.ExecuteAsync(null);

        Assert.True(vm.ShowDuplicatePages);
        Assert.Equal(2, vm.PickedFiles.Count);
        Assert.Equal(SteamAccountImportCopy.FileLoaded, vm.PickedFiles[0].Outcome);
        Assert.Equal(SteamAccountImportCopy.FileDuplicate, vm.PickedFiles[1].Outcome);
        Assert.True(vm.PickedFiles[1].IsProblem);
    }

    /// <summary>One file of each kind is the ordinary case and says nothing.</summary>
    [Fact]
    public async Task One_file_of_each_kind_raises_no_second_copy_notice()
    {
        var vm = Create(picker: new FakeSteamPageFilePicker(
            SteamAccountPageFixtures.PathOf(SteamAccountPageFixtures.LicensesPage1),
            SteamAccountPageFixtures.PathOf(SteamAccountPageFixtures.PurchaseHistory)));

        await vm.ImportFromSavedPagesCommand.ExecuteAsync(null);

        Assert.False(vm.ShowDuplicatePages);
    }

    /// <summary>
    /// Both routes hand the same importer the same type. The amendment's fourth
    /// condition — one parser, the embedded path being a fetch strategy rather
    /// than a second importer — is enforced here at the screen.
    /// </summary>
    [Fact]
    public async Task Both_routes_converge_on_the_same_importer()
    {
        var import = new FakeSteamAccountPageImport();
        var picker = new FakeSteamPageFilePicker(
            SteamAccountPageFixtures.PathOf(SteamAccountPageFixtures.LicensesPage1),
            SteamAccountPageFixtures.PathOf(SteamAccountPageFixtures.PurchaseHistory));

        var vm = Create(
            new FakeSteamPageHarvester
            {
                Result = SteamPageHarvestResult.Captured(Captured(), 0, null),
            },
            import,
            picker);

        await vm.ImportFromSignInCommand.ExecuteAsync(null);
        await vm.ImportFromSavedPagesCommand.ExecuteAsync(null);

        Assert.Equal(2, import.Calls);
        Assert.Equal(SteamAccountPageSource.EmbeddedSession, import.Seen[0].Source);
        Assert.Equal(SteamAccountPageSource.SavedFile, import.Seen[1].Source);
        Assert.True(import.Seen[1].IsComplete);
    }

    /// <summary>A new attempt clears the last one before it starts.</summary>
    [Fact]
    public async Task A_new_attempt_clears_the_previous_report()
    {
        var import = new FakeSteamAccountPageImport();
        var vm = Create(
            new FakeSteamPageHarvester
            {
                Result = SteamPageHarvestResult.Captured(Captured(), 0, null),
            },
            import,
            new FakeSteamPageFilePicker());

        await vm.ImportFromSignInCommand.ExecuteAsync(null);
        Assert.True(vm.HasResult);

        await vm.ImportFromSavedPagesCommand.ExecuteAsync(null);

        Assert.False(vm.HasResult);
        Assert.Empty(vm.Counts);
        Assert.Empty(vm.Skipped);
        Assert.Empty(vm.PickedFiles);
        Assert.Equal(SteamAccountImportCopy.NothingPicked, vm.NoticeMessage);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the saved-file route over the real loader and real fixtures, with a
    /// fake importer answering the given report. The route matters here: the
    /// truncation notices are gated on where the pages came from.
    /// </summary>
    private static async Task<SteamAccountImportViewModel> SavedFileRun(
        SteamAccountPageImportReport report)
    {
        var vm = Create(
            import: new FakeSteamAccountPageImport { Report = report },
            picker: new FakeSteamPageFilePicker(
                SteamAccountPageFixtures.PathOf(SteamAccountPageFixtures.LicensesPage1),
                SteamAccountPageFixtures.PathOf(SteamAccountPageFixtures.PurchaseHistory)));

        await vm.ImportFromSavedPagesCommand.ExecuteAsync(null);
        Assert.True(vm.HasResult);
        return vm;
    }
}

/// <summary>
/// The embedded-session route, faked. Records every request it is handed, which
/// is what makes the consent assertion possible without a browser.
/// </summary>
internal sealed class FakeSteamPageHarvester : ISteamAccountPageHarvester
{
    public string Name => "fake";

    public bool Available { get; set; } = true;

    /// <summary>Held open to keep a harvest "in progress" for as long as a test needs.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public SteamPageHarvestResult Result { get; set; } = SteamPageHarvestResult.Cancelled();

    public int AvailabilityChecks { get; private set; }

    public int HarvestCalls { get; private set; }

    public List<SteamPageHarvestRequest> Requests { get; } = [];

    public ValueTask<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        AvailabilityChecks++;
        return ValueTask.FromResult(Available);
    }

    public async Task<SteamPageHarvestResult> HarvestAsync(
        SteamPageHarvestRequest request, CancellationToken ct = default)
    {
        HarvestCalls++;
        Requests.Add(request);

        if (Gate is not null)
        {
            await Gate.Task.WaitAsync(ct);
        }

        return Result;
    }
}

/// <summary>The importer, faked: records what it was handed and answers a fixed report.</summary>
internal sealed class FakeSteamAccountPageImport : ISteamAccountPageImport
{
    public SteamAccountPageImportReport Report { get; set; } = new()
    {
        LicensesOutcome = SteamAccountPageParseOutcome.Parsed,
        LicenseRowsParsed = 1,
        AcquisitionsMatched = 1,
        OwnershipsFilled = 1,
    };

    public int Calls { get; private set; }

    public List<SteamAccountPages> Seen { get; } = [];

    public Task<SteamAccountPageImportReport> ImportAsync(
        SteamAccountPages pages, CancellationToken ct = default)
    {
        Calls++;
        Seen.Add(pages);
        return Task.FromResult(Report);
    }
}

/// <summary>The OS file dialog, faked: answers a fixed set of paths.</summary>
internal sealed class FakeSteamPageFilePicker : ISteamAccountPageFilePicker
{
    private readonly string[] _paths;

    public FakeSteamPageFilePicker(params string[] paths) => _paths = paths;

    public int Calls { get; private set; }

    public Task<IReadOnlyList<string>> PickAsync(string title, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult<IReadOnlyList<string>>(_paths);
    }
}

/// <summary>
/// An import screen for tests that need one only because
/// <see cref="MainWindowViewModel"/> requires it. No harvester, no picker and a
/// fake importer, so nothing is read and nothing is written.
/// </summary>
internal static class DetachedAccountImport
{
    public static SteamAccountImportViewModel Create() => new(
        new FakeSteamAccountPageImport(),
        new SteamAccountPageFileLoader(),
        new FakeSteamPageFilePicker());
}
