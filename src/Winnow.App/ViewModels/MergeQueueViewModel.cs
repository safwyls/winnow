using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Merging;
using Winnow.Core.Repositories;
using Winnow.Covers;
using Winnow.Covers.Igdb;
using Winnow.Resolve;
using Winnow.Resolve.Matching;

namespace Winnow.App.ViewModels;

/// <summary>
/// The Same Game screen. One pane with a 48px header and a two-segment
/// control, REVIEW / HISTORY, in the same grammar the settings surface uses
/// for PLATFORMS / APPEARANCE.
///
/// <para>REVIEW's unit is a GROUP, never a pair. Pending proposals are resolved
/// through the live link map, proposals whose two sides are already one game are
/// dropped, and the connected components of what is left become one card each.
/// Answering a member therefore cannot leave a sibling card stale: they were
/// never separate cards.</para>
///
/// <para>Answering writes a LINK, not a merge. Nothing is deleted, so the answer
/// is retractable from HISTORY and the same group can be linked, retracted and
/// linked again any number of times. The destructive executor still backs the
/// two merge sections of HISTORY, which exist for installs that answered under
/// the previous flow; the review path never reaches it.</para>
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
    private readonly IIdentityLinkRepository _links;
    private readonly ICoverCache? _covers;
    private readonly IResolveStateRepository? _resolveState;

    /// <summary>Display resolution the view last asked for; 0 until it attaches.</summary>
    private double _coverWidthPixels;

    private bool _loaded;

    // Both engines are required, not optional. A type registered in the
    // container and resolved nowhere is indistinguishable from one that works;
    // omitting either must break the container at startup rather than render a
    // screen whose answers quietly write nothing.
    public MergeQueueViewModel(
        IMergeCandidateRepository candidates,
        IReleaseRepository releases,
        IWorkRepository works,
        MergeExecutor merges,
        IIdentityLinkRepository links,
        ICoverCache? covers = null,
        IResolveStateRepository? resolveState = null)
    {
        _candidates = candidates;
        _releases = releases;
        _works = works;
        _merges = merges;
        _links = links;
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

    // Recomputes on the way in. The link log moves whenever a group is answered
    // or retracted, and merge reversibility depends on every merge applied after
    // a given one, so a verdict computed at the last load is a claim about a
    // database that may since have moved.
    [RelayCommand]
    private async Task ShowHistoryAsync(CancellationToken ct)
    {
        IsHistoryVisible = true;
        await RefreshAppliedAsync(ct);
    }

    // ── The review queue ─────────────────────────────────────────────────────

    /// <summary>The pending groups, sorted strongest first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(PendingCount), nameof(PendingCountText), nameof(HasPending),
        nameof(ShowEmpty), nameof(RowOpacity), nameof(ShowOutstandingNotice))]
    public partial IReadOnlyList<MergeGroupViewModel> Groups { get; set; } = [];

    /// <summary>The group the user is currently looking at, or null when the queue is empty.</summary>
    [ObservableProperty]
    public partial MergeGroupViewModel? SelectedGroup { get; set; }

    /// <summary>Number of groups still waiting for an answer.</summary>
    public int PendingCount => Groups.Count;

    /// <summary>Plex Mono, tabular, grouped — every number in the app (§3).</summary>
    public string PendingCountText => PendingCount.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>Uppercase label beside the count.</summary>
    public string PendingCountLabel => MergeCopy.PendingCountLabel;

    /// <summary>True when there are pending groups to review.</summary>
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
        ? MergeCopy.EmptySwept
        : MergeCopy.EmptyNotSwept;

    /// <summary>Standing explanation under the screen title.</summary>
    public string IntroMessage => MergeCopy.QueueIntro;

    // ── The pairs answered under the previous two-step flow ──────────────────

    /// <summary>
    /// Pairs confirmed under the old two-step flow that were never applied.
    /// Nothing this build does adds to this list, because the review path no
    /// longer writes <c>confirmed</c> and no longer merges. It exists so an
    /// install predating the change has somewhere to finish, and it is absent
    /// altogether once drained.
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

    // ── History: link acts ───────────────────────────────────────────────────

    /// <summary>
    /// Every link act, newest first, each with its retraction. Rebuilt from the
    /// table on every arrival, because the table is the history and a row's
    /// standing changes whenever anything is linked or retracted.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLinkHistory), nameof(ShowLinkHistoryEmpty))]
    public partial IReadOnlyList<MergeLinkHistoryRowViewModel> LinkHistory { get; set; } = [];

    /// <summary>True when at least one group has been linked.</summary>
    public bool HasLinkHistory => LinkHistory.Count > 0;

    /// <summary>True once the screen has loaded and nothing has been linked.</summary>
    public bool ShowLinkHistoryEmpty => _loaded && LinkHistory.Count == 0;

    // ── History: applied merges ──────────────────────────────────────────────

    /// <summary>
    /// Every applied merge, newest first, each with its undo verdict. A separate
    /// list from <see cref="LinkHistory"/> because a retraction and a
    /// fifteen-table reversal are different facts, and one interleaved list
    /// would need a sentence per row saying which kind it was. This list is
    /// finite and shrinking: nothing this build does adds to it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHistory))]
    public partial IReadOnlyList<MergeHistoryRowViewModel> History { get; set; } = [];

    /// <summary>True when at least one merge has been applied.</summary>
    public bool HasHistory => History.Count > 0;

    // ── What the last act actually did ───────────────────────────────────────

    /// <summary>
    /// What the last answer, retraction or undo actually did. Written from what
    /// the engine returned, never from what was asked for.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(HasReport), nameof(ReportUndoAutomationName), nameof(ReportRetractAutomationName))]
    public partial string? ReportMessage { get; set; }

    /// <summary>True when there is an outcome to display.</summary>
    public bool HasReport => !string.IsNullOrEmpty(ReportMessage);

    /// <summary>
    /// The link act the report is about. Set from the act id the repository
    /// returned, so the control is offered only for a write that happened.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetractReport))]
    public partial long? ReportRetractActId { get; set; }

    /// <summary>True when the report's link can be retracted from where it stands.</summary>
    public bool CanRetractReport => ReportRetractActId is not null;

    /// <summary>
    /// The merge the report is about, when it can still be reversed. Reachable
    /// only from the two merge sections of HISTORY; the review path never
    /// applies a merge.
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

    /// <summary>Label on the segment showing what has been answered.</summary>
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

    /// <summary>Section heading for the list of link acts.</summary>
    public string LinkHistoryHeading => MergeCopy.LinkHistoryHeading;

    /// <summary>Introduction under that heading.</summary>
    public string LinkHistoryIntro => MergeCopy.LinkHistoryIntro;

    /// <summary>Empty state for the link list.</summary>
    public string LinkHistoryEmptyMessage => MergeCopy.LinkHistoryEmpty;

    /// <summary>Section heading for the history of applied merges.</summary>
    public string HistoryHeading => MergeCopy.HistoryHeading;

    /// <summary>Introduction under the history heading.</summary>
    public string HistoryIntro => MergeCopy.HistoryIntro;

    /// <summary>Label on the control that retracts the link the report describes.</summary>
    public string ReportRetractButtonText => MergeCopy.RetractButton;

    /// <summary>Tooltip on that control.</summary>
    public string ReportRetractTooltipText => MergeCopy.RetractTooltip;

    /// <summary>Label on the undo control beside a merge outcome report.</summary>
    public string ReportUndoButtonText => MergeCopy.ReportUndoButton;

    /// <summary>Tooltip on that control.</summary>
    public string ReportUndoTooltipText => MergeCopy.ReportUndoTooltip;

    /// <summary>
    /// Automation name for the retraction beside a report. Without the verb a
    /// screen reader announces a button indistinguishable from a statement,
    /// matching how history rows build their automation names (§8).
    /// </summary>
    public string ReportRetractAutomationName =>
        string.Create(CultureInfo.CurrentCulture, $"{MergeCopy.RetractButton}. {ReportMessage}");

    /// <summary>Automation name for the merge undo beside a report.</summary>
    public string ReportUndoAutomationName =>
        string.Create(CultureInfo.CurrentCulture, $"{MergeCopy.ReportUndoButton}. {ReportMessage}");

    /// <summary>Tooltip on "Different games".</summary>
    public string DifferentGamesTooltip => MergeCopy.DifferentGamesTooltip;

    // ── Loading ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct)
    {
        // Must be read before the queue so the empty state knows if the matcher has run.
        HasCompletedSweep = _resolveState is not null
            && await _resolveState.GetLastSoftMatchSweepAsync(ct) is not null;

        var pending = await _candidates.GetPendingAsync(ct);
        var resolution = await _links.GetResolutionAsync(ct);

        var releaseIds = new HashSet<long>();
        foreach (var candidate in pending)
        {
            releaseIds.Add(candidate.LeftReleaseId);
            releaseIds.Add(candidate.RightReleaseId);
        }

        var outstanding = await _merges.OutstandingAsync(ct);
        foreach (var plan in outstanding)
        {
            AddIfPresent(releaseIds, plan.LeftReleaseId);
            AddIfPresent(releaseIds, plan.RightReleaseId);
        }

        var library = await DescribeAsync(releaseIds, ct);

        _loaded = true;
        Groups = await BuildGroupsAsync(pending, library, resolution, ct);
        Outstanding = BuildOutstanding(outstanding, library.Titles, library.WorkOfRelease);
        History = BuildHistory(await _merges.HistoryAsync(ct));
        LinkHistory = await BuildLinkHistoryAsync(ct);

        Select(Groups.Count > 0 ? Groups[0] : null);
        RequestCovers(_coverWidthPixels);
    }

    // ── Answering ────────────────────────────────────────────────────────────

    /// <summary>
    /// Links every checked member under the chosen primary, in one act and one
    /// transaction, and records a rejection for every proposal the answer leaves
    /// outside the link.
    ///
    /// <para>Nothing else on screen is re-read. Components are disjoint over
    /// resolved works, so a link inside one group cannot change another; the
    /// answered card is removed and no neighbour is replanned. The previous
    /// screen replanned the whole queue on every answer and froze for about two
    /// seconds at 200 pending pairs.</para>
    /// </summary>
    [RelayCommand]
    private async Task SameGameAsync(MergeGroupViewModel? group, CancellationToken ct)
    {
        if (group is null || group.IsDecided)
        {
            return;
        }

        // Latch before await to prevent double-writes from rapid clicks.
        group.IsDecided = true;

        var children = group.IncludedChildWorkIds;
        var rejected = group.RejectedCandidateIds;
        var primaryTitle = group.PrimaryTitle;

        ReportRetractActId = null;
        ReportUndoApplicationId = null;
        ReportUndoTitle = null;

        if (children.Count == 0)
        {
            // Every member unchecked is the same answer as "different games",
            // and it is recorded the same way: no link, and every proposal the
            // answer touched written down.
            await RejectAsync(group.AllCandidateIds, ct);
            ReportMessage = MergeCopy.NothingLinked;
            Remove(group);
            return;
        }

        var actId = await _links.LinkAsync(
            new IdentityLinkRequest
            {
                ParentWorkId = group.Primary.WorkId,
                ChildWorkIds = children,
                Kind = IdentityLinkKinds.SameGame,
                Source = IdentityLinkSources.User,
            },
            ct);

        await RejectAsync(rejected, ct);

        ReportRetractActId = actId;
        ReportMessage = string.Format(
            CultureInfo.CurrentCulture,
            MergeCopy.LinkedReportFormat,
            primaryTitle,
            children.Count.ToString("N0", CultureInfo.CurrentCulture));

        Remove(group);
    }

    /// <summary>Rejects every proposal in the group, links nothing, and removes the card.</summary>
    [RelayCommand]
    private async Task DifferentGamesAsync(MergeGroupViewModel? group, CancellationToken ct)
    {
        if (group is null || group.IsDecided)
        {
            return;
        }

        group.IsDecided = true;
        await RejectAsync(group.AllCandidateIds, ct);
        Remove(group);
    }

    private async Task RejectAsync(IReadOnlyList<long> candidateIds, CancellationToken ct)
    {
        foreach (var candidateId in candidateIds)
        {
            await _candidates.SetStatusAsync(candidateId, MergeCandidateStatuses.Rejected, ct);
        }
    }

    // ── Retracting ───────────────────────────────────────────────────────────

    /// <summary>
    /// Retracts the link the report describes. The proposals were never marked
    /// answered, so they return to the queue as ordinary pending rows on the
    /// next load, and the same group can be linked again immediately.
    /// </summary>
    [RelayCommand]
    private async Task RetractReportAsync(CancellationToken ct)
    {
        if (ReportRetractActId is not { } actId)
        {
            return;
        }

        await RetractActAsync(actId, ct);
    }

    /// <summary>Retracts one act from the link history list.</summary>
    [RelayCommand]
    private async Task RetractAsync(MergeLinkHistoryRowViewModel? row, CancellationToken ct)
    {
        if (row is null || !row.CanRetract)
        {
            return;
        }

        row.IsRetracting = true;
        await RetractActAsync(row.ActId, ct);
    }

    private async Task RetractActAsync(long actId, CancellationToken ct)
    {
        ReportRetractActId = null;
        ReportUndoApplicationId = null;
        ReportUndoTitle = null;

        var retracted = await _links.RetractActAsync(actId, null, ct);
        ReportMessage = retracted ? MergeCopy.Retracted : MergeCopy.RetractedAlready;

        await LoadAsync(ct);
    }

    // ── Applying the leftovers ───────────────────────────────────────────────

    // Applies one leftover pair and reports the outcome the engine returned. A
    // refused plan writes nothing and says so; it is never silently dropped.
    // Reachable only from HISTORY: nothing the review queue does adds to this
    // list any more.
    [RelayCommand]
    private async Task ApplyAsync(MergeApplyViewModel? row, CancellationToken ct)
    {
        if (row is null || !row.CanApply)
        {
            return;
        }

        row.IsApplying = true;

        var outcome = await _merges.ApplyAsync(row.Id, ct);
        ReportRetractActId = null;
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

        if (outcome.Applied
            && outcome.ApplicationId is { } applicationId
            && (await _merges.PreviewUndoAsync(applicationId, ct)).Reversible)
        {
            ReportUndoApplicationId = applicationId;
            ReportUndoTitle = row.AbsorbedTitle ?? row.SurvivingTitle;
        }

        await LoadAsync(ct);
    }

    // The batch path. Each pair is its own transaction, so one pair that cannot
    // merge safely is skipped rather than holding the rest back.
    [RelayCommand]
    private async Task ApplyAllAsync(CancellationToken ct)
    {
        var summary = await _merges.ApplyAllConfirmedAsync(ct);

        ReportRetractActId = null;
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

    // ── Undoing a merge ──────────────────────────────────────────────────────

    // The undo beside a merge report, which is what made applying a leftover
    // safe. The review path no longer merges, so this is reachable only from
    // HISTORY.
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
        ReportRetractActId = null;
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
    public void Select(MergeGroupViewModel? group)
    {
        if (ReferenceEquals(SelectedGroup, group))
        {
            return;
        }

        if (SelectedGroup is { } previous)
        {
            previous.IsSelected = false;
        }

        SelectedGroup = group;
        if (group is not null)
        {
            group.IsSelected = true;
        }
    }

    /// <summary>
    /// Keyboard navigation (§8): moves selection by <paramref name="delta"/>
    /// cards. Returns the new index, or -1 when the queue is empty.
    /// </summary>
    public int MoveSelection(int delta)
    {
        if (Groups.Count == 0)
        {
            return -1;
        }

        var current = IndexOf(SelectedGroup);
        var next = current < 0
            ? 0
            : Math.Clamp(current + delta, 0, Groups.Count - 1);
        Select(Groups[next]);
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
        foreach (var group in Groups)
        {
            group.RequestCovers(displayWidthPixels);
        }
    }

    // ── Rebuilding ───────────────────────────────────────────────────────────

    // Removes an answered card and leaves the cursor on the group that slid into
    // its place, so the queue can be worked straight down without re-aiming.
    private void Remove(MergeGroupViewModel group)
    {
        var index = IndexOf(group);

        var remaining = new List<MergeGroupViewModel>(Groups.Count);
        foreach (var existing in Groups)
        {
            if (!ReferenceEquals(existing, group))
            {
                remaining.Add(existing);
            }
        }

        Groups = remaining;
        Select(remaining.Count == 0
            ? null
            : remaining[Math.Clamp(index, 0, remaining.Count - 1)]);
    }

    private async Task<IReadOnlyList<MergeGroupViewModel>> BuildGroupsAsync(
        IReadOnlyList<MergeCandidate> pending,
        LibrarySnapshot library,
        IdentityResolution resolution,
        CancellationToken ct)
    {
        var payloads = new Dictionary<long, SoftMatchSignalsPayload?>(pending.Count);
        var rows = new Dictionary<long, MergeCandidate>(pending.Count);
        var proposals = new List<MergeGroupProposal>(pending.Count);

        foreach (var candidate in pending)
        {
            var payload = MergeEdgeViewModel.Parse(candidate);
            payloads[candidate.Id] = payload;
            rows[candidate.Id] = candidate;

            proposals.Add(new MergeGroupProposal
            {
                CandidateId = candidate.Id,
                LeftReleaseId = candidate.LeftReleaseId,
                RightReleaseId = candidate.RightReleaseId,
                Score = candidate.Score,
                IsPriority = MergeEdgeViewModel.IsPriorityBand(payload),
            });
        }

        var groups = MergeGrouping.Build(
            proposals, library.WorkOfRelease, library.Works, resolution.SameGame);

        var cards = new List<MergeGroupViewModel>(groups.Count);
        foreach (var group in groups)
        {
            var members = new List<MergeGroupMemberViewModel>(group.Members.Count);
            foreach (var member in group.Members)
            {
                members.Add(new MergeGroupMemberViewModel(
                    member.WorkId,
                    await DescribeWorkAsync(member, library, ct),
                    member.ReleaseIds,
                    member.BestScore,
                    member.IsDefaultIncluded));
            }

            var edges = new List<MergeEdgeViewModel>(group.Edges.Count);
            foreach (var edge in group.Edges)
            {
                edges.Add(MergeEdgeViewModel.Create(
                    edge, payloads.GetValueOrDefault(edge.CandidateId)));
            }

            cards.Add(new MergeGroupViewModel(group, members, edges));
        }

        return cards;
    }

    // The member's face comes from the WORK, because a member is a work and its
    // title is the one the library would keep. The cover key comes from the
    // store entry, because that is where a Steam appid lives.
    private async Task<MergeSideViewModel> DescribeWorkAsync(
        MergeGroupMember member, LibrarySnapshot library, CancellationToken ct)
    {
        var work = await _works.GetAsync(member.WorkId, ct);
        var releaseId = member.ReleaseIds.Count > 0 ? member.ReleaseIds[0] : 0;

        CoverKey? coverKey = null;
        foreach (var candidate in member.ReleaseIds)
        {
            if (library.CoverKeys.TryGetValue(candidate, out var key))
            {
                coverKey = key;
                break;
            }
        }

        if (coverKey is null && IgdbImageUrl.ImageId(work?.CoverUrl) is { Length: > 0 } imageId)
        {
            coverKey = CoverKey.Igdb(imageId);
        }

        return new MergeSideViewModel(
            releaseId,
            work?.Name ?? library.Titles.GetValueOrDefault(releaseId, string.Empty),
            null,
            work?.FirstReleaseYear,
            work?.Publisher,
            coverKey,
            _covers,
            member.ReleaseIds);
    }

    private async Task<IReadOnlyList<MergeLinkHistoryRowViewModel>> BuildLinkHistoryAsync(
        CancellationToken ct)
    {
        var links = await _links.GetHistoryAsync(null, ct);
        if (links.Count == 0)
        {
            return [];
        }

        var acts = await _links.GetActsAsync(ct);
        var actById = new Dictionary<long, IdentityAct>(acts.Count);
        foreach (var act in acts)
        {
            actById[act.Id] = act;
        }

        var names = new Dictionary<long, string>();
        async Task<string> NameOfAsync(long workId)
        {
            if (names.TryGetValue(workId, out var known))
            {
                return known;
            }

            var work = await _works.GetAsync(workId, ct);
            var name = work?.Name ?? string.Create(CultureInfo.InvariantCulture, $"#{workId}");
            names[workId] = name;
            return name;
        }

        // Grouped by act, because an act is the unit of undo: one retraction
        // reverses every link it created, however many that was.
        var byAct = new Dictionary<long, List<IdentityLink>>();
        var order = new List<long>();
        foreach (var link in links)
        {
            if (!byAct.TryGetValue(link.ActId, out var list))
            {
                byAct[link.ActId] = list = [];
                order.Add(link.ActId);
            }

            list.Add(link);
        }

        var rows = new List<MergeLinkHistoryRowViewModel>(order.Count);
        foreach (var actId in order)
        {
            if (!actById.TryGetValue(actId, out var act))
            {
                continue;
            }

            var members = byAct[actId];
            var parentWorkId = members[0].ParentWorkId;
            var childTitles = new List<string>(members.Count);
            var live = false;
            DateTime? retractedAt = null;

            foreach (var link in members)
            {
                childTitles.Add(await NameOfAsync(link.ChildWorkId));
                if (link.IsLive)
                {
                    live = true;
                }
                else if (retractedAt is null || link.RetractedAt > retractedAt)
                {
                    retractedAt = link.RetractedAt;
                }
            }

            rows.Add(new MergeLinkHistoryRowViewModel(
                act, await NameOfAsync(parentWorkId), childTitles, live, retractedAt));
        }

        rows.Reverse();
        return rows;
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

        var library = await DescribeAsync(releaseIds, ct);

        Outstanding = BuildOutstanding(outstanding, library.Titles, library.WorkOfRelease);
        History = BuildHistory(await _merges.HistoryAsync(ct));
        LinkHistory = await BuildLinkHistoryAsync(ct);
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

    /// <summary>What one load read about the releases the queue names.</summary>
    private sealed record LibrarySnapshot(
        Dictionary<long, string> Titles,
        Dictionary<long, CoverKey> CoverKeys,
        Dictionary<long, long> WorkOfRelease,
        Dictionary<long, SurvivorCandidate> Works);

    private async Task<LibrarySnapshot> DescribeAsync(
        IEnumerable<long> releaseIds, CancellationToken ct)
    {
        var titles = new Dictionary<long, string>();
        var coverKeys = new Dictionary<long, CoverKey>();
        var workOfRelease = new Dictionary<long, long>();
        var works = new Dictionary<long, SurvivorCandidate>();

        foreach (var releaseId in releaseIds)
        {
            var release = await _releases.GetAsync(releaseId, ct);
            if (release is null)
            {
                continue;
            }

            workOfRelease[releaseId] = release.WorkId;

            var work = await _works.GetAsync(release.WorkId, ct);
            titles[releaseId] = work?.Name ?? release.Name;

            if (work is not null && !works.ContainsKey(work.Id))
            {
                // The three facts the ladder tests. Release count is the work's
                // real count, not the count of entries this queue happens to
                // name, because "most store entries" is a claim about the game.
                works[work.Id] = new SurvivorCandidate
                {
                    WorkId = work.Id,
                    HasIgdbId = work.IgdbId is not null,
                    NameIsProvisional = work.NameIsProvisional,
                    ReleaseCount = (await _releases.GetByWorkAsync(work.Id, ct)).Count,
                };
            }

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

        return new LibrarySnapshot(titles, coverKeys, workOfRelease, works);
    }

    private int IndexOf(MergeGroupViewModel? group)
    {
        if (group is null)
        {
            return -1;
        }

        for (var i = 0; i < Groups.Count; i++)
        {
            if (ReferenceEquals(Groups[i], group))
            {
                return i;
            }
        }

        return -1;
    }
}
