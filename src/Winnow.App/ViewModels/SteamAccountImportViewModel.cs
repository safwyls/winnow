using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;
using Winnow.Core.Auth;
using Winnow.Core.Ingest;

namespace Winnow.App.ViewModels;

/// <summary>
/// One row of the result table: a label and a number already formatted with
/// group separators. Rendered in IBM Plex Mono with tabular figures.
/// </summary>
/// <param name="Label">The uppercase count label, e.g. LICENCES FOUND.</param>
/// <param name="Value">The count, pre-formatted with group separators.</param>
public sealed record ImportCountRow(string Label, string Value);

/// <summary>
/// One file the user picked and what happened to it, shown beside the
/// filename in the picked-files list.
/// </summary>
/// <param name="Name">The filename only, never the full path.</param>
/// <param name="Outcome">The short uppercase status label shown beside the filename.</param>
/// <param name="IsProblem">True for anything that is not a successful load.</param>
public sealed record PickedFileRow(string Name, string Outcome, bool IsProblem);

/// <summary>
/// View model for the M5 Steam account-page import screen. Presents two
/// routes to the same two Steam account pages, both converging on the same
/// importer and the same parser (ROADMAP §4.7 amendment, condition 4). Reads
/// nothing and writes nothing itself; it raises commands (§5.1).
///
/// <para>Opening the screen starts neither route. The consent flag on the
/// harvest request is set in the body of the sign-in command and nowhere
/// else, so the mechanism cannot grant itself the consent that lets it open
/// a window. A closed window and an un-signed-in session are reported as
/// facts, never as failures.</para>
/// </summary>
public partial class SteamAccountImportViewModel : ObservableObject
{
    /// <summary>
    /// How many licence rows one saved page can plausibly hold before the count
    /// is better explained by pagination than by a small account.
    ///
    /// <para><b>90, not 100, and the difference is the whole point.</b> Steam's
    /// licences view pages at an advertised hundred but renders about 96 of
    /// them — measured 2026-08-29 against a real account, where a page reading
    /// "1-100 of 979" held 96 rows. A threshold of 100 could therefore never
    /// fire, so the heuristic it guards was dead code: every saved page one of a
    /// paginated list slipped through as a small complete account. The margin
    /// sits below the real render count and above any plausible library that
    /// genuinely ends there.</para>
    /// </summary>
    private const int LicensesPageSizeHint = 90;

    private readonly ISteamAccountPageImport _import;
    private readonly ISteamAccountPageFileLoader _loader;
    private readonly ISteamAccountPageFilePicker _picker;
    private readonly ISteamAccountPageHarvester? _harvester;

    private SteamAccountPageImportReport? _report;
    private SteamAccountPageSource? _reportSource;

    /// <param name="import">The App-layer importer both routes converge on.</param>
    /// <param name="loader">The saved-file route's reader.</param>
    /// <param name="picker">The OS file dialog, behind a seam so the routes test without a window.</param>
    /// <param name="harvester">
    /// The embedded-session route. Optional: a host that registered no harvester
    /// gets a screen that says this route cannot run here, and the saved-file
    /// route is unaffected.
    /// </param>
    public SteamAccountImportViewModel(
        ISteamAccountPageImport import,
        ISteamAccountPageFileLoader loader,
        ISteamAccountPageFilePicker picker,
        ISteamAccountPageHarvester? harvester = null)
    {
        _import = import;
        _loader = loader;
        _picker = picker;
        _harvester = harvester;
    }

    // ══ Header ══════════════════════════════════════════════════════════════

    public string Title => SteamAccountImportCopy.Title;

    public string IntroMessage => SteamAccountImportCopy.Intro;

    /// <summary>The segment label on the settings surface, beside STORES and APPEARANCE.</summary>
    public string RailRow => SteamAccountImportCopy.RailRow;

    public string RailTooltip => SteamAccountImportCopy.RailTooltip;

    // ══ Route A — sign in inside Winnow ═════════════════════════════════════

    public string SignInRouteHeading => SteamAccountImportCopy.SignInRouteHeading;

    public string SignInRouteButtonText => SteamAccountImportCopy.SignInRouteButton;

    public string SignInRouteExplanation => SteamAccountImportCopy.SignInRouteExplanation;

    public string SignInBusyMessage => SteamAccountImportCopy.SignInBusy;

    public string SignInUnavailableMessage => SteamAccountImportCopy.SignInUnavailable;

    /// <summary>
    /// Whether the embedded browser reports that it can run here. Advisory
    /// only: the button stays live either way, because a route drawn as a dead
    /// control is a route presented as second-class, and because the runtime can
    /// be installed while this screen is open.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSignInUnavailable))]
    public partial bool SignInRouteAvailable { get; set; } = true;

    public bool ShowSignInUnavailable => !SignInRouteAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(ShowSignInBusy))]
    [NotifyCanExecuteChangedFor(nameof(ImportFromSignInCommand), nameof(ImportFromSavedPagesCommand))]
    public partial bool IsSigningIn { get; set; }

    public bool ShowSignInBusy => IsSigningIn;

    // ══ Route B — save the pages yourself ═══════════════════════════════════

    public string SavedPagesRouteHeading => SteamAccountImportCopy.SavedPagesRouteHeading;

    public string SavedPagesRouteButtonText => SteamAccountImportCopy.SavedPagesRouteButton;

    public string SavedPagesRouteExplanation => SteamAccountImportCopy.SavedPagesRouteExplanation;

    public string SavedPagesHintMessage => SteamAccountImportCopy.SavedPagesLoadMoreHint;

    /// <summary>
    /// The licences page's own limitation, which is the harder of the two and
    /// the one no click fixes: Steam paginates it a hundred at a time, so a
    /// saved file holds one page whatever the user does before saving.
    /// </summary>
    public string SavedPagesLicensesHintMessage => SteamAccountImportCopy.SavedPagesLicensesHint;

    public string SavedPagesBusyMessage => SteamAccountImportCopy.SavedPagesBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(ShowSavedPagesBusy))]
    [NotifyCanExecuteChangedFor(nameof(ImportFromSignInCommand), nameof(ImportFromSavedPagesCommand))]
    public partial bool IsReadingFiles { get; set; }

    public bool ShowSavedPagesBusy => IsReadingFiles;

    /// <summary>The files the last pick offered, with what happened to each.</summary>
    public ObservableCollection<PickedFileRow> PickedFiles { get; } = [];

    public bool ShowPickedFiles => PickedFiles.Count > 0;

    /// <summary>
    /// At least one picked file was a second copy of a page kind already read.
    /// Worth its own sentence rather than only a label in a narrow column,
    /// because the likeliest way to produce one is saving licences pages 1 and 2
    /// — in which case the second page was not imported and the user's whole
    /// reason for picking two files went unmet silently.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDuplicatePages))]
    public partial bool HasDuplicatePages { get; set; }

    public bool ShowDuplicatePages => HasDuplicatePages;

    public string DuplicatePagesMessage => SteamAccountImportCopy.DuplicatePagesNotice;

    // ══ Shared state ════════════════════════════════════════════════════════

    /// <summary>Either route is running. Both buttons are held while one is.</summary>
    public bool IsBusy => IsSigningIn || IsReadingFiles;

    /// <summary>
    /// The neutral report on the last attempt: what happened, stated as a fact.
    /// A closed window and an unsigned-in session both land here, never in
    /// <see cref="ProblemMessage"/>, because neither is a fault.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNotice))]
    public partial string? NoticeMessage { get; set; }

    public bool ShowNotice => !string.IsNullOrWhiteSpace(NoticeMessage);

    /// <summary>Something went wrong and the sentence says what. Amber, never Flare.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowProblem))]
    public partial string? ProblemMessage { get; set; }

    public bool ShowProblem => !string.IsNullOrWhiteSpace(ProblemMessage);

    // ══ The result ══════════════════════════════════════════════════════════

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSkipped), nameof(ShowNothingApplied))]
    public partial bool HasResult { get; set; }

    public string ResultHeading => SteamAccountImportCopy.ResultHeading;

    public string SkippedHeading => SteamAccountImportCopy.SkippedHeading;

    public string NothingAppliedMessage => SteamAccountImportCopy.NothingApplied;

    /// <summary>The counts, in the order the question is asked: read, matched, applied.</summary>
    public ObservableCollection<ImportCountRow> Counts { get; } = [];

    /// <summary>Only the skip reasons that actually happened. A row of zeroes explains nothing.</summary>
    public ObservableCollection<ImportCountRow> Skipped { get; } = [];

    public bool ShowSkipped => Skipped.Count > 0;

    /// <summary>Rows were read and none of them filled anything in. Not a failure.</summary>
    public bool ShowNothingApplied => HasResult && _report is { OwnershipsFilled: 0 };

    /// <summary>
    /// Steam's paginator advertised a licence total larger than the rows it
    /// rendered. Shown as a neutral line under the counts and never as a
    /// warning: it is true of a complete capture as often as a partial one, so
    /// nothing may be concluded from it. It is here only because a user reading
    /// "979" on Steam and a smaller number here would otherwise conclude the
    /// import lost their games.
    /// </summary>
    public bool ShowLicensesCountMismatch =>
        HasResult
        && _report is { LicensesParsed: true, LicensesReportedTotal: { } total } report
        && total != report.LicenseRowsParsed + report.LicenseRowsSkippedByParser;

    public string LicensesCountMismatchMessage => SteamAccountImportCopy.LicensesCountMismatchNote;

    /// <summary>
    /// What this pass could not see of the purchase history, or null when it saw
    /// all of it. One property per page rather than one per route: the FACT of
    /// truncation is reported on both routes, and only the REMEDY differs, so a
    /// route can change the sentence but never suppress it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHistoryTruncation))]
    public partial string? HistoryTruncationMessage { get; set; }

    public bool ShowHistoryTruncation => !string.IsNullOrWhiteSpace(HistoryTruncationMessage);

    /// <summary>The same, for the licences list. Symmetric with the history by construction.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLicensesTruncation))]
    public partial string? LicensesTruncationMessage { get; set; }

    public bool ShowLicensesTruncation => !string.IsNullOrWhiteSpace(LicensesTruncationMessage);

    /// <summary>
    /// The purchase history's truncation sentence, chosen from the route and the
    /// reason the gathering stopped.
    ///
    /// <para>On the embedded route the walk's own stop reason is authoritative
    /// and outranks the parser: <see cref="SteamLoadMoreDecision.ReachedCap"/>
    /// means Winnow stopped rather than Steam, which is a different fact from a
    /// walk that ran out of road. A walk that stopped producing rows, or one
    /// that finished while the document still advertises more, are the same fact
    /// to the reader — this run did not get everything and a rerun is worth
    /// it — so they share a sentence.</para>
    ///
    /// <para>On the saved-file route there is no walk, so the parser is the only
    /// witness and the remedy — click "load more transactions" before saving —
    /// is advice only that route's user can act on.</para>
    /// </summary>
    private static string? HistoryTruncation(
        SteamAccountPageImportReport report,
        SteamAccountPageSource source,
        SteamPageHarvestResult? harvest)
    {
        if (source == SteamAccountPageSource.SavedFile)
        {
            return report.HistoryTruncated ? SteamAccountImportCopy.HistoryTruncatedNotice : null;
        }

        if (harvest?.LoadMoreStoppedBecause == SteamLoadMoreDecision.ReachedCap)
        {
            return SteamAccountImportCopy.SignInHistoryReachedCapNotice;
        }

        // The session watched the control stop offering rows in a live DOM, which
        // outranks anything the parser reads off the captured markup: the parser
        // can only see inline style, so a load-more button hidden by a stylesheet
        // still looks live to it. A walk that ran out is complete.
        if (harvest?.HistoryLoadedToEnd == true)
        {
            return null;
        }

        var stalled = harvest?.LoadMoreStoppedBecause == SteamLoadMoreDecision.Stalled;
        return stalled || report.HistoryTruncated
            ? SteamAccountImportCopy.SignInHistoryIncompleteNotice
            : null;
    }

    /// <summary>
    /// The licences list's truncation sentence. The same matrix as
    /// <see cref="HistoryTruncation"/>, over the licences walk's own stop reason.
    ///
    /// <para>The saved-file arm carries one extra witness the history has no need
    /// of: a saved licences page can arrive with no paginator in it at all, so a
    /// row count that stops dead on the hundred-row page boundary is treated as
    /// evidence of pagination. That heuristic is saved-file only — the embedded
    /// walk reports what it did, so it never has to be guessed at.</para>
    /// </summary>
    private static string? LicensesTruncation(
        SteamAccountPageImportReport report,
        SteamAccountPageSource source,
        SteamPageHarvestResult? harvest)
    {
        if (source == SteamAccountPageSource.SavedFile)
        {
            return report.LicensesTruncated || LooksLikeOneLicensesPage(report)
                ? SteamAccountImportCopy.LicensesTruncatedNotice
                : null;
        }

        if (harvest?.LicensesStoppedBecause == SteamLoadMoreDecision.ReachedCap)
        {
            return SteamAccountImportCopy.SignInLicensesReachedCapNotice;
        }

        // Same precedence, and here it fixes a false alarm rather than a missed
        // one: Steam's paginator advertises a total it does not render, so a
        // complete walk parses fewer rows than the page claims. A session that
        // watched the paginator run out is complete whatever any count says.
        if (harvest?.LicensesWalkedToEnd == true)
        {
            return null;
        }

        var stalled = harvest?.LicensesStoppedBecause == SteamLoadMoreDecision.Stalled;
        return stalled || report.LicensesTruncated
            ? SteamAccountImportCopy.SignInLicensesIncompleteNotice
            : null;
    }

    /// <summary>
    /// A saved licences page with no paginator whose rows stop on the page
    /// boundary. Steam serves a hundred licences at a time, so a file that holds
    /// exactly that many is page one of several far more often than it is a
    /// hundred-licence account.
    /// </summary>
    private static bool LooksLikeOneLicensesPage(SteamAccountPageImportReport report)
        => report.LicensesParsed
        && report.LicensesReportedTotal is null
        && report.LicenseRowsParsed + report.LicenseRowsSkippedByParser >= LicensesPageSizeHint;

    // ══ Commands ════════════════════════════════════════════════════════════

    /// <summary>
    /// Asks the harvester whether it could run here, so the screen can say so
    /// before anyone presses anything. Opens no window and does no IO.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync(CancellationToken ct)
    {
        if (_harvester is null)
        {
            SignInRouteAvailable = false;
            return;
        }

        try
        {
            SignInRouteAvailable = await _harvester.IsAvailableAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            SignInRouteAvailable = false;
        }
    }

    /// <summary>
    /// Route A. The ONLY place <see cref="SteamPageHarvestRequest.ConsentGranted"/>
    /// is ever set true: it is set here, in the body of the command a person
    /// pressed, having read the paragraph above the button. Nothing else in this
    /// type constructs a request, so the mechanism cannot grant itself the
    /// consent that lets it open a window.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanImport), IncludeCancelCommand = true)]
    private async Task ImportFromSignInAsync(CancellationToken ct)
    {
        ClearLastAttempt();
        IsSigningIn = true;

        try
        {
            if (_harvester is null)
            {
                SignInRouteAvailable = false;
                NoticeMessage = SteamAccountImportCopy.OutcomeUnavailable;
                return;
            }

            SteamPageHarvestResult harvest;
            try
            {
                harvest = await _harvester.HarvestAsync(
                    new SteamPageHarvestRequest { ConsentGranted = true },
                    ct);
            }
            catch (OperationCanceledException)
            {
                NoticeMessage = SteamAccountImportCopy.OutcomeCancelled;
                return;
            }

            Describe(harvest);

            if (harvest.Pages is { IsEmpty: false } pages)
            {
                await ApplyAsync(pages, harvest, ct);
            }
        }
        finally
        {
            IsSigningIn = false;
        }
    }

    /// <summary>
    /// Route B. Picks one or both saved files, reads them, and hands the same
    /// <see cref="SteamAccountPages"/> to the same importer route A uses.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportFromSavedPagesAsync(CancellationToken ct)
    {
        ClearLastAttempt();
        IsReadingFiles = true;

        try
        {
            var paths = await _picker.PickAsync(SteamAccountImportCopy.FilePickerTitle, ct);
            if (paths.Count == 0)
            {
                NoticeMessage = SteamAccountImportCopy.NothingPicked;
                return;
            }

            var loaded = await _loader.LoadAsync(paths, ct);

            foreach (var file in loaded.Files)
            {
                PickedFiles.Add(new PickedFileRow(
                    Path.GetFileName(file.Path),
                    Describe(file.Outcome),
                    file.Outcome != SteamAccountPageFileOutcome.Loaded));
            }

            OnPropertyChanged(nameof(ShowPickedFiles));
            HasDuplicatePages = loaded.Files.Any(
                f => f.Outcome == SteamAccountPageFileOutcome.Duplicate);

            if (!loaded.AnythingLoaded)
            {
                NoticeMessage = SteamAccountImportCopy.NothingRecognized;
                return;
            }

            // No harvest ran, so there is no walk to report on and the parser is
            // the only witness to what the files hold.
            await ApplyAsync(loaded.Pages, harvest: null, ct);
        }
        finally
        {
            IsReadingFiles = false;
        }
    }

    private bool CanImport() => !IsBusy;

    // ══ Both routes converge here ═══════════════════════════════════════════

    private async Task ApplyAsync(
        SteamAccountPages pages, SteamPageHarvestResult? harvest, CancellationToken ct)
    {
        // Awaited, never fired and forgotten: this writes to ownership rows, and
        // a write nobody waits for is a write nobody can report on.
        var report = await _import.ImportAsync(pages, ct);

        _report = report;
        _reportSource = pages.Source;

        HistoryTruncationMessage = HistoryTruncation(report, pages.Source, harvest);
        LicensesTruncationMessage = LicensesTruncation(report, pages.Source, harvest);

        Counts.Clear();
        Add(Counts, SteamAccountImportCopy.LabelLicencesFound, report.LicenseRowsParsed);

        // Steam's own figure, beside the rows actually read, and only when the
        // two differ. In the table rather than in the sentence so both numbers
        // land in Plex Mono and line up under each other (§3).
        if (report.LicensesReportedTotal is { } reportedLicences
            && reportedLicences != report.LicenseRowsParsed + report.LicenseRowsSkippedByParser)
        {
            Add(Counts, SteamAccountImportCopy.LabelLicencesReported, reportedLicences);
        }
        Add(Counts, SteamAccountImportCopy.LabelPurchasesFound, report.HistoryRowsParsed);
        Add(Counts, SteamAccountImportCopy.LabelMatched, report.AcquisitionsMatched);
        Add(Counts, SteamAccountImportCopy.LabelPricesMatched, report.PricesMatched);
        Add(Counts, SteamAccountImportCopy.LabelGamesUpdated, report.OwnershipsFilled);
        Add(Counts, SteamAccountImportCopy.LabelAlreadyComplete, report.OwnershipsAlreadyComplete);

        Skipped.Clear();
        AddIfAny(Skipped, SteamAccountImportCopy.SkipBundles, report.SkippedBundleRows);
        AddIfAny(Skipped, SteamAccountImportCopy.SkipRefunds, report.SkippedRefundedRows);
        AddIfAny(Skipped, SteamAccountImportCopy.SkipGiftsAndInGame, report.SkippedNonPurchaseRows);
        AddIfAny(Skipped, SteamAccountImportCopy.SkipWallet, report.SkippedNonProductRows);
        AddIfAny(Skipped, SteamAccountImportCopy.SkipAmbiguous, report.SkippedAmbiguousTitle);
        AddIfAny(Skipped, SteamAccountImportCopy.SkipNoMatch, report.SkippedNoOwnershipMatch);
        AddIfAny(Skipped, SteamAccountImportCopy.SkipDisagreed, report.SkippedConflictingRows);

        // A page Steam has redesigned is a problem with a reason, not a silent
        // zero. Reported after the counts so the half that did work still shows.
        var problems = new List<string>(2);
        if (report.LicensesUnrecognized)
        {
            problems.Add(Reason(SteamAccountImportCopy.LicensesNotRecognized, report.LicensesFailureReason));
        }

        if (report.HistoryUnrecognized)
        {
            problems.Add(Reason(SteamAccountImportCopy.HistoryNotRecognized, report.HistoryFailureReason));
        }

        if (problems.Count > 0)
        {
            ProblemMessage = string.Join(" ", problems);
        }

        HasResult = true;
        OnPropertyChanged(nameof(ShowSkipped));
    }

    private void ClearLastAttempt()
    {
        NoticeMessage = null;
        ProblemMessage = null;
        HistoryTruncationMessage = null;
        LicensesTruncationMessage = null;
        HasDuplicatePages = false;
        HasResult = false;
        _report = null;
        _reportSource = null;
        Counts.Clear();
        Skipped.Clear();
        PickedFiles.Clear();
        OnPropertyChanged(nameof(ShowSkipped));
        OnPropertyChanged(nameof(ShowPickedFiles));
    }

    /// <summary>
    /// Maps every harvest outcome onto one sentence. Cancelled and NoSession are
    /// notices rather than problems on purpose: closing a window is a decision,
    /// and never signing in is a state whose remedy is to sign in.
    /// </summary>
    private void Describe(SteamPageHarvestResult harvest)
    {
        switch (harvest.Outcome)
        {
            case SteamPageHarvestOutcome.Captured:
                NoticeMessage = SteamAccountImportCopy.OutcomeCaptured;
                break;

            case SteamPageHarvestOutcome.Partial:
                NoticeMessage = SteamAccountImportCopy.OutcomePartial;
                break;

            case SteamPageHarvestOutcome.Cancelled:
                NoticeMessage = SteamAccountImportCopy.OutcomeCancelled;
                break;

            case SteamPageHarvestOutcome.NoSession:
                NoticeMessage = SteamAccountImportCopy.OutcomeNoSession;
                break;

            case SteamPageHarvestOutcome.Unavailable:
                SignInRouteAvailable = false;
                NoticeMessage = SteamAccountImportCopy.OutcomeUnavailable;
                break;

            case SteamPageHarvestOutcome.Failed:
            default:
                ProblemMessage = SteamAccountImportCopy.OutcomeFailed;
                break;
        }
    }

    private static string Describe(SteamAccountPageFileOutcome outcome) => outcome switch
    {
        SteamAccountPageFileOutcome.Loaded => SteamAccountImportCopy.FileLoaded,
        SteamAccountPageFileOutcome.NotFound => SteamAccountImportCopy.FileNotFound,
        SteamAccountPageFileOutcome.Unreadable => SteamAccountImportCopy.FileUnreadable,
        SteamAccountPageFileOutcome.Duplicate => SteamAccountImportCopy.FileDuplicate,
        _ => SteamAccountImportCopy.FileNotRecognized,
    };

    private static string Reason(string message, string? detail)
        => string.IsNullOrWhiteSpace(detail) ? message : $"{message} {detail}.";

    private static void Add(ObservableCollection<ImportCountRow> rows, string label, int value)
        => rows.Add(new ImportCountRow(label, value.ToString("N0")));

    private static void AddIfAny(ObservableCollection<ImportCountRow> rows, string label, int value)
    {
        if (value > 0)
        {
            Add(rows, label, value);
        }
    }
}
