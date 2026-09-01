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
/// Merge confirm queue: shows all pending soft matches, strongest first, for
/// the user to confirm or reject. Both answers are terminal — a decided pair
/// is never re-asked.
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

    // MergeExecutor is required, not optional. An engine registered in the
    // container and resolved nowhere is indistinguishable from one that works;
    // omitting it must break the container at startup rather than render a
    // screen with its applying and history sections quietly absent.
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

    /// <summary>The pending soft-match pairs, sorted strongest first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(PendingCount), nameof(PendingCountText), nameof(HasPending),
        nameof(ShowEmpty), nameof(RowOpacity))]
    public partial IReadOnlyList<MergeCandidateViewModel> Candidates { get; set; } = [];

    /// <summary>The pair the user is currently looking at, or null when the queue is empty.</summary>
    [ObservableProperty]
    public partial MergeCandidateViewModel? SelectedCandidate { get; set; }

    /// <summary>Number of pairs still waiting for an answer.</summary>
    public int PendingCount => Candidates.Count;

    /// <summary>Plex Mono, tabular, grouped — every number in the app (§3).</summary>
    public string PendingCountText => PendingCount.ToString("N0");

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

    // ── Applying (TASK-64) ───────────────────────────────────────────────────

    /// <summary>
    /// The pairs the user has answered "Same game" that nothing has carried out
    /// yet. Answering writes a status; this section is where that status becomes
    /// a merge.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(OutstandingCount), nameof(OutstandingCountText),
        nameof(HasOutstanding), nameof(ShowOutstandingEmpty))]
    public partial IReadOnlyList<MergeApplyViewModel> Outstanding { get; set; } = [];

    /// <summary>Number of confirmed pairs waiting to be applied.</summary>
    public int OutstandingCount => Outstanding.Count;

    /// <summary>Plex Mono, tabular, grouped — every number in the app (§3).</summary>
    public string OutstandingCountText => OutstandingCount.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>True when confirmed pairs are waiting to be applied.</summary>
    public bool HasOutstanding => OutstandingCount > 0;

    /// <summary>True once the screen has loaded and no confirmed pairs are waiting.</summary>
    public bool ShowOutstandingEmpty => _loaded && OutstandingCount == 0;

    // ── History and undo (TASK-62) ───────────────────────────────────────────

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

    /// <summary>
    /// What the last apply or undo actually did. Written from the outcome the
    /// engine returned, never from what was asked for.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReport))]
    public partial string? ReportMessage { get; set; }

    /// <summary>True when there is an outcome to display.</summary>
    public bool HasReport => !string.IsNullOrEmpty(ReportMessage);

    // ── Chrome the view binds to ─────────────────────────────────────────────

    /// <summary>Section heading for confirmed-but-unapplied pairs.</summary>
    public string ApplyHeading => MergeCopy.ApplyHeading;

    /// <summary>Introduction under the apply heading.</summary>
    public string ApplyIntro => MergeCopy.ApplyIntro;

    /// <summary>Empty state for the apply section.</summary>
    public string ApplyEmptyMessage => MergeCopy.ApplyEmpty;

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

    /// <summary>Tooltip on the "Same game" button, explaining that it records the answer without applying.</summary>
    public string SameGameTooltip => MergeCopy.SameGameTooltip;

    /// <summary>Tooltip on the "Different games" button, stating permanence.</summary>
    public string DifferentGamesTooltip => MergeCopy.DifferentGamesTooltip;

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
        Select(cards.Count > 0 ? cards[0] : null);
        RequestCovers(_coverWidthPixels);
    }

    // ── Applying ─────────────────────────────────────────────────────────────

    // Applies one pair and reports the outcome the engine returned. A refused
    // plan writes nothing and says so; it is never silently dropped.
    [RelayCommand]
    private async Task ApplyAsync(MergeApplyViewModel? row, CancellationToken ct)
    {
        if (row is null || !row.CanApply)
        {
            return;
        }

        row.IsApplying = true;

        var outcome = await _merges.ApplyAsync(row.Id, ct);
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

    /// <summary>Writes <c>confirmed</c> and removes the pair from the queue.</summary>
    [RelayCommand]
    private Task SameGameAsync(MergeCandidateViewModel? candidate)
        => DecideAsync(candidate, MergeCandidateStatuses.Confirmed);

    /// <summary>Writes <c>rejected</c> (permanent) and removes the pair from the queue.</summary>
    [RelayCommand]
    private Task DifferentGamesAsync(MergeCandidateViewModel? candidate)
        => DecideAsync(candidate, MergeCandidateStatuses.Rejected);

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

    private async Task DecideAsync(MergeCandidateViewModel? candidate, string status)
    {
        if (candidate is null || candidate.IsDecided)
        {
            return;
        }

        // Latch before await to prevent double-writes from rapid clicks.
        candidate.IsDecided = true;

        // TODO(data-layer M1): `confirmed` records intent only — releases are
        // NOT merged yet. Merge execution (repointing ownerships, events,
        // external_ids, collapsing works, tombstoning losers) belongs in a
        // separate commit with its own integrity review.
        await _candidates.SetStatusAsync(candidate.Id, status);

        var remaining = new List<MergeCandidateViewModel>(Candidates.Count);
        foreach (var existing in Candidates)
        {
            if (!ReferenceEquals(existing, candidate))
            {
                remaining.Add(existing);
            }
        }

        var index = IndexOf(candidate);
        Candidates = remaining;

        // Select the pair that slid into the answered one's place.
        if (remaining.Count == 0)
        {
            Select(null);
        }
        else
        {
            Select(remaining[Math.Clamp(index, 0, remaining.Count - 1)]);
        }
    }

    // The describe pass returns work ids beside the titles so BuildOutstanding
    // can identify which release carries the surviving identity without a
    // second round of lookups.
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
