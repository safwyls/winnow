using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.Core.Domain;
using Winnow.Core.Merging;
using Winnow.Core.Repositories;
using Winnow.Covers;
using Winnow.Covers.Igdb;
using Winnow.Resolve;

namespace Winnow.App.ViewModels;

/// <summary>
/// The Same Game screen. One pane with a 48px header and a two-segment
/// control, REVIEW / HISTORY, in the same grammar the settings surface uses
/// for PLATFORMS / APPEARANCE. REVIEW is the confirm queue and nothing else.
/// HISTORY holds every already-answered pair: applied merges with their undo,
/// plus a section of pairs confirmed under the old two-step flow that were
/// never applied (absent once drained).
///
/// <para>Answering "Same game" now merges the pair where it stands, and undo
/// is offered on the outcome report. Undo is what makes applying on answer
/// safe.</para>
/// </summary>
public partial class MergeQueueViewModel : ObservableObject
{
    /// <summary>Cover geometry from §6: "two covers side by side at 200×300".</summary>
    public const double CoverWidth = 200;

    /// <summary>2:3 portrait, matching the capsule geometry the grid uses.</summary>
    public const double CoverHeight = CoverWidth * 1.5;

    private readonly IMergeCandidateRepository _candidates;
    private readonly IReleaseRepository _releases;
    private readonly IWorkRepository _works;
    private readonly MergeExecutor _merges;
    private readonly ICoverCache? _covers;
    private readonly IResolveStateRepository? _resolveState;

    /// <summary>Display resolution the view last asked for; 0 until it attaches.</summary>
    private double _coverWidthPixels;

    private bool _loaded;

    // Cached release-to-work membership. AffectedBy reads this to decide which
    // cards a merge could have changed, avoiding a database round-trip on the
    // answer path where repeated keypresses must stay cheap. Updated in place
    // when a merge moves the absorbed work's releases to the surviving work;
    // re-querying would reintroduce the cost this cache exists to avoid.
    private readonly Dictionary<long, long> _workIdOfRelease = [];

    // MergeExecutor is required, not optional. An engine registered in the
    // container and resolved nowhere is indistinguishable from one that works;
    // omitting it must break the container at startup rather than render a
    // screen whose answers quietly write nothing.
    public MergeQueueViewModel(
        IMergeCandidateRepository candidates,
        IReleaseRepository releases,
        IWorkRepository works,
        MergeExecutor merges,
        ICoverCache? covers = null,
        IResolveStateRepository? resolveState = null)
    {
        _candidates = candidates;
        _releases = releases;
        _works = works;
        _merges = merges;
        _covers = covers;
        _resolveState = resolveState;
    }

    // ── Which surface is up ──────────────────────────────────────────────────

    /// <summary>
    /// The segmented control's state. Review is the default. History is a
    /// second view of one screen, not a second place in the rail; the rail's
    /// Volt edge marks exactly one location (§12.2), and a second rail row
    /// would make the rail answer "where am I" twice for one feature.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReviewVisible))]
    public partial bool IsHistoryVisible { get; set; }

    /// <summary>True when the review queue is the visible surface.</summary>
    public bool IsReviewVisible => !IsHistoryVisible;

    /// <summary>Switches the segmented control to REVIEW.</summary>
    [RelayCommand]
    private void ShowReview() => IsHistoryVisible = false;

    // Recomputes on the way in. Reversibility depends on every merge applied
    // after a given one, so a verdict computed at the last load is a claim
    // about a database that may since have moved. A cached "you can undo
    // this" becomes a lie the moment a later merge consumes the survivor.
    [RelayCommand]
    private async Task ShowHistoryAsync(CancellationToken ct)
    {
        IsHistoryVisible = true;
        await RefreshAppliedAsync(ct);
    }

    // ── The review queue ─────────────────────────────────────────────────────

    /// <summary>The pending soft-match pairs, sorted strongest first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(PendingCount), nameof(PendingCountText), nameof(HasPending),
        nameof(ShowEmpty), nameof(RowOpacity), nameof(ShowOutstandingNotice))]
    public partial IReadOnlyList<MergeCandidateViewModel> Candidates { get; set; } = [];

    /// <summary>The pair the user is currently looking at, or null when the queue is empty.</summary>
    [ObservableProperty]
    public partial MergeCandidateViewModel? SelectedCandidate { get; set; }

    /// <summary>Number of pairs still waiting for an answer.</summary>
    public int PendingCount => Candidates.Count;

    /// <summary>Plex Mono, tabular, grouped — every number in the app (§3).</summary>
    public string PendingCountText => PendingCount.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>True when there are pending candidates to review.</summary>
    public bool HasPending => PendingCount > 0;

    /// <summary>Dims to 40% when empty so the rail row stays visible but recedes.</summary>
    public double RowOpacity => HasPending ? 1.0 : 0.4;

    /// <summary>True once the screen has loaded and the queue is empty.</summary>
    public bool ShowEmpty => _loaded && PendingCount == 0;

    /// <summary>True once a soft-match sweep has completed. False when unknown or unregistered.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyMessage))]
    public partial bool HasCompletedSweep { get; set; }

    /// <summary>Empty-state message: distinguishes "sweep found nothing" from "sweep hasn't run yet".</summary>
    public string EmptyMessage => HasCompletedSweep
        ? "Nothing to review. No ambiguous pairs found."
        : "Nothing to review yet. Still comparing your library for duplicates.";

    /// <summary>Standing explanation under the screen title.</summary>
    public string IntroMessage => MergeCopy.QueueIntro;

    // ── The pairs answered under the previous two-step flow ──────────────────

    /// <summary>
    /// Pairs confirmed under the old two-step flow that were never applied.
    /// Nothing this build does adds to this list, because answering now
    /// applies. It exists so an install predating the change has somewhere to
    /// finish, and it is absent altogether once drained.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(OutstandingCount), nameof(OutstandingCountText), nameof(HasOutstanding),
        nameof(ShowOutstandingNotice), nameof(OutstandingNoticeMessage))]
    public partial IReadOnlyList<MergeApplyViewModel> Outstanding { get; set; } = [];

    /// <summary>Number of confirmed pairs waiting to be applied.</summary>
    public int OutstandingCount => Outstanding.Count;

    /// <summary>Plex Mono, tabular, grouped — every number in the app (§3).</summary>
    public string OutstandingCountText => OutstandingCount.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>True when confirmed pairs are waiting to be applied.</summary>
    public bool HasOutstanding => OutstandingCount > 0;

    // Two segments can hide work a single scroll could not. An empty queue
    // beside unapplied leftovers is the one state that reads as finished while
    // the History segment still has outstanding pairs; the notice prevents a
    // user from leaving the screen early.
    public bool ShowOutstandingNotice => ShowEmpty && HasOutstanding;

    // Count rendered inline in the data face (Plex Mono tnum, §3).
    public string OutstandingNoticeMessage => string.Format(
        CultureInfo.CurrentCulture, MergeCopy.OutstandingNoticeFormat, OutstandingCountText);

    // ── History and undo ─────────────────────────────────────────────────────

    /// <summary>
    /// Every applied merge, newest first, each with its undo verdict. Rebuilt
    /// from scratch on every load because reversibility depends on every merge
    /// applied after a given one; a row's enabled state is a fact about the whole
    /// log at this instant and is never carried across a load.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHistory), nameof(ShowHistoryEmpty))]
    public partial IReadOnlyList<MergeHistoryRowViewModel> History { get; set; } = [];

    /// <summary>True when at least one merge has been applied.</summary>
    public bool HasHistory => History.Count > 0;

    /// <summary>True once the screen has loaded and no merges have been applied.</summary>
    public bool ShowHistoryEmpty => _loaded && History.Count == 0;

    // ── What the last act actually did ───────────────────────────────────────

    /// <summary>
    /// What the last answer or undo actually did. Written from the outcome the
    /// engine returned, never from what was asked for.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReport), nameof(ReportUndoAutomationName))]
    public partial string? ReportMessage { get; set; }

    /// <summary>True when there is an outcome to display.</summary>
    public bool HasReport => !string.IsNullOrEmpty(ReportMessage);

    /// <summary>
    /// The merge the report is about, when it can still be reversed. Set from
    /// the undo plan the engine returns after the apply, never assumed from
    /// the fact that an apply succeeded.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUndoReport))]
    public partial long? ReportUndoApplicationId { get; set; }

    /// <summary>The title the undo would restore, shown on the undo control.</summary>
    [ObservableProperty]
    public partial string? ReportUndoTitle { get; set; }

    /// <summary>True when the report's merge can still be reversed.</summary>
    public bool CanUndoReport => ReportUndoApplicationId is not null;

    // ── Chrome the view binds to ─────────────────────────────────────────────

    /// <summary>Small uppercase label at the left of the screen's header strip.</summary>
    public string ScreenLabel => MergeCopy.ScreenLabel;

    /// <summary>Label on the segment showing the review queue.</summary>
    public string ReviewSegmentLabel => MergeCopy.SegmentReview;

    /// <summary>Label on the segment showing applied merges.</summary>
    public string HistorySegmentLabel => MergeCopy.SegmentHistory;

    /// <summary>Tooltip on the review segment.</summary>
    public string ReviewSegmentTooltip => MergeCopy.SegmentReviewTooltip;

    /// <summary>Tooltip on the history segment.</summary>
    public string HistorySegmentTooltip => MergeCopy.SegmentHistoryTooltip;

    /// <summary>Section heading for pairs answered before merges applied on confirm.</summary>
    public string ApplyHeading => MergeCopy.ApplyHeading;

    /// <summary>Introduction under that heading.</summary>
    public string ApplyIntro => MergeCopy.ApplyIntro;

    /// <summary>Label for the batch apply control.</summary>
    public string ApplyAllButtonText => MergeCopy.ApplyAllButton;

    /// <summary>Tooltip on the batch apply control.</summary>
    public string ApplyAllTooltip => MergeCopy.ApplyAllTooltip;

    /// <summary>Section heading for the history of applied merges.</summary>
    public string HistoryHeading => MergeCopy.HistoryHeading;

    /// <summary>Introduction under the history heading.</summary>
    public string HistoryIntro => MergeCopy.HistoryIntro;

    /// <summary>Empty state for the history section.</summary>
    public string HistoryEmptyMessage => MergeCopy.HistoryEmpty;

    /// <summary>Label on the undo control beside the outcome report.</summary>
    public string ReportUndoButtonText => MergeCopy.ReportUndoButton;

    /// <summary>Tooltip on that control.</summary>
    public string ReportUndoTooltipText => MergeCopy.ReportUndoTooltip;

    // The report sentence names no action on its own, so a screen reader would
    // announce a button indistinguishable from a statement. Prefixing the verb
    // makes the target unique, matching how history rows build their automation
    // names (§8).
    public string ReportUndoAutomationName =>
        string.Create(CultureInfo.CurrentCulture, $"{MergeCopy.ReportUndoButton}. {ReportMessage}");

    /// <summary>Tooltip on "Different games", stating permanence.</summary>
    public string DifferentGamesTooltip => MergeCopy.DifferentGamesTooltip;

    // ── Loading ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct)
    {
        // Must be read before candidates so the empty state knows if the matcher has run.
        HasCompletedSweep = _resolveState is not null
            && await _resolveState.GetLastSoftMatchSweepAsync(ct) is not null;

        // Sort here so review order is owned by this screen, not just the SQL query.
        var pending = await _candidates.GetPendingAsync(ct);

        // Both lists are rebuilt from the engine on every load. Nothing about
        // a plan or an undo verdict survives a reload.
        var outstanding = await _merges.OutstandingAsync(ct);
        var history = await _merges.HistoryAsync(ct);

        var releaseIds = new HashSet<long>();
        foreach (var candidate in pending)
        {
            releaseIds.Add(candidate.LeftReleaseId);
            releaseIds.Add(candidate.RightReleaseId);
        }

        foreach (var plan in outstanding)
        {
            AddIfPresent(releaseIds, plan.LeftReleaseId);
            AddIfPresent(releaseIds, plan.RightReleaseId);
        }

        var (titles, coverKeys, workIds) = await DescribeReleasesAsync(releaseIds, ct);

        var cards = new List<MergeCandidateViewModel>(pending.Count);
        foreach (var candidate in pending)
        {
            cards.Add(MergeCandidateViewModel.Create(candidate, titles, coverKeys, _covers));
        }

        cards.Sort(static (a, b) =>
        {
            var byScore = b.Score.CompareTo(a.Score);
            return byScore != 0 ? byScore : a.Id.CompareTo(b.Id);
        });

        _loaded = true;
        Candidates = cards;
        Outstanding = BuildOutstanding(outstanding, titles, workIds);
        History = BuildHistory(history);

        // Titles already fetched: a plan's two release ids are the candidate
        // row's own two columns, so the describe pass above covers them.
        await RefreshPreviewsAsync(cards, titles, workIds, ct);

        // Candidates, not the local cards list: cards was built before the
        // prune, so selecting from it could put the cursor on a card that
        // is no longer on screen.
        Select(Candidates.Count > 0 ? Candidates[0] : null);
        RequestCovers(_coverWidthPixels);
    }

    // ── Answering ────────────────────────────────────────────────────────────

    // Confirming applies. The status write and the merge are two statements
    // rather than one transaction, so a crash between them leaves a confirmed
    // pair nothing has applied, which is exactly the state the history
    // surface's leftover section exists to finish. The IsDecided latch is set
    // before the first await so a double click cannot write twice.
    [RelayCommand]
    private async Task SameGameAsync(MergeCandidateViewModel? candidate, CancellationToken ct)
    {
        if (candidate is null || candidate.IsDecided)
        {
            return;
        }

        // Latch before await to prevent double-writes from rapid clicks.
        candidate.IsDecided = true;
        await _candidates.SetStatusAsync(candidate.Id, MergeCandidateStatuses.Confirmed, ct);

        MergeOutcome outcome;
        try
        {
            outcome = await _merges.ApplyAsync(candidate.Id, ct);
        }
        catch (InvalidOperationException)
        {
            // The merge repository throws to roll back its transaction; the
            // cascade tripwire and stranded-achievements check abort this way
            // because a thrown exception is the one path SQLite guarantees a
            // full rollback, making the throw the safety contract rather than a
            // fault. Uncaught, it reaches the UI thread through the relay
            // command, and this app installs no unhandled-exception handler, so
            // the window would close over a database that was left intact.
            // Catching here reports the failure in the same Amber block a
            // refusal uses, and releases the latch so the pair stays answerable
            // rather than stranded behind two dead buttons.
            ReportUndoApplicationId = null;
            ReportUndoTitle = null;
            ReportMessage = MergeCopy.AppliedFailed;
            candidate.IsDecided = false;

            // This is the one answer path that leaves a confirmed pair with
            // nothing applied, which is exactly the state the HISTORY segment's
            // leftover section and its count exist to surface. The success path
            // skips this refresh because it never creates a leftover; skipping
            // it here too would leave the segment count stale and the pair
            // invisible until something else reloaded the screen. A rollback
            // is rare, so the cost this adds is never felt on the path that
            // matters (the repeated-keypress answer path below).
            await RefreshAppliedAsync(ct);
            return;
        }

        await ReportOutcomeAsync(outcome, candidate.Preview, ct);

        Remove(candidate);

        // After Remove so the answered card is excluded. Before Remove it was
        // included, planned, and then discarded by RefreshPreviewsAsync's
        // live-set filter, one wasted database round-trip per answer. Only the
        // cards whose releases or work the merge touched are returned; replanning
        // every remaining card froze the UI for ~2 s at 200 pairs (measured),
        // because Microsoft.Data.Sqlite completes synchronously and this is the
        // screen worked down with repeated S keypresses. A card none of the
        // merge's release or work ids reach reads the same rows it read before,
        // so its plan cannot have changed.
        var affected = AffectedBy(outcome);

        // History and leftovers live on the other segment and are recomputed
        // on entry, so the answer path does not rebuild them.
        await RefreshPreviewsAsync(affected, null, null, ct);
    }

    /// <summary>Writes <c>rejected</c> (permanent), applies nothing, and removes the pair.</summary>
    [RelayCommand]
    private async Task DifferentGamesAsync(MergeCandidateViewModel? candidate, CancellationToken ct)
    {
        if (candidate is null || candidate.IsDecided)
        {
            return;
        }

        candidate.IsDecided = true;
        await _candidates.SetStatusAsync(candidate.Id, MergeCandidateStatuses.Rejected, ct);
        Remove(candidate);
    }

    // Verb, mode and refusal come from the outcome the engine returned, not
    // from what was asked, so a refusal is always surfaced. Titles come from
    // the preview the card was showing, not re-read from the outcome; a second
    // describe pass would cost a round-trip on the answer path that must stay
    // cheap, and the preview was re-planned after every write so it is current
    // here. The undo control is armed only when the engine confirms the merge
    // is still reversible.
    private async Task ReportOutcomeAsync(
        MergeOutcome outcome, MergePreviewViewModel? preview, CancellationToken ct)
    {
        ReportUndoApplicationId = null;
        ReportUndoTitle = null;

        if (!outcome.Applied)
        {
            ReportMessage = string.Format(
                CultureInfo.CurrentCulture,
                MergeCopy.AppliedNothingFormat,
                MergeApplyViewModel.RefusalFor(outcome.Plan.Blocker));
            return;
        }

        var surviving = preview?.SurvivingTitle ?? string.Empty;
        var absorbed = preview?.AbsorbedTitle
            ?? MergeApplyViewModel.ReleaseLabel(preview?.AbsorbedReleaseId);

        ReportMessage = string.Format(
            CultureInfo.CurrentCulture,
            MergeCopy.AppliedReportFormat,
            surviving,
            absorbed,
            ModePhrase(outcome.Plan.Mode));

        if (outcome.ApplicationId is { } applicationId
            && (await _merges.PreviewUndoAsync(applicationId, ct)).Reversible)
        {
            ReportUndoApplicationId = applicationId;
            ReportUndoTitle = absorbed;
        }
    }

    // ── Applying the leftovers ───────────────────────────────────────────────

    // Applies one leftover pair and reports the outcome the engine returned. A
    // refused plan writes nothing and says so; it is never silently dropped.
    [RelayCommand]
    private async Task ApplyAsync(MergeApplyViewModel? row, CancellationToken ct)
    {
        if (row is null || !row.CanApply)
        {
            return;
        }

        row.IsApplying = true;

        var outcome = await _merges.ApplyAsync(row.Id, ct);
        ReportUndoApplicationId = null;
        ReportUndoTitle = null;
        ReportMessage = outcome.Applied
            ? string.Format(
                CultureInfo.CurrentCulture,
                MergeCopy.AppliedReportFormat,
                row.SurvivingTitle,
                row.AbsorbedTitle ?? MergeApplyViewModel.ReleaseLabel(row.AbsorbedReleaseId),
                ModePhrase(outcome.Plan.Mode))
            : string.Format(
                CultureInfo.CurrentCulture,
                MergeCopy.AppliedNothingFormat,
                MergeApplyViewModel.RefusalFor(outcome.Plan.Blocker));

        await LoadAsync(ct);
    }

    // The batch path. Each pair is its own transaction, so one pair that cannot
    // merge safely is skipped rather than holding the rest back.
    [RelayCommand]
    private async Task ApplyAllAsync(CancellationToken ct)
    {
        var summary = await _merges.ApplyAllConfirmedAsync(ct);

        ReportUndoApplicationId = null;
        ReportUndoTitle = null;
        ReportMessage = summary.Applied == 0
            ? string.Format(
                CultureInfo.CurrentCulture, MergeCopy.AppliedBatchNoneFormat, summary.Considered)
            : string.Format(
                CultureInfo.CurrentCulture,
                MergeCopy.AppliedBatchFormat,
                summary.Applied, summary.Considered, summary.Skipped);

        await LoadAsync(ct);
    }

    // ── Undo ─────────────────────────────────────────────────────────────────

    // The undo the outcome report offers, which is what makes
    // answering-applies safe: the merge can be reversed from the same line
    // that reported it, without navigating to the history surface.
    [RelayCommand]
    private async Task UndoReportAsync(CancellationToken ct)
    {
        if (ReportUndoApplicationId is not { } applicationId)
        {
            return;
        }

        await UndoApplicationAsync(applicationId, ReportUndoTitle ?? string.Empty, ct);
    }

    [RelayCommand]
    private async Task UndoAsync(MergeHistoryRowViewModel? row, CancellationToken ct)
    {
        if (row is null || !row.CanUndo)
        {
            return;
        }

        row.IsUndoing = true;
        await UndoApplicationAsync(row.ApplicationId, row.AbsorbedTitle ?? row.SurvivingTitle, ct);
    }

    // The one constructive path out of a disabled undo. The control undoes the
    // merge that stands in the way, never the row it sits on.
    [RelayCommand]
    private async Task UndoBlockingAsync(MergeHistoryRowViewModel? row, CancellationToken ct)
    {
        if (row?.BlockingApplicationId is not { } blocking)
        {
            return;
        }

        var target = History.FirstOrDefault(candidate => candidate.ApplicationId == blocking);
        await UndoApplicationAsync(
            blocking, target?.AbsorbedTitle ?? target?.SurvivingTitle ?? string.Empty, ct);
    }

    private async Task UndoApplicationAsync(long applicationId, string restoredTitle, CancellationToken ct)
    {
        ReportUndoApplicationId = null;
        ReportUndoTitle = null;

        try
        {
            var result = await _merges.UndoAsync(applicationId, ct);
            ReportMessage = string.Format(
                CultureInfo.CurrentCulture,
                MergeCopy.UndoneReportFormat,
                restoredTitle,
                result.RowsReinserted + result.RowsRepointedBack + result.RowsRestoredInPlace);
        }
        catch (MergeUndoRefusedException refused)
        {
            // Gate one. The blocker is the sentence the screen already knows how
            // to say, so the user reads the same words here as on the row.
            ReportMessage = string.Format(
                CultureInfo.CurrentCulture,
                MergeCopy.UndoRefusedFormat,
                MergeHistoryRowViewModel.BlockedTextFor(
                    refused.Blocker, null, refused.Plan?.BlockingApplicationId));
        }

        await LoadAsync(ct);
    }

    // ── Selection ────────────────────────────────────────────────────────────

    /// <summary>Selection, shared by pointer and keyboard.</summary>
    public void Select(MergeCandidateViewModel? candidate)
    {
        if (ReferenceEquals(SelectedCandidate, candidate))
        {
            return;
        }

        if (SelectedCandidate is { } previous)
        {
            previous.IsSelected = false;
        }

        SelectedCandidate = candidate;
        if (candidate is not null)
        {
            candidate.IsSelected = true;
        }
    }

    /// <summary>
    /// Keyboard navigation (§8): moves selection by <paramref name="delta"/>
    /// cards. Returns the new index, or -1 when the queue is empty.
    /// </summary>
    public int MoveSelection(int delta)
    {
        if (Candidates.Count == 0)
        {
            return -1;
        }

        var current = IndexOf(SelectedCandidate);
        var next = current < 0
            ? 0
            : Math.Clamp(current + delta, 0, Candidates.Count - 1);
        Select(Candidates[next]);
        return next;
    }

    /// <summary>Sets the display resolution for cover decoding.</summary>
    public void RequestCovers(double displayWidthPixels)
    {
        if (displayWidthPixels <= 0)
        {
            return;
        }

        _coverWidthPixels = displayWidthPixels;
        foreach (var candidate in Candidates)
        {
            candidate.RequestCovers(displayWidthPixels);
        }
    }

    // ── Rebuilding ───────────────────────────────────────────────────────────

    // Removes an answered card and leaves the cursor on the pair that slid into
    // its place, so the queue can be worked straight down without re-aiming.
    private void Remove(MergeCandidateViewModel candidate)
    {
        var index = IndexOf(candidate);

        var remaining = new List<MergeCandidateViewModel>(Candidates.Count);
        foreach (var existing in Candidates)
        {
            if (!ReferenceEquals(existing, candidate))
            {
                remaining.Add(existing);
            }
        }

        Candidates = remaining;
        Select(remaining.Count == 0
            ? null
            : remaining[Math.Clamp(index, 0, remaining.Count - 1)]);
    }

    // Restates every visible card's outcome from the current database. On the
    // load path the caller passes in titles it has already fetched (a plan's
    // two release ids are the candidate row's own two columns). After a merge
    // the caller passes null, and the method fetches from the fresh plans,
    // because a merge can change which release a neighbouring plan names.
    private async Task RefreshPreviewsAsync(
        IReadOnlyList<MergeCandidateViewModel> cards,
        IReadOnlyDictionary<long, string>? titles,
        IReadOnlyDictionary<long, long>? workIds,
        CancellationToken ct)
    {
        if (cards.Count == 0)
        {
            return;
        }

        var planned = new List<(MergeCandidateViewModel Card, MergePlan Plan)>(cards.Count);
        foreach (var card in cards)
        {
            planned.Add((card, await _merges.PreviewAsync(card.Id, ct)));
        }

        if (titles is null || workIds is null)
        {
            // From the fresh plans, never from the ids the cards were built
            // with. A merge can have absorbed one of those releases, and a
            // lookup against a row that no longer exists would put a release
            // number where the survivor's name belongs.
            var releaseIds = new HashSet<long>();
            foreach (var (_, plan) in planned)
            {
                AddIfPresent(releaseIds, plan.LeftReleaseId);
                AddIfPresent(releaseIds, plan.RightReleaseId);
            }

            var described = await DescribeReleasesAsync(releaseIds, ct);
            titles = described.Titles;
            workIds = described.WorkIds;
        }

        // Plans are paired to cards by reference, not by index. An answer on
        // another card can replace Candidates across the awaits above, so
        // index pairing would silently assign one card's outcome to its
        // neighbour, which sits directly over a button that writes to the
        // library. The reference check ensures a stale plan is dropped rather
        // than misattributed.
        var live = new HashSet<MergeCandidateViewModel>(Candidates);
        // Filtering the pending read handles the load path; this handles the
        // answer path, where answering one pair can make a neighbouring pair
        // already-one-game. Together they make acceptance criterion "no BLOCKED
        // card and no already-one-game message can appear for any pair the queue
        // shows" true for a whole session, not just at load. The card is dropped
        // from the screen but its row is NOT answered on the user's behalf; it
        // stays pending for the sweep to withdraw, because a rejection would
        // record a decision the user never made.
        var settled = new List<MergeCandidateViewModel>();
        foreach (var (card, plan) in planned)
        {
            if (!live.Contains(card))
            {
                continue;
            }

            if (plan.Mode == MergeMode.NothingToDo)
            {
                settled.Add(card);
                continue;
            }

            var (surviving, absorbed) = Sides(plan, workIds);
            card.Preview = new MergePreviewViewModel(
                plan,
                TitleOf(titles, surviving),
                absorbed is { } id && titles.TryGetValue(id, out var name) ? name : null,
                absorbed);
        }

        foreach (var card in settled)
        {
            Remove(card);
        }
    }

    // Returns only cards whose plan a merge could have changed. A plan reads
    // two releases and the works they sit on, so a card is reachable when it
    // names one of the merged releases or when one of its releases sat on the
    // absorbed work (which just moved to the surviving one). Every other card
    // reads exactly the rows it read before the merge. Re-planning the whole
    // queue on every answer froze the UI for ~2s at 200 pending pairs because
    // Microsoft.Data.Sqlite completes synchronously and this is the path meant
    // for repeated keypresses.
    private List<MergeCandidateViewModel> AffectedBy(MergeOutcome outcome)
    {
        if (!outcome.Applied)
        {
            return [];
        }

        var merged = new HashSet<long>();
        AddIfPresent(merged, outcome.Plan.LeftReleaseId);
        AddIfPresent(merged, outcome.Plan.RightReleaseId);
        AddIfPresent(merged, outcome.Plan.SurvivingReleaseId);
        AddIfPresent(merged, outcome.Plan.AbsorbedReleaseId);

        var absorbedWork = outcome.Plan.AbsorbedWorkId;

        bool Reached(long releaseId)
            => merged.Contains(releaseId)
                || (absorbedWork is { } work
                    && _workIdOfRelease.TryGetValue(releaseId, out var owner)
                    && owner == work);

        var affected = new List<MergeCandidateViewModel>();
        foreach (var card in Candidates)
        {
            if (Reached(card.Left.ReleaseId) || Reached(card.Right.ReleaseId))
            {
                affected.Add(card);
            }
        }

        // The absorbed work's releases now belong to the surviving work.
        // Rewriting the cache here keeps the next AffectedBy call correct;
        // re-querying would reintroduce the database round-trip this cache
        // exists to avoid.
        if (absorbedWork is { } absorbed && outcome.Plan.SurvivingWorkId is { } survivor)
        {
            foreach (var releaseId in _workIdOfRelease.Keys.ToList())
            {
                if (_workIdOfRelease[releaseId] == absorbed)
                {
                    _workIdOfRelease[releaseId] = survivor;
                }
            }
        }

        return affected;
    }

    // Applied merges and the leftovers are one read: both are facts about what
    // has been written, and both move whenever anything is.
    private async Task RefreshAppliedAsync(CancellationToken ct)
    {
        var outstanding = await _merges.OutstandingAsync(ct);

        var releaseIds = new HashSet<long>();
        foreach (var plan in outstanding)
        {
            AddIfPresent(releaseIds, plan.LeftReleaseId);
            AddIfPresent(releaseIds, plan.RightReleaseId);
        }

        var (titles, _, workIds) = await DescribeReleasesAsync(releaseIds, ct);

        Outstanding = BuildOutstanding(outstanding, titles, workIds);
        History = BuildHistory(await _merges.HistoryAsync(ct));
    }

    private static void AddIfPresent(HashSet<long> ids, long? id)
    {
        if (id is { } value)
        {
            ids.Add(value);
        }
    }

    private static IReadOnlyList<MergeApplyViewModel> BuildOutstanding(
        IReadOnlyList<MergePlan> plans,
        IReadOnlyDictionary<long, string> titles,
        IReadOnlyDictionary<long, long> workIds)
    {
        var rows = new List<MergeApplyViewModel>(plans.Count);
        foreach (var plan in plans)
        {
            var (surviving, absorbed) = Sides(plan, workIds);
            rows.Add(new MergeApplyViewModel(
                plan,
                TitleOf(titles, surviving),
                absorbed is { } id && titles.TryGetValue(id, out var name) ? name : null,
                absorbed));
        }

        return rows;
    }

    // A work-only merge records no surviving release, so the surviving side is
    // found by asking which of the two releases already sits on the surviving
    // work.
    private static (long? Surviving, long? Absorbed) Sides(
        MergePlan plan, IReadOnlyDictionary<long, long> workIds)
    {
        if (plan.SurvivingReleaseId is { } surviving && plan.AbsorbedReleaseId is { } absorbed)
        {
            return (surviving, absorbed);
        }

        if (plan.SurvivingWorkId is { } survivingWorkId)
        {
            if (plan.LeftReleaseId is { } left
                && workIds.TryGetValue(left, out var leftWork)
                && leftWork == survivingWorkId)
            {
                return (left, plan.RightReleaseId);
            }

            if (plan.RightReleaseId is { } right
                && workIds.TryGetValue(right, out var rightWork)
                && rightWork == survivingWorkId)
            {
                return (right, plan.LeftReleaseId);
            }
        }

        return (plan.LeftReleaseId, plan.RightReleaseId);
    }

    private static string TitleOf(IReadOnlyDictionary<long, string> titles, long? releaseId)
        => releaseId is { } id && titles.TryGetValue(id, out var name)
            ? name
            : MergeApplyViewModel.ReleaseLabel(releaseId);

    // A blocked row names the merge blocking it, so the descriptions are
    // collected first and handed to the rows that need them.
    private static IReadOnlyList<MergeHistoryRowViewModel> BuildHistory(
        IReadOnlyList<MergeUndoPlan> plans)
    {
        var labels = new Dictionary<long, string>(plans.Count);
        foreach (var plan in plans)
        {
            labels[plan.ApplicationId] = new MergeHistoryRowViewModel(plan).BlockingLabel;
        }

        var rows = new List<MergeHistoryRowViewModel>(plans.Count);
        foreach (var plan in plans)
        {
            rows.Add(new MergeHistoryRowViewModel(
                plan,
                plan.BlockingApplicationId is { } blocking
                    && labels.TryGetValue(blocking, out var label)
                        ? label
                        : null));
        }

        return rows;
    }

    private static string ModePhrase(MergeMode mode) => mode switch
    {
        MergeMode.ReleaseCollapse => MergeCopy.ModeReleaseCollapse,
        MergeMode.WorkOnly => MergeCopy.ModeWorkOnly,
        _ => string.Empty,
    };

    private async Task<(Dictionary<long, string> Titles,
                        Dictionary<long, CoverKey> CoverKeys,
                        Dictionary<long, long> WorkIds)>
        DescribeReleasesAsync(IEnumerable<long> releaseIds, CancellationToken ct)
    {
        var titles = new Dictionary<long, string>();
        var coverKeys = new Dictionary<long, CoverKey>();
        var workIds = new Dictionary<long, long>();

        foreach (var releaseId in releaseIds)
        {
            var release = await _releases.GetAsync(releaseId, ct);
            if (release is null)
            {
                continue;
            }

            workIds[releaseId] = release.WorkId;
            _workIdOfRelease[releaseId] = release.WorkId;

            // Prefer work name (human title) over release name (edition).
            var work = await _works.GetAsync(release.WorkId, ct);
            titles[releaseId] = work?.Name ?? release.Name;

            var externalIds = await _releases.GetExternalIdsAsync(releaseId, ct);
            var steam = externalIds.FirstOrDefault(x => x.Provider == ExternalIdProviders.Steam);
            if (steam is not null)
            {
                coverKeys[releaseId] = CoverKey.Steam(steam.ProviderId);
            }
            else if (IgdbImageUrl.ImageId(work?.CoverUrl) is { Length: > 0 } imageId)
            {
                // IGDB fallback for the side without a Steam appid (common in cross-store pairs).
                coverKeys[releaseId] = CoverKey.Igdb(imageId);
            }
        }

        return (titles, coverKeys, workIds);
    }

    private int IndexOf(MergeCandidateViewModel? candidate)
    {
        if (candidate is null)
        {
            return -1;
        }

        for (var i = 0; i < Candidates.Count; i++)
        {
            if (ReferenceEquals(Candidates[i], candidate))
            {
                return i;
            }
        }

        return -1;
    }
}
