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
/// The Same Game screen. One pane with a 48px header and a three-segment
/// control, REVIEW / EXPANSIONS / HISTORY, in the same grammar the settings
/// surface uses for PLATFORMS / APPEARANCE.
///
/// <para>REVIEW's unit is a GROUP, never a pair. Pending proposals are resolved
/// through the live link map, proposals whose two sides are already one game are
/// dropped, and the connected components of what is left become one card each.
/// Answering a member therefore cannot leave a sibling card stale: they were
/// never separate cards.</para>
///
/// <para>Answering writes a LINK, not a merge. Nothing is deleted, so the
/// answer is retractable from HISTORY and the same group can be linked,
/// retracted and linked again any number of times.</para>
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
    private readonly IIdentityLinkRepository _links;
    private readonly IOwnershipRepository _ownership;
    private readonly LibraryExpansionScan _expansions;
    private readonly IExpansionRefusalRepository _expansionRefusals;
    private readonly ICoverCache? _covers;
    private readonly IResolveStateRepository? _resolveState;

    /// <summary>Display resolution the view last asked for; 0 until it attaches.</summary>
    private double _coverWidthPixels;

    private bool _loaded;

    /// <summary>True once one expansion scan has completed in this session.</summary>
    private bool _scannedExpansions;

    /// <summary>
    /// The link and ownership repositories are required, not optional. A type
    /// registered in the container and resolved nowhere is indistinguishable
    /// from one that works; omitting either must break the container at startup
    /// rather than render a screen whose answers quietly write nothing.
    /// </summary>
    public MergeQueueViewModel(
        IMergeCandidateRepository candidates,
        IReleaseRepository releases,
        IWorkRepository works,
        IIdentityLinkRepository links,
        IOwnershipRepository ownership,
        LibraryExpansionScan expansions,
        IExpansionRefusalRepository expansionRefusals,
        ICoverCache? covers = null,
        IResolveStateRepository? resolveState = null)
    {
        _candidates = candidates;
        _releases = releases;
        _works = works;
        _links = links;
        _ownership = ownership;
        _expansions = expansions;
        _expansionRefusals = expansionRefusals;
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

    /// <summary>
    /// True when the EXPANSIONS surface is up.
    ///
    /// <para>A third segment rather than a third kind of card in one scroll,
    /// and the reason is that the two surfaces ask different questions with
    /// different answers. REVIEW asks "same game?" and answers Same game /
    /// Different games; this asks "expansion?" and answers Group / Not
    /// expansions. Interleaving them would put two answer vocabularies in one
    /// column and give S and D two meanings.</para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReviewVisible))]
    public partial bool IsExpansionsVisible { get; set; }

    /// <summary>True when the review queue is the visible surface.</summary>
    public bool IsReviewVisible => !IsHistoryVisible && !IsExpansionsVisible;

    /// <summary>Switches the segmented control to REVIEW.</summary>
    [RelayCommand]
    private void ShowReview()
    {
        ClearReport();
        IsHistoryVisible = false;
        IsExpansionsVisible = false;
    }

    /// <summary>Switches to HISTORY, rebuilding the list from the table.
    /// Recomputed on every arrival because the link log moves whenever a
    /// group is answered or retracted.</summary>
    [RelayCommand]
    private async Task ShowHistoryAsync(CancellationToken ct)
    {
        ClearReport();
        IsExpansionsVisible = false;
        IsHistoryVisible = true;
        LinkHistory = await BuildLinkHistoryAsync(ct);
    }

    /// <summary>Switches the segmented control to EXPANSIONS. The cards were
    /// built by the last load, so arriving here costs no scan.</summary>
    [RelayCommand]
    private void ShowExpansions()
    {
        ClearReport();
        IsHistoryVisible = false;
        IsExpansionsVisible = true;
    }

    // ── The review queue ─────────────────────────────────────────────────────

    /// <summary>The pending groups, sorted strongest first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(PendingCount), nameof(PendingCountText), nameof(HasPending),
        nameof(ShowEmpty), nameof(OutstandingCount), nameof(OutstandingCountText),
        nameof(HasOutstanding), nameof(RowOpacity))]
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

    /// <summary>Review groups plus expansion groups. The rail row counts what is
    /// waiting on the screen, and the screen holds two questions; counting review
    /// alone showed a dimmed <c>SAME GAME? 0</c> over a dozen expansion cards.</summary>
    public int OutstandingCount => PendingCount + ExpansionCount;

    /// <summary>Plex Mono, tabular, grouped, every number in the app (§3).</summary>
    public string OutstandingCountText =>
        OutstandingCount.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>True when either surface has a card waiting.</summary>
    public bool HasOutstanding => OutstandingCount > 0;

    /// <summary>Dims the rail row to 40% when both surfaces are empty, so the row
    /// stays visible but recedes.</summary>
    public double RowOpacity => HasOutstanding ? 1.0 : 0.4;

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

    // ── The expansion queue ──────────────────────────────────────────────────

    /// <summary>
    /// Base games with expansions proposed under them, one
    /// card each, base work id ascending. DERIVED on every load rather than
    /// stored, for the reason §6.1 gives about buckets: the detector's guards
    /// will be tuned, and a stored proposal computed under an older rule rots.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(ExpansionCount), nameof(ExpansionCountText), nameof(HasExpansions),
        nameof(ShowExpansionsEmpty), nameof(OutstandingCount),
        nameof(OutstandingCountText), nameof(HasOutstanding), nameof(RowOpacity))]
    public partial IReadOnlyList<ExpansionGroupViewModel> ExpansionGroups { get; set; } = [];

    /// <summary>The card the keyboard acts on, or null when the surface is empty.</summary>
    [ObservableProperty]
    public partial ExpansionGroupViewModel? SelectedExpansionGroup { get; set; }

    /// <summary>Number of base games still waiting for an answer.</summary>
    public int ExpansionCount => ExpansionGroups.Count;

    /// <summary>Plex Mono, tabular, grouped — every number in the app (§3).</summary>
    public string ExpansionCountText =>
        ExpansionCount.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>Uppercase label beside the count. The unit is a base game, not a pack.</summary>
    public string ExpansionCountLabel => ExpansionCopy.PendingCountLabel;

    /// <summary>True when there are cards to answer.</summary>
    public bool HasExpansions => ExpansionCount > 0;

    /// <summary>True once the screen has loaded and the expansion surface is empty.</summary>
    public bool ShowExpansionsEmpty => _loaded && ExpansionCount == 0;

    /// <summary>
    /// Empty-state message: distinguishes "the scan found nothing" from "the
    /// scan has not finished yet".
    /// </summary>
    public string ExpansionsEmptyMessage => _scannedExpansions
        ? ExpansionCopy.EmptyScanned
        : ExpansionCopy.EmptyNotScanned;

    /// <summary>The question this surface asks, display L.</summary>
    public string ExpansionsQuestion => ExpansionCopy.ScreenQuestion;

    /// <summary>Standing explanation under that question: display only, retractable.</summary>
    public string ExpansionsIntro => ExpansionCopy.Intro;

    /// <summary>Label on the segment showing this surface.</summary>
    public string ExpansionsSegmentLabel => ExpansionCopy.SegmentExpansions;

    /// <summary>Tooltip on that segment.</summary>
    public string ExpansionsSegmentTooltip => ExpansionCopy.SegmentExpansionsTooltip;

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

    // ── What the last act actually did ───────────────────────────────────────

    /// <summary>
    /// What the last answer, retraction or undo actually did. Written from what
    /// the engine returned, never from what was asked for.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(HasReport), nameof(HasReviewReport), nameof(HasExpansionsReport),
        nameof(HasHistoryReport), nameof(ReportRetractAutomationName))]
    public partial string? ReportMessage { get; set; }

    /// <summary>Which surface the standing report belongs to.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(HasReviewReport), nameof(HasExpansionsReport), nameof(HasHistoryReport))]
    public partial MergeReportSurface ReportSurface { get; set; }

    /// <summary>True when there is an outcome to display.</summary>
    public bool HasReport => !string.IsNullOrEmpty(ReportMessage);

    /// <summary>True when the standing report belongs to the review surface.</summary>
    public bool HasReviewReport => HasReport && ReportSurface == MergeReportSurface.Review;

    /// <summary>True when the standing report belongs to the expansions surface.</summary>
    public bool HasExpansionsReport =>
        HasReport && ReportSurface == MergeReportSurface.Expansions;

    /// <summary>True when the standing report belongs to the history surface.</summary>
    public bool HasHistoryReport => HasReport && ReportSurface == MergeReportSurface.History;

    /// <summary>
    /// The link act the report is about. Set from the act id the repository
    /// returned, so the control is offered only for a write that happened.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetractReport))]
    public partial long? ReportRetractActId { get; set; }

    /// <summary>True when the report's link can be retracted from where it stands.</summary>
    public bool CanRetractReport => ReportRetractActId is not null;

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

    /// <summary>Section heading for the list of link acts.</summary>
    public string LinkHistoryHeading => MergeCopy.LinkHistoryHeading;

    /// <summary>Introduction under that heading.</summary>
    public string LinkHistoryIntro => MergeCopy.LinkHistoryIntro;

    /// <summary>Empty state for the link list.</summary>
    public string LinkHistoryEmptyMessage => MergeCopy.LinkHistoryEmpty;

    /// <summary>Label on the control that retracts the link the report describes.</summary>
    public string ReportRetractButtonText => MergeCopy.RetractButton;

    /// <summary>Tooltip on that control.</summary>
    public string ReportRetractTooltipText => MergeCopy.RetractTooltip;

    /// <summary>
    /// Automation name for the retraction beside a report. Without the verb a
    /// screen reader announces a button indistinguishable from a statement,
    /// matching how history rows build their automation names (§8).
    /// </summary>
    public string ReportRetractAutomationName =>
        string.Create(CultureInfo.CurrentCulture, $"{MergeCopy.RetractButton}. {ReportMessage}");

    /// <summary>Tooltip on "Different games".</summary>
    public string DifferentGamesTooltip => MergeCopy.DifferentGamesTooltip;

    // ── Loading ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct)
    {
        ClearReport();

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

        var library = await DescribeAsync(releaseIds, ct);

        _loaded = true;
        Groups = await BuildGroupsAsync(pending, library, resolution, ct);
        ExpansionGroups = await BuildExpansionGroupsAsync(ct);
        LinkHistory = await BuildLinkHistoryAsync(ct);

        Select(Groups.Count > 0 ? Groups[0] : null);
        SelectExpansion(ExpansionGroups.Count > 0 ? ExpansionGroups[0] : null);
        RequestCovers(_coverWidthPixels);
    }

    // ── Answering an expansion group ─────────────────────────────────────────

    /// <summary>
    /// Groups every checked pack under the base game, in one act and one
    /// transaction, and records a refusal for every pack the user left
    /// unchecked, so an unchecked pack is an answer rather than a card that
    /// comes back on the next scan.
    ///
    /// <para>The link is written at kind <c>expansion_of</c>, which no query
    /// that produces a number reads: the bucket query filters on
    /// <c>same_game</c>, and <c>ExpansionGrouping</c> has no resolver at all.
    /// So this write changes what the details modal shows and nothing
    /// else.</para>
    /// </summary>
    [RelayCommand]
    private async Task GroupExpansionsAsync(ExpansionGroupViewModel? group, CancellationToken ct)
    {
        if (group is null || group.IsDecided)
        {
            return;
        }

        group.IsDecided = true;

        var children = group.IncludedChildWorkIds;
        var refused = group.RefusedPairs;
        var baseTitle = group.BaseTitle;

        ClearReport();

        if (children.Count == 0)
        {
            // Taking none is the same answer as "not expansions", recorded the
            // same way. It is the "none" of none, some or all.
            await _expansionRefusals.RefuseAsync(group.AllPairs, null, ct);
            Report(ExpansionCopy.NothingGrouped);
            RemoveExpansion(group);
            return;
        }

        var actId = await _links.LinkAsync(
            new IdentityLinkRequest
            {
                ParentWorkId = group.BaseWorkId,
                ChildWorkIds = children,
                Kind = IdentityLinkKinds.ExpansionOf,
                Source = IdentityLinkSources.User,
            },
            ct);

        await _expansionRefusals.RefuseAsync(refused, null, ct);

        Report(
            string.Format(
                CultureInfo.CurrentCulture,
                ExpansionCopy.GroupedReportFormat,
                baseTitle,
                children.Count.ToString("N0", CultureInfo.CurrentCulture)),
            actId);

        RemoveExpansion(group);
    }

    /// <summary>
    /// Records every pack on the card as a separate game and removes the card.
    /// Writes no link, so nothing about the library changes at all.
    /// </summary>
    [RelayCommand]
    private async Task NotExpansionsAsync(ExpansionGroupViewModel? group, CancellationToken ct)
    {
        if (group is null || group.IsDecided)
        {
            return;
        }

        group.IsDecided = true;
        await _expansionRefusals.RefuseAsync(group.AllPairs, null, ct);
        RemoveExpansion(group);
    }

    /// <summary>Selection on the expansion surface, shared by pointer and keyboard.</summary>
    /// <param name="group">The card to select, or null to select nothing.</param>
    public void SelectExpansion(ExpansionGroupViewModel? group)
    {
        if (ReferenceEquals(SelectedExpansionGroup, group))
        {
            return;
        }

        if (SelectedExpansionGroup is { } previous)
        {
            previous.IsSelected = false;
        }

        SelectedExpansionGroup = group;
        if (group is not null)
        {
            group.IsSelected = true;
        }
    }

    private void RemoveExpansion(ExpansionGroupViewModel group)
    {
        var index = IndexOfExpansion(group);

        var remaining = new List<ExpansionGroupViewModel>(ExpansionGroups.Count);
        foreach (var existing in ExpansionGroups)
        {
            if (!ReferenceEquals(existing, group))
            {
                remaining.Add(existing);
            }
        }

        ExpansionGroups = remaining;
        SelectExpansion(remaining.Count == 0
            ? null
            : remaining[Math.Clamp(index, 0, remaining.Count - 1)]);
    }

    private int IndexOfExpansion(ExpansionGroupViewModel? group)
    {
        if (group is null)
        {
            return -1;
        }

        for (var i = 0; i < ExpansionGroups.Count; i++)
        {
            if (ReferenceEquals(ExpansionGroups[i], group))
            {
                return i;
            }
        }

        return -1;
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

        ClearReport();

        if (children.Count == 0)
        {
            // Every member unchecked is the same answer as "different games",
            // and it is recorded the same way: no link, and every proposal the
            // answer touched written down.
            await RejectAsync(group.AllCandidateIds, ct);
            Report(MergeCopy.NothingLinked);
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

        Report(
            string.Format(
                CultureInfo.CurrentCulture,
                MergeCopy.LinkedReportFormat,
                primaryTitle,
                children.Count.ToString("N0", CultureInfo.CurrentCulture)),
            actId);

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
        var retracted = await _links.RetractActAsync(actId, null, ct);

        // The reload clears the report, so the outcome is stamped after it
        // rather than before, or the screen would go quiet on the one act whose
        // whole point is that it can be undone.
        await LoadAsync(ct);

        Report(retracted ? MergeCopy.Retracted : MergeCopy.RetractedAlready);
    }

    /// <summary>Stamps the outcome onto whichever surface is up, so a report
    /// cannot outlive the surface that raised it. Written from what the engine
    /// returned, never from what was asked for.</summary>
    /// <param name="message">The outcome line.</param>
    /// <param name="actId">The link act it can be retracted from, or null when
    /// the answer wrote no link.</param>
    private void Report(string message, long? actId = null)
    {
        ReportSurface = IsHistoryVisible
            ? MergeReportSurface.History
            : IsExpansionsVisible
                ? MergeReportSurface.Expansions
                : MergeReportSurface.Review;
        ReportRetractActId = actId;
        ReportMessage = message;
    }

    /// <summary>Drops the standing report. Called on every segment switch and at
    /// the top of <c>LoadAsync</c>; without it one answer left the Amber note up
    /// for the rest of the session.</summary>
    private void ClearReport()
    {
        ReportMessage = null;
        ReportSurface = MergeReportSurface.None;
        ReportRetractActId = null;
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

    /// <summary>
    /// Keyboard navigation (§8): moves expansion selection by
    /// <paramref name="delta"/> cards. Returns the new index, or -1 when the
    /// surface is empty.
    /// </summary>
    /// <param name="delta">Cards to move; positive is down, negative is up.</param>
    /// <returns>The new index, or -1 when there are no expansion cards.</returns>
    public int MoveExpansionSelection(int delta)
    {
        if (ExpansionGroups.Count == 0)
        {
            return -1;
        }

        var current = IndexOfExpansion(SelectedExpansionGroup);
        var next = current < 0
            ? 0
            : Math.Clamp(current + delta, 0, ExpansionGroups.Count - 1);
        SelectExpansion(ExpansionGroups[next]);
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

        foreach (var group in ExpansionGroups)
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
                    await DescribeWorkAsync(member.WorkId, member.ReleaseIds, library, ct),
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
        long workId,
        IReadOnlyList<long> releaseIds,
        LibrarySnapshot library,
        CancellationToken ct)
    {
        var work = await _works.GetAsync(workId, ct);
        var releaseId = releaseIds.Count > 0 ? releaseIds[0] : 0;

        CoverKey? coverKey = null;
        foreach (var candidate in releaseIds)
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

        var stores = new List<string>();
        foreach (var candidate in releaseIds)
        {
            if (!library.Stores.TryGetValue(candidate, out var owned))
            {
                continue;
            }

            foreach (var store in owned)
            {
                if (!stores.Contains(store, StringComparer.OrdinalIgnoreCase))
                {
                    stores.Add(store);
                }
            }
        }

        return new MergeSideViewModel(
            releaseId,
            work?.Name ?? library.Titles.GetValueOrDefault(releaseId, string.Empty),
            null,
            work?.FirstReleaseYear,
            work?.Publisher,
            coverKey,
            _covers,
            releaseIds,
            stores);
    }

    /// <summary>
    /// Builds one card per base game from a fresh scan.
    /// The proposals are re-derived here rather than read from a table, so a
    /// pack the user just grouped, refused or separated is gone or back on the
    /// very next load with no reconciliation pass to write.
    /// </summary>
    private async Task<IReadOnlyList<ExpansionGroupViewModel>> BuildExpansionGroupsAsync(
        CancellationToken ct)
    {
        var report = await _expansions.ScanAsync(ct);

        _scannedExpansions = true;
        OnPropertyChanged(nameof(ExpansionsEmptyMessage));

        if (report.Groups.Count == 0)
        {
            return [];
        }

        var releaseIds = new HashSet<long>();
        foreach (var group in report.Groups)
        {
            foreach (var releaseId in group.Base.ReleaseIds)
            {
                releaseIds.Add(releaseId);
            }

            foreach (var member in group.Members)
            {
                foreach (var releaseId in member.Work.ReleaseIds)
                {
                    releaseIds.Add(releaseId);
                }
            }
        }

        var library = await DescribeAsync(releaseIds, ct);

        var cards = new List<ExpansionGroupViewModel>(report.Groups.Count);
        foreach (var group in report.Groups)
        {
            var members = new List<ExpansionMemberViewModel>(group.Members.Count);
            foreach (var member in group.Members)
            {
                members.Add(new ExpansionMemberViewModel(
                    member.Work.WorkId,
                    await DescribeWorkAsync(
                        member.Work.WorkId, member.Work.ReleaseIds, library, ct),
                    member.Evidence));
            }

            cards.Add(new ExpansionGroupViewModel(
                group.Base.WorkId,
                await DescribeWorkAsync(group.Base.WorkId, group.Base.ReleaseIds, library, ct),
                members));
        }

        return cards;
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

            // An act is an expansion act when EVERY link it wrote is one. The
            // test is "all" rather than "any" because a same-game act can
            // carry expansion links it displaced and re-parented, and the row
            // must describe the act the user performed, not the repair it
            // happened to include.
            var expansion = true;

            foreach (var link in members)
            {
                childTitles.Add(await NameOfAsync(link.ChildWorkId));
                if (link.Kind != IdentityLinkKinds.ExpansionOf)
                {
                    expansion = false;
                }

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
                act, await NameOfAsync(parentWorkId), childTitles, live, retractedAt, expansion));
        }

        rows.Reverse();
        return rows;
    }

    /// <summary>What one load read about the releases the queue names.</summary>
    private sealed record LibrarySnapshot(
        Dictionary<long, string> Titles,
        Dictionary<long, CoverKey> CoverKeys,
        Dictionary<long, long> WorkOfRelease,
        Dictionary<long, SurvivorCandidate> Works,
        Dictionary<long, IReadOnlyList<string>> Stores);

    private async Task<LibrarySnapshot> DescribeAsync(
        IEnumerable<long> releaseIds, CancellationToken ct)
    {
        var titles = new Dictionary<long, string>();
        var coverKeys = new Dictionary<long, CoverKey>();
        var workOfRelease = new Dictionary<long, long>();
        var works = new Dictionary<long, SurvivorCandidate>();
        var stores = new Dictionary<long, IReadOnlyList<string>>();

        foreach (var releaseId in releaseIds)
        {
            var release = await _releases.GetAsync(releaseId, ct);
            if (release is null)
            {
                continue;
            }

            workOfRelease[releaseId] = release.WorkId;

            // The store is the fact that decides whether a pair is one game
            // on two storefronts, so it is read from the ownership rows for
            // every entry the queue names rather than derived from the cover
            // key or the external-id provider.
            var owned = await _ownership.GetByReleaseAsync(releaseId, ct);
            if (owned.Count > 0)
            {
                stores[releaseId] = [.. owned.Select(o => o.Store)];
            }

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

        return new LibrarySnapshot(titles, coverKeys, workOfRelease, works, stores);
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
