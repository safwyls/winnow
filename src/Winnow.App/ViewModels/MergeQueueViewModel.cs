using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Merging;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Covers;
using Winnow.Covers.Igdb;
using Winnow.Resolve;
using Winnow.Resolve.Matching;

namespace Winnow.App.ViewModels;

/// <summary>
/// The Merges screen: one queue of proposal cards in five sections, a header
/// carrying the sort, the safe bulk path and the selection's primary, a cut
/// bar filtering to one kind, and an ambient dock whose Undo reverses the
/// last act or the last run of dismissals.
///
/// <para>Two product rules drive everything here. Merging is non-destructive:
/// an answer writes a link act that nests the other rows under the header,
/// nothing is deleted, and Separate again retracts the act. And the choice is
/// the user's: Winnow proposes, never auto-merges; Different games is
/// permanent, a confidence is a word and never a reason to act.</para>
///
/// <para>Same-game proposals and expansion proposals arrive through different
/// contracts (<c>merge_candidates</c> and a scan) and are one kind of card
/// here. Past link acts arrive as resolved strips in their own section, so
/// the queue is the retraction surface for every relation and there is no
/// history list.</para>
/// </summary>
public partial class MergeQueueViewModel : ObservableObject, IDisposable
{
    /// <summary>The row thumbnail's width in device-independent pixels: 34, a 2:3 portrait at 51 tall.</summary>
    public const double CoverWidth = 34;

    /// <summary>How long the dock stays up before it dismisses itself.</summary>
    public static readonly TimeSpan DockFor = TimeSpan.FromSeconds(7);

    private const double ExactTitleFloor = 0.999;

    private readonly IMergeCandidateRepository _candidates;
    private readonly IReleaseRepository _releases;
    private readonly IWorkRepository _works;
    private readonly IIdentityLinkRepository _links;
    private readonly IOwnershipRepository _ownership;
    private readonly LibraryExpansionScan _expansions;
    private readonly IExpansionRefusalRepository _expansionRefusals;
    private readonly ILibraryQueryRepository _libraryQueries;
    private readonly ICoverCache? _covers;
    private readonly IResolveStateRepository? _resolveState;
    private readonly TimeProvider _clock;
    private readonly Action<Action> _post;

    private readonly Dictionary<MergeRowViewModel, MergeCardViewModel> _cardOfRow = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<MergeCardViewModel, MergeSectionViewModel> _sectionOfCard = new(ReferenceEqualityComparer.Instance);

    private double _coverWidthPixels;
    private bool _loaded;
    private ITimer? _dockTimer;
    private UndoRun? _run;
    private bool _disposed;

    /// <summary>
    /// The link, ownership and library-query repositories are required, not
    /// optional. A type registered in the container and resolved nowhere is
    /// indistinguishable from one that works; omitting one must break the
    /// container at startup rather than render a screen whose rows show no
    /// hours or whose answers quietly write nothing.
    /// </summary>
    public MergeQueueViewModel(
        IMergeCandidateRepository candidates,
        IReleaseRepository releases,
        IWorkRepository works,
        IIdentityLinkRepository links,
        IOwnershipRepository ownership,
        LibraryExpansionScan expansions,
        IExpansionRefusalRepository expansionRefusals,
        ILibraryQueryRepository libraryQueries,
        ICoverCache? covers = null,
        IResolveStateRepository? resolveState = null,
        TimeProvider? clock = null,
        Action<Action>? post = null)
    {
        _candidates = candidates;
        _releases = releases;
        _works = works;
        _links = links;
        _ownership = ownership;
        _expansions = expansions;
        _expansionRefusals = expansionRefusals;
        _libraryQueries = libraryQueries;
        _covers = covers;
        _resolveState = resolveState;
        _clock = clock ?? TimeProvider.System;
        _post = post ?? (action => Avalonia.Threading.Dispatcher.UIThread.Post(action));

        Sections =
        [
            new MergeSectionViewModel(MergeSectionKind.Stores, MergeCopy.SectionStores, MergeCopy.SectionStoresBlurb),
            new MergeSectionViewModel(MergeSectionKind.Editions, MergeCopy.SectionEditions, MergeCopy.SectionEditionsBlurb),
            new MergeSectionViewModel(MergeSectionKind.Expansions, MergeCopy.SectionExpansions, MergeCopy.SectionExpansionsBlurb),
            new MergeSectionViewModel(MergeSectionKind.Parts, MergeCopy.SectionParts, MergeCopy.SectionPartsBlurb),
            new MergeSectionViewModel(MergeSectionKind.Tests, MergeCopy.SectionTests, MergeCopy.SectionTestsBlurb),
        ];

        SortOptions =
        [
            new MergeSortOptionViewModel(MergeSort.StrongestMatch, MergeCopy.SortStrongestMatch) { IsSelected = true },
            new MergeSortOptionViewModel(MergeSort.PlaytimeAtStake, MergeCopy.SortPlaytimeAtStake),
            new MergeSortOptionViewModel(MergeSort.Title, MergeCopy.SortTitle),
        ];

        KindOptions =
        [
            new MergeKindOptionViewModel(null, MergeCopy.KindAll) { IsSelected = true },
            new MergeKindOptionViewModel(MergeSectionKind.Stores, MergeCopy.KindStores),
            new MergeKindOptionViewModel(MergeSectionKind.Editions, MergeCopy.KindEditions),
            new MergeKindOptionViewModel(MergeSectionKind.Expansions, MergeCopy.KindExpansions),
            new MergeKindOptionViewModel(MergeSectionKind.Parts, MergeCopy.KindParts),
            new MergeKindOptionViewModel(MergeSectionKind.Tests, MergeCopy.KindTests),
        ];
    }

    // ── The queue ────────────────────────────────────────────────────────────

    /// <summary>The five sections, in drawing order. Fixed for the life of the screen.</summary>
    public IReadOnlyList<MergeSectionViewModel> Sections { get; }

    /// <summary>The sort menu's rows.</summary>
    public IReadOnlyList<MergeSortOptionViewModel> SortOptions { get; }

    /// <summary>The cut bar's segments.</summary>
    public IReadOnlyList<MergeKindOptionViewModel> KindOptions { get; }

    /// <summary>The order pending cards take within each section.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortLabel), nameof(SortAutomationName))]
    public partial MergeSort Sort { get; private set; }

    /// <summary>The one section shown, or null for all of them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsKindFiltered), nameof(KindChipLabel), nameof(CutCountText),
        nameof(ShownPendingCount), nameof(PendingLine), nameof(ExactCount),
        nameof(AcceptExactLabel), nameof(CanAcceptExact))]
    public partial MergeSectionKind? Kind { get; private set; }

    /// <summary>True once one soft-match sweep has completed. False when unknown or unregistered.</summary>
    [ObservableProperty]
    public partial bool HasCompletedSweep { get; set; }

    /// <summary>Cards waiting for an answer, every section counted.</summary>
    public int PendingCount => Sections.Sum(section => section.PendingCount);

    /// <summary>Cards waiting for an answer in the sections the filter shows.</summary>
    public int ShownPendingCount => Sections.Where(section => section.IsVisible).Sum(section => section.PendingCount);

    /// <summary>True when anything is waiting.</summary>
    public bool HasPending => PendingCount > 0;

    /// <summary>True once the screen has loaded.</summary>
    public bool IsLoaded => _loaded;

    /// <summary>The count line beside the title, for the sections shown.</summary>
    public string PendingLine => ShownPendingCount switch
    {
        0 => MergeCopy.NothingWaiting,
        1 => string.Format(CultureInfo.CurrentCulture, MergeCopy.PendingOneFormat, "1"),
        var n => string.Format(
            CultureInfo.CurrentCulture, MergeCopy.PendingManyFormat, n.ToString("N0", CultureInfo.CurrentCulture)),
    };

    /// <summary>The count at the right of the cut bar: the pending total, or total → shown while filtered.</summary>
    public string CutCountText
    {
        get
        {
            var all = PendingCount.ToString("N0", CultureInfo.CurrentCulture);
            return Kind is null
                ? all
                : string.Format(
                    CultureInfo.CurrentCulture,
                    MergeCopy.CutCountFormat,
                    all,
                    ShownPendingCount.ToString("N0", CultureInfo.CurrentCulture));
        }
    }

    // ── Sort ─────────────────────────────────────────────────────────────────

    /// <summary>The sort button's label.</summary>
    public string SortLabel => string.Format(
        CultureInfo.CurrentCulture, MergeCopy.SortButtonFormat, LabelOf(Sort));

    /// <summary>What a screen reader calls the sort button.</summary>
    public string SortAutomationName => string.Format(
        CultureInfo.CurrentCulture, MergeCopy.SortAutomationFormat, LabelOf(Sort));

    /// <summary>Tooltip on the sort button.</summary>
    public string SortTooltip => MergeCopy.SortTooltip;

    /// <summary>Re-orders every section by <paramref name="option"/>.</summary>
    [RelayCommand]
    private void SelectSort(MergeSortOptionViewModel? option)
    {
        if (option is null)
        {
            return;
        }

        foreach (var candidate in SortOptions)
        {
            candidate.IsSelected = ReferenceEquals(candidate, option);
        }

        Sort = option.Sort;
        foreach (var section in Sections)
        {
            section.Resort(Sort);
        }
    }

    private string LabelOf(MergeSort sort) =>
        SortOptions.First(option => option.Sort == sort).Label;

    // ── Kind filter ──────────────────────────────────────────────────────────

    /// <summary>True while one section is shown alone.</summary>
    public bool IsKindFiltered => Kind is not null;

    /// <summary>The cut chip's words: the shown section's full title.</summary>
    public string KindChipLabel => Kind is { } kind
        ? Sections.First(section => section.Kind == kind).Title
        : string.Empty;

    /// <summary>Tooltip on the cut chip.</summary>
    public string KindChipTooltip => MergeCopy.KindChipTooltip;

    /// <summary>Tooltip on the chip's ✕.</summary>
    public string KindChipClearTip => MergeCopy.KindChipClearTip;

    /// <summary>Shows one section, or every section for the ALL segment.</summary>
    [RelayCommand]
    private void SelectKind(MergeKindOptionViewModel? option)
    {
        if (option is null)
        {
            return;
        }

        foreach (var candidate in KindOptions)
        {
            candidate.IsSelected = ReferenceEquals(candidate, option);
        }

        Kind = option.Kind;
        foreach (var section in Sections)
        {
            section.IsVisible = Kind is null || section.Kind == Kind;
        }

        RefreshCounts();

        // The cursor must not stay on a card the filter just hid, or S would
        // answer a card the user cannot see.
        var rows = VisibleRows();
        if (FocusedRow is null || !rows.Contains(FocusedRow))
        {
            Focus(rows.Count > 0 ? rows[0] : null);
        }
    }

    /// <summary>The chip's ✕: back to every section.</summary>
    [RelayCommand]
    private void ClearKind() => SelectKind(KindOptions[0]);

    // ── Header buttons ───────────────────────────────────────────────────────

    /// <summary>Pending EXACT MATCH cards in ACROSS STORES, while that section is shown.</summary>
    public int ExactCount => ExactCards().Count;

    /// <summary>The bulk accept button's label; counts live.</summary>
    public string AcceptExactLabel => ExactCount switch
    {
        0 => MergeCopy.AcceptExactNone,
        1 => string.Format(CultureInfo.CurrentCulture, MergeCopy.AcceptExactOneFormat, "1"),
        var n => string.Format(
            CultureInfo.CurrentCulture, MergeCopy.AcceptExactFormat, n.ToString("N0", CultureInfo.CurrentCulture)),
    };

    /// <summary>True while there is an exact cross-store group to accept.</summary>
    public bool CanAcceptExact => ExactCount > 0;

    /// <summary>Tooltip on the bulk accept button.</summary>
    public string AcceptExactTooltip => MergeCopy.AcceptExactTooltip;

    /// <summary>How many pending cards are checked, in every section.</summary>
    public int SelectedCount => SelectedCards().Count;

    /// <summary>The primary button's label; counts live.</summary>
    public string MergeSelectedLabel => SelectedCount == 0
        ? MergeCopy.MergeSelectedNone
        : string.Format(
            CultureInfo.CurrentCulture,
            MergeCopy.MergeSelectedFormat,
            SelectedCount.ToString("N0", CultureInfo.CurrentCulture));

    /// <summary>True while a card is checked.</summary>
    public bool CanMergeSelected => SelectedCount > 0;

    /// <summary>Tooltip on the primary button.</summary>
    public string MergeSelectedTooltip => MergeCopy.MergeSelectedTooltip;

    // ── Copy the view binds to ───────────────────────────────────────────────

    /// <summary>The h1.</summary>
    public string Title => MergeCopy.Title;

    /// <summary>The rail row's label.</summary>
    public string RailLabel => MergeCopy.RailLabel;

    /// <summary>The rail row's tooltip.</summary>
    public string RailTooltip => MergeCopy.RailTooltip;

    /// <summary>The dock's one control.</summary>
    public string UndoButtonText => MergeCopy.UndoButton;

    /// <summary>Tooltip on Undo.</summary>
    public string UndoTooltip => MergeCopy.UndoTooltip;

    /// <summary>Tooltip on the dock's ✕.</summary>
    public string DockCloseTip => MergeCopy.DockCloseTip;

    /// <summary>Label on every card's affirmative answer.</summary>
    public string SameGameButtonText => MergeCopy.SameGameButton;

    /// <summary>Label on every card's negative answer.</summary>
    public string DifferentGamesButtonText => MergeCopy.DifferentGamesButton;

    /// <summary>Tooltip on Same game.</summary>
    public string SameGameTooltip => MergeCopy.SameGameTooltip;

    /// <summary>Tooltip on Different games.</summary>
    public string DifferentGamesTooltip => MergeCopy.DifferentGamesTooltip;

    /// <summary>Label on every strip's control.</summary>
    public string SeparateButtonText => MergeCopy.SeparateButton;

    /// <summary>Tooltip on Separate again.</summary>
    public string SeparateTooltip => MergeCopy.SeparateTooltip;

    // ── The dock ─────────────────────────────────────────────────────────────

    /// <summary>True while the dock card is up.</summary>
    [ObservableProperty]
    public partial bool IsDockOpen { get; private set; }

    /// <summary>The dock's title line.</summary>
    [ObservableProperty]
    public partial string DockTitle { get; private set; } = string.Empty;

    /// <summary>The dock's note.</summary>
    [ObservableProperty]
    public partial string DockNote { get; private set; } = string.Empty;

    /// <summary>The ✕: closes the dock early. The act stands.</summary>
    [RelayCommand]
    private void DismissDock() => CloseDock(forget: true);

    /// <summary>
    /// Restores the state the last act or run of dismissals started from:
    /// retracts every link the run wrote and puts every dismissed card back
    /// where it was, with its proposals pending again.
    /// </summary>
    [RelayCommand]
    private async Task UndoAsync(CancellationToken ct)
    {
        if (_run is not { } run)
        {
            return;
        }

        CloseDock(forget: true);

        foreach (var linked in run.Linked)
        {
            await _links.RetractActAsync(linked.ActId, null, ct);
            foreach (var candidateId in linked.RejectedCandidateIds)
            {
                await _candidates.SetStatusAsync(candidateId, MergeCandidateStatuses.Pending, ct);
            }

            if (linked.RefusedPairs.Count > 0)
            {
                await _expansionRefusals.RetractAsync(linked.RefusedPairs, ct);
            }

            linked.Card.MarkPending();
            if (_sectionOfCard.TryGetValue(linked.Card, out var section))
            {
                section.Refresh();
            }
        }

        // Newest dismissal first, so each card lands at the index it left
        // from with the cards dismissed after it already back in place.
        for (var i = run.Dismissed.Count - 1; i >= 0; i--)
        {
            var (section, card, index) = run.Dismissed[i];
            foreach (var candidateId in card.CandidateIds)
            {
                await _candidates.SetStatusAsync(candidateId, MergeCandidateStatuses.Pending, ct);
            }

            if (card.RefusalPairs.Count > 0)
            {
                await _expansionRefusals.RetractAsync(card.RefusalPairs, ct);
            }

            card.IsDecided = false;
            section.Insert(card, index);
        }

        RefreshCounts();
        if (run.Linked.Count > 0)
        {
            Focus(run.Linked[0].Card.Rows[0]);
        }
        else if (run.Dismissed.Count > 0)
        {
            Focus(run.Dismissed[0].Card.Rows[0]);
        }
    }

    private void ShowDock(UndoRun run, string title, string note)
    {
        _run = run;
        DockTitle = title;
        DockNote = note;
        IsDockOpen = true;

        _dockTimer?.Dispose();
        _dockTimer = _clock.CreateTimer(
            _ => _post(() => CloseDock(forget: true)), null, DockFor, Timeout.InfiniteTimeSpan);
    }

    private void CloseDock(bool forget)
    {
        _dockTimer?.Dispose();
        _dockTimer = null;
        IsDockOpen = false;
        if (forget)
        {
            _run = null;
        }
    }

    // ── Loading ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct)
    {
        // Must be read before the queue so an empty section knows if the matcher has run.
        HasCompletedSweep = _resolveState is not null
            && await _resolveState.GetLastSoftMatchSweepAsync(ct) is not null;

        var pending = await _candidates.GetPendingAsync(ct);
        var resolution = await _links.GetResolutionAsync(ct);
        var scan = await _expansions.ScanAsync(ct);
        var history = await _links.GetHistoryAsync(null, ct);
        var acts = await _links.GetActsAsync(ct);

        var releaseIds = new HashSet<long>();
        foreach (var candidate in pending)
        {
            releaseIds.Add(candidate.LeftReleaseId);
            releaseIds.Add(candidate.RightReleaseId);
        }

        foreach (var group in scan.Groups)
        {
            releaseIds.UnionWith(group.Base.ReleaseIds);
            foreach (var member in group.Members)
            {
                releaseIds.UnionWith(member.Work.ReleaseIds);
            }
        }

        var standing = StandingActs(history, acts);
        var releasesOfWork = new Dictionary<long, IReadOnlyList<long>>();
        foreach (var act in standing)
        {
            foreach (var workId in act.WorkIds)
            {
                if (!releasesOfWork.ContainsKey(workId))
                {
                    var owned = await _releases.GetByWorkAsync(workId, ct);
                    releasesOfWork[workId] = [.. owned.Select(release => release.Id).Order()];
                    releaseIds.UnionWith(releasesOfWork[workId]);
                }
            }
        }

        var library = await DescribeAsync(releaseIds, ct);
        var now = _clock.GetUtcNow().UtcDateTime;

        var cards = new List<MergeCardViewModel>();
        cards.AddRange(await BuildSameGameCardsAsync(pending, library, resolution, now, ct));
        cards.AddRange(await BuildExpansionCardsAsync(scan, library, now, ct));
        cards.AddRange(await BuildStandingCardsAsync(standing, releasesOfWork, library, now, ct));

        _loaded = true;
        Place(cards);

        Focus(VisibleRows().FirstOrDefault());
        RequestCovers(_coverWidthPixels);
    }

    private void Place(IReadOnlyList<MergeCardViewModel> cards)
    {
        foreach (var existing in _sectionOfCard.Keys)
        {
            existing.PropertyChanged -= OnCardChanged;
        }

        _cardOfRow.Clear();
        _sectionOfCard.Clear();

        foreach (var section in Sections)
        {
            var mine = new List<MergeCardViewModel>();
            foreach (var card in cards)
            {
                if (card.Section != section.Kind)
                {
                    continue;
                }

                mine.Add(card);
                _sectionOfCard[card] = section;
                card.PropertyChanged += OnCardChanged;
                foreach (var row in card.Rows)
                {
                    _cardOfRow[row] = card;
                }
            }

            section.Replace(mine, Sort);
            section.IsVisible = Kind is null || section.Kind == Kind;
            section.EmptyText = IsSameGameSection(section.Kind) && !HasCompletedSweep
                ? MergeCopy.SectionNotSwept
                : MergeCopy.SectionEmpty;
        }

        RefreshCounts();
    }

    private static bool IsSameGameSection(MergeSectionKind kind) =>
        kind is MergeSectionKind.Stores or MergeSectionKind.Editions;

    private void OnCardChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MergeCardViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(MergeSelectedLabel));
            OnPropertyChanged(nameof(CanMergeSelected));
        }
    }

    private void RefreshCounts()
    {
        foreach (var section in Sections)
        {
            section.Refresh();
        }

        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(ShownPendingCount));
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(PendingLine));
        OnPropertyChanged(nameof(CutCountText));
        OnPropertyChanged(nameof(ExactCount));
        OnPropertyChanged(nameof(AcceptExactLabel));
        OnPropertyChanged(nameof(CanAcceptExact));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(MergeSelectedLabel));
        OnPropertyChanged(nameof(CanMergeSelected));
    }

    // ── Answering ────────────────────────────────────────────────────────────

    /// <summary>
    /// Nests every checked row under the header, in one act and one
    /// transaction, and records the proposals a left-out row had with the
    /// linked rows as answered no. Nothing else on screen is re-read: a link
    /// inside one card cannot change another, and the card becomes a strip
    /// in place.
    /// </summary>
    [RelayCommand]
    private async Task SameGameAsync(MergeCardViewModel? card, CancellationToken ct)
    {
        if (card is null || !card.CanAnswer)
        {
            return;
        }

        // Latch before the await so a double click cannot write twice.
        card.IsDecided = true;
        var linked = await LinkAsync(card, ct);
        card.MarkResolved(linked.ActId);

        RefreshCounts();
        AdvanceFocusFrom(card);

        var nested = card.ChildWorkIds.Count;
        var leftOut = card.ExcludedRows.Count;
        ShowDock(
            new UndoRun(UndoKind.Merge, [linked], []),
            string.Format(CultureInfo.CurrentCulture, MergeCopy.DockRolledUnderFormat, card.HeaderTitle),
            leftOut > 0
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    MergeCopy.DockNestedLeftOutFormat,
                    nested.ToString("N0", CultureInfo.CurrentCulture),
                    leftOut.ToString("N0", CultureInfo.CurrentCulture))
                : nested == 1
                    ? MergeCopy.DockNestedOne
                    : string.Format(
                        CultureInfo.CurrentCulture,
                        MergeCopy.DockNestedManyFormat,
                        nested.ToString("N0", CultureInfo.CurrentCulture)));
    }

    /// <summary>
    /// A click on a row asks the shell to open the game's details, so the
    /// entries can be compared before answering. The screen itself knows
    /// nothing about the modal; the library owns it.
    /// </summary>
    [RelayCommand]
    private void OpenDetails(MergeRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        Focus(row, announce: false);
        DetailsRequested?.Invoke(row);
    }

    /// <summary>Raised when a row asks for the game's details.</summary>
    public event Action<MergeRowViewModel>? DetailsRequested;

    /// <summary>
    /// Records every proposal on the card as answered no, and removes the
    /// card. Consecutive dismissals share one dock card and one Undo.
    /// </summary>
    [RelayCommand]
    private async Task DifferentGamesAsync(MergeCardViewModel? card, CancellationToken ct)
    {
        if (card is null || card.IsDecided || card.IsResolved
            || !_sectionOfCard.TryGetValue(card, out var section))
        {
            return;
        }

        card.IsDecided = true;
        foreach (var candidateId in card.CandidateIds)
        {
            await _candidates.SetStatusAsync(candidateId, MergeCandidateStatuses.Rejected, ct);
        }

        if (card.RefusalPairs.Count > 0)
        {
            await _expansionRefusals.RefuseAsync(card.RefusalPairs, null, ct);
        }

        var index = section.IndexOf(card);
        AdvanceFocusFrom(card);
        section.Remove(card);
        card.IsSelected = false;
        RefreshCounts();

        var run = IsDockOpen && _run is { Kind: UndoKind.Dismiss } standing
            ? standing
            : new UndoRun(UndoKind.Dismiss, [], []);
        run.Dismissed.Add((section, card, index));

        ShowDock(
            run,
            run.Dismissed.Count == 1
                ? MergeCopy.DockLeftOneAlone
                : string.Format(
                    CultureInfo.CurrentCulture,
                    MergeCopy.DockLeftAloneFormat,
                    run.Dismissed.Count.ToString("N0", CultureInfo.CurrentCulture)),
            MergeCopy.DockStaySeparate);
    }

    /// <summary>
    /// Retracts the strip's act. A card answered this session returns to a
    /// question in place; a strip loaded from an earlier session reloads the
    /// queue, because the proposals it came from are not on the card.
    /// </summary>
    [RelayCommand]
    private async Task SeparateAsync(MergeCardViewModel? card, CancellationToken ct)
    {
        if (card is not { IsResolved: true, ActId: { } actId })
        {
            return;
        }

        await _links.RetractActAsync(actId, null, ct);

        if (card.IsFromHistory)
        {
            await LoadAsync(ct);
            return;
        }

        card.MarkPending();
        RefreshCounts();
        Focus(card.Rows[0]);
    }

    /// <summary>Rolls up every checked card, each under the header the user picked.</summary>
    [RelayCommand]
    private async Task MergeSelectedAsync(CancellationToken ct)
    {
        var cards = SelectedCards();
        if (cards.Count == 0)
        {
            return;
        }

        var linked = await LinkAllAsync(cards, ct);
        ShowDock(
            new UndoRun(UndoKind.Merge, linked, []),
            linked.Count == 1
                ? MergeCopy.DockRolledOneGroup
                : string.Format(
                    CultureInfo.CurrentCulture,
                    MergeCopy.DockRolledGroupsFormat,
                    linked.Count.ToString("N0", CultureInfo.CurrentCulture)),
            MergeCopy.DockEachKeptHeader);
    }

    /// <summary>The safe bulk path: EXACT MATCH cards in ACROSS STORES only.</summary>
    [RelayCommand]
    private async Task AcceptExactAsync(CancellationToken ct)
    {
        var cards = ExactCards();
        if (cards.Count == 0)
        {
            return;
        }

        var linked = await LinkAllAsync(cards, ct);
        ShowDock(
            new UndoRun(UndoKind.Merge, linked, []),
            linked.Count == 1
                ? MergeCopy.DockRolledOneExact
                : string.Format(
                    CultureInfo.CurrentCulture,
                    MergeCopy.DockRolledExactFormat,
                    linked.Count.ToString("N0", CultureInfo.CurrentCulture)),
            MergeCopy.DockCrossStoreOnly);
    }

    private async Task<List<LinkedAct>> LinkAllAsync(
        IReadOnlyList<MergeCardViewModel> cards, CancellationToken ct)
    {
        var linked = new List<LinkedAct>(cards.Count);
        foreach (var card in cards)
        {
            if (!card.CanAnswer)
            {
                continue;
            }

            card.IsDecided = true;
            var act = await LinkAsync(card, ct);
            card.MarkResolved(act.ActId);
            linked.Add(act);
        }

        RefreshCounts();
        if (linked.Count > 0)
        {
            AdvanceFocusFrom(linked[0].Card);
        }

        return linked;
    }

    // The link, then the proposals the answer left outside it. Both are
    // remembered so one Undo reverses the whole answer.
    private async Task<LinkedAct> LinkAsync(MergeCardViewModel card, CancellationToken ct)
    {
        var actId = await _links.LinkAsync(
            new IdentityLinkRequest
            {
                ParentWorkId = card.ParentWorkId,
                ChildWorkIds = card.ChildWorkIds,
                Kind = card.LinkKind,
                Source = IdentityLinkSources.User,
                RelationLabel = card.RelationLabel,
            },
            ct);

        var rejected = card.RejectedCandidateIds;
        foreach (var candidateId in rejected)
        {
            await _candidates.SetStatusAsync(candidateId, MergeCandidateStatuses.Rejected, ct);
        }

        var refused = card.RefusedPairs;
        if (refused.Count > 0)
        {
            await _expansionRefusals.RefuseAsync(refused, null, ct);
        }

        return new LinkedAct(card, actId, rejected, refused);
    }

    private List<MergeCardViewModel> SelectedCards()
    {
        var selected = new List<MergeCardViewModel>();
        foreach (var section in Sections)
        {
            foreach (var card in section.Cards)
            {
                if (card.IsPending && card.IsSelected)
                {
                    selected.Add(card);
                }
            }
        }

        return selected;
    }

    private List<MergeCardViewModel> ExactCards()
    {
        var exact = new List<MergeCardViewModel>();
        foreach (var section in Sections)
        {
            if (section.Kind != MergeSectionKind.Stores || !section.IsVisible)
            {
                continue;
            }

            foreach (var card in section.Cards)
            {
                if (card.IsPending && card.IsExact)
                {
                    exact.Add(card);
                }
            }
        }

        return exact;
    }

    // ── Keyboard ─────────────────────────────────────────────────────────────

    /// <summary>The row the keyboard cursor is on, or null when nothing is pending.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FocusedCard))]
    public partial MergeRowViewModel? FocusedRow { get; private set; }

    /// <summary>The card the cursor's row belongs to. S and D act on it.</summary>
    public MergeCardViewModel? FocusedCard =>
        FocusedRow is { } row && _cardOfRow.TryGetValue(row, out var card) ? card : null;

    /// <summary>
    /// Raised when the cursor moves for a reason other than the view's own
    /// focus, so the view can put keyboard focus on the row and scroll it
    /// into view.
    /// </summary>
    public event Action<MergeRowViewModel>? FocusRequested;

    /// <summary>Selection follows focus: the view reports the row that took focus.</summary>
    public void FocusRow(MergeRowViewModel? row) => Focus(row, announce: false);

    /// <summary>Moves the cursor by <paramref name="delta"/> rows across every visible pending card. Returns the row, or null when nothing is pending.</summary>
    public MergeRowViewModel? MoveFocus(int delta)
    {
        var rows = VisibleRows();
        if (rows.Count == 0)
        {
            Focus(null);
            return null;
        }

        var current = FocusedRow is { } focused ? rows.IndexOf(focused) : -1;
        var next = current < 0 ? 0 : Math.Clamp(current + delta, 0, rows.Count - 1);
        Focus(rows[next]);
        return rows[next];
    }

    /// <summary>Space: makes the cursor's row the header.</summary>
    public void PromoteFocused()
    {
        if (FocusedRow is { } row && FocusedCard is { } card)
        {
            card.Promote(row);
        }
    }

    /// <summary>Row → card, for the view's pointer handlers.</summary>
    public MergeCardViewModel? CardOf(MergeRowViewModel row) =>
        _cardOfRow.TryGetValue(row, out var card) ? card : null;

    private List<MergeRowViewModel> VisibleRows()
    {
        var rows = new List<MergeRowViewModel>();
        foreach (var section in Sections)
        {
            if (!section.IsVisible)
            {
                continue;
            }

            foreach (var card in section.Cards)
            {
                if (card.IsPending)
                {
                    rows.AddRange(card.Rows);
                }
            }
        }

        return rows;
    }

    private void Focus(MergeRowViewModel? row, bool announce = true)
    {
        if (ReferenceEquals(FocusedRow, row))
        {
            return;
        }

        if (FocusedRow is { } previous)
        {
            previous.IsFocused = false;
            if (_cardOfRow.TryGetValue(previous, out var previousCard))
            {
                previousCard.IsFocused = false;
            }
        }

        FocusedRow = row;
        if (row is null)
        {
            return;
        }

        row.IsFocused = true;
        if (_cardOfRow.TryGetValue(row, out var card))
        {
            card.IsFocused = true;
        }

        if (announce)
        {
            FocusRequested?.Invoke(row);
        }
    }

    // Leaves the cursor on the row that takes the answered card's place, so
    // the queue can be worked straight down without re-aiming.
    private void AdvanceFocusFrom(MergeCardViewModel card)
    {
        if (FocusedCard is null || !ReferenceEquals(FocusedCard, card))
        {
            return;
        }

        var before = VisibleRows();
        var index = FocusedRow is { } focused ? before.IndexOf(focused) : -1;
        var after = new List<MergeRowViewModel>(before.Count);
        foreach (var row in before)
        {
            if (!ReferenceEquals(_cardOfRow[row], card))
            {
                after.Add(row);
            }
        }

        if (after.Count == 0)
        {
            Focus(null);
            return;
        }

        var headRows = 0;
        for (var i = 0; i < index && i < before.Count; i++)
        {
            if (!ReferenceEquals(_cardOfRow[before[i]], card))
            {
                headRows++;
            }
        }

        Focus(after[Math.Clamp(headRows, 0, after.Count - 1)]);
    }

    // ── Covers ───────────────────────────────────────────────────────────────

    /// <summary>Sets the display resolution for cover decoding.</summary>
    public void RequestCovers(double displayWidthPixels)
    {
        if (displayWidthPixels <= 0)
        {
            return;
        }

        _coverWidthPixels = displayWidthPixels;
        foreach (var section in Sections)
        {
            foreach (var card in section.Cards)
            {
                card.RequestCovers(displayWidthPixels);
            }
        }
    }

    // ── Building same-game cards ─────────────────────────────────────────────

    private async Task<List<MergeCardViewModel>> BuildSameGameCardsAsync(
        IReadOnlyList<MergeCandidate> pending,
        LibrarySnapshot library,
        IdentityResolution resolution,
        DateTime now,
        CancellationToken ct)
    {
        var payloads = new Dictionary<long, SoftMatchSignalsPayload?>(pending.Count);
        var proposals = new List<MergeGroupProposal>(pending.Count);
        foreach (var candidate in pending)
        {
            var payload = MergeEdgeViewModel.Parse(candidate);
            payloads[candidate.Id] = payload;
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

        var cards = new List<MergeCardViewModel>(groups.Count);
        foreach (var group in groups)
        {
            // Primary first, then by the strength of each member's best edge:
            // the rows a user must think about are the weakest, and they end
            // the list rather than being scattered through it.
            var ordered = new List<MergeGroupMember>(group.Members);
            ordered.Sort((a, b) =>
            {
                if (a.WorkId == group.PrimaryWorkId)
                {
                    return -1;
                }

                if (b.WorkId == group.PrimaryWorkId)
                {
                    return 1;
                }

                var byScore = b.BestScore.CompareTo(a.BestScore);
                return byScore != 0 ? byScore : a.WorkId.CompareTo(b.WorkId);
            });

            var rows = new List<MergeRowViewModel>(ordered.Count);
            foreach (var member in ordered)
            {
                rows.Add(new MergeRowViewModel(
                    member.WorkId,
                    await DescribeWorkAsync(member.WorkId, member.ReleaseIds, library, ct),
                    member.ReleaseIds,
                    library.FactsOf(member.ReleaseIds),
                    now,
                    canPromote: true,
                    isPack: false));
            }

            MergeGroupEdge? strongest = null;
            foreach (var edge in group.Edges)
            {
                if (strongest is null || edge.Score > strongest.Score)
                {
                    strongest = edge;
                }
            }

            var payload = strongest is null ? null : payloads.GetValueOrDefault(strongest.CandidateId);
            var stores = DistinctStores(rows);

            // EXACT MATCH needs the titles to agree as the stores spell them,
            // not only after the matcher strips an edition: "The Witcher 3"
            // against its Game of the Year edition normalises to one title and
            // is still the user's call, which is what LIKELY says.
            var sameTitles = SameTitles(rows);
            var confidence = group.IsPriority && sameTitles && (payload?.TitleSimilarity ?? 0) >= ExactTitleFloor
                ? MergeConfidence.Exact
                : group.IsPriority
                    ? MergeConfidence.Likely
                    : MergeConfidence.WorthALook;

            cards.Add(new MergeCardViewModel(
                string.Create(CultureInfo.InvariantCulture, $"merge-card-{ordered.Min(m => m.WorkId)}"),
                stores.Count >= 2 ? MergeSectionKind.Stores : MergeSectionKind.Editions,
                confidence,
                group.Score,
                SameGameReason(payload, rows, stores, group, sameTitles),
                rows,
                headerIndex: 0,
                IdentityLinkKinds.SameGame,
                relationLabel: null,
                group.Edges,
                refusalPairs: [],
                standingActId: null));
        }

        return cards;
    }

    private static bool SameTitles(IReadOnlyList<MergeRowViewModel> rows)
    {
        for (var i = 1; i < rows.Count; i++)
        {
            if (!string.Equals(rows[i].Title.Trim(), rows[0].Title.Trim(), StringComparison.CurrentCultureIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static List<string> DistinctStores(IReadOnlyList<MergeRowViewModel> rows)
    {
        var stores = new List<string>();
        foreach (var row in rows)
        {
            foreach (var chip in row.StoreChips)
            {
                if (!stores.Contains(chip, StringComparer.OrdinalIgnoreCase))
                {
                    stores.Add(chip);
                }
            }
        }

        return stores;
    }

    // One sentence naming only what the match used. The numbers inside it
    // are the matcher's own; nothing is re-scored here.
    private static string SameGameReason(
        SoftMatchSignalsPayload? payload,
        IReadOnlyList<MergeRowViewModel> rows,
        IReadOnlyList<string> stores,
        MergeGroup group,
        bool sameTitles)
    {
        var sentences = new List<string>(3);

        if (payload is null)
        {
            sentences.Add(MergeCopy.ReasonNoBreakdown);
        }
        else
        {
            if (payload.TitleSimilarity >= ExactTitleFloor && !sameTitles)
            {
                sentences.Add(MergeCopy.ReasonSameTitleApartFromEdition);
            }
            else if (payload.TitleSimilarity >= ExactTitleFloor)
            {
                sentences.Add(stores.Count >= 2
                    ? string.Format(
                        CultureInfo.CurrentCulture,
                        MergeCopy.ReasonSameTitleOnFormat,
                        JoinStores(stores))
                    : MergeCopy.ReasonSameTitle);
            }
            else
            {
                sentences.Add(string.Format(
                    CultureInfo.CurrentCulture,
                    MergeCopy.ReasonNameMatchFormat,
                    payload.TitleSimilarity.ToString("0.00", CultureInfo.InvariantCulture)));
            }

            var clauses = new List<string>(2);
            switch (payload.PublisherMatch)
            {
                case true:
                    clauses.Add(MergeCopy.ReasonSamePublisher);
                    break;
                case false:
                    clauses.Add(MergeCopy.ReasonDifferentPublishers);
                    break;
            }

            if (payload.YearDelta is { } delta)
            {
                var gap = Math.Abs(delta);
                clauses.Add(gap switch
                {
                    0 => MergeCopy.ReasonSameYear,
                    1 => MergeCopy.ReasonOneYearApart,
                    _ => string.Format(
                        CultureInfo.CurrentCulture,
                        MergeCopy.ReasonYearsApartFormat,
                        gap.ToString(CultureInfo.InvariantCulture)),
                });
            }

            if (clauses.Count > 0)
            {
                var clause = string.Join(", ", clauses);
                sentences.Add(char.ToUpper(clause[0], CultureInfo.CurrentCulture) + clause[1..] + ".");
            }
        }

        // A row no proposal named with the header reached the group through
        // a sibling. Said in words, because it is the guard against Prey
        // (2006) and Prey (2017) arriving as one game.
        var titles = new Dictionary<long, string>(rows.Count);
        foreach (var row in rows)
        {
            titles[row.WorkId] = row.Title;
        }

        foreach (var row in rows)
        {
            if (row.WorkId == group.PrimaryWorkId)
            {
                continue;
            }

            MergeGroupEdge? best = null;
            var direct = false;
            foreach (var edge in group.Edges)
            {
                if (edge.Other(row.WorkId) is not { } neighbour)
                {
                    continue;
                }

                if (neighbour == group.PrimaryWorkId)
                {
                    direct = true;
                    break;
                }

                if (best is null || edge.Score > best.Score)
                {
                    best = edge;
                }
            }

            if (!direct && best?.Other(row.WorkId) is { } through
                && titles.TryGetValue(through, out var throughTitle))
            {
                sentences.Add(string.Format(
                    CultureInfo.CurrentCulture, MergeCopy.ReasonIndirectFormat, row.Title, throughTitle));
            }
        }

        return string.Join(" ", sentences);
    }

    private static string JoinStores(IReadOnlyList<string> stores)
    {
        if (stores.Count <= 1)
        {
            return stores.Count == 0 ? string.Empty : stores[0];
        }

        return string.Join(", ", stores.Take(stores.Count - 1)) + " and " + stores[^1];
    }

    // ── Building expansion cards ─────────────────────────────────────────────

    // A base game's packs are split by section before a card exists, so a
    // card never mixes expansion_of with variant_of and one link kind per
    // card holds.
    private async Task<List<MergeCardViewModel>> BuildExpansionCardsAsync(
        ExpansionScanReport scan, LibrarySnapshot library, DateTime now, CancellationToken ct)
    {
        var cards = new List<MergeCardViewModel>();
        foreach (var group in scan.Groups)
        {
            var bySection = new Dictionary<MergeSectionKind, List<ExpansionProposalMember>>();
            foreach (var member in group.Members)
            {
                var kind = SectionOf(member.Kind, member.RelationLabel);
                if (!bySection.TryGetValue(kind, out var list))
                {
                    bySection[kind] = list = [];
                }

                list.Add(member);
            }

            foreach (var (section, members) in bySection)
            {
                var rows = new List<MergeRowViewModel>(members.Count + 1)
                {
                    new(
                        group.Base.WorkId,
                        await DescribeWorkAsync(group.Base.WorkId, group.Base.ReleaseIds, library, ct),
                        group.Base.ReleaseIds,
                        library.FactsOf(group.Base.ReleaseIds),
                        now,
                        canPromote: false,
                        isPack: false),
                };

                var pairs = new List<ExpansionRefusalRequest>(members.Count);
                string? sharedLabel = null;
                var labelsAgree = true;
                var declared = 0;
                foreach (var member in members)
                {
                    rows.Add(new MergeRowViewModel(
                        member.Work.WorkId,
                        await DescribeWorkAsync(member.Work.WorkId, member.Work.ReleaseIds, library, ct),
                        member.Work.ReleaseIds,
                        library.FactsOf(member.Work.ReleaseIds),
                        now,
                        canPromote: false,
                        isPack: true));
                    pairs.Add(new ExpansionRefusalRequest(group.Base.WorkId, member.Work.WorkId));

                    if (member.FromMetadata)
                    {
                        declared++;
                    }

                    if (sharedLabel is null)
                    {
                        sharedLabel = member.RelationLabel;
                    }
                    else if (!string.Equals(sharedLabel, member.RelationLabel, StringComparison.OrdinalIgnoreCase))
                    {
                        labelsAgree = false;
                    }
                }

                var allDeclared = declared == members.Count;
                cards.Add(new MergeCardViewModel(
                    string.Create(CultureInfo.InvariantCulture, $"expansion-card-{section}-{group.Base.WorkId}"),
                    section,
                    allDeclared ? MergeConfidence.Exact : MergeConfidence.Likely,
                    allDeclared ? 1.0 : 0.5 + (0.5 * declared / members.Count),
                    ExpansionReason(group.Base.Title, members, declared),
                    rows,
                    headerIndex: 0,
                    section == MergeSectionKind.Tests ? IdentityLinkKinds.VariantOf : IdentityLinkKinds.ExpansionOf,
                    labelsAgree ? sharedLabel : null,
                    edges: [],
                    pairs,
                    standingActId: null));
            }
        }

        return cards;
    }

    private static MergeSectionKind SectionOf(string kind, string? relationLabel)
    {
        if (kind == IdentityLinkKinds.VariantOf)
        {
            return MergeSectionKind.Tests;
        }

        return relationLabel is not null
            && (string.Equals(relationLabel, RelationLabels.Episode, StringComparison.OrdinalIgnoreCase)
                || string.Equals(relationLabel, RelationLabels.Season, StringComparison.OrdinalIgnoreCase))
            ? MergeSectionKind.Parts
            : MergeSectionKind.Expansions;
    }

    private static string ExpansionReason(
        string baseTitle, IReadOnlyList<ExpansionProposalMember> members, int declared)
    {
        if (declared == members.Count)
        {
            return members.Count == 1
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    MergeCopy.ReasonDeclaredOneFormat,
                    members[0].Work.Title,
                    baseTitle)
                : string.Format(
                    CultureInfo.CurrentCulture,
                    MergeCopy.ReasonDeclaredManyFormat,
                    members.Count.ToString("N0", CultureInfo.CurrentCulture),
                    baseTitle);
        }

        var opener = members.Count == 1
            ? string.Format(
                CultureInfo.CurrentCulture,
                MergeCopy.ReasonSuffixFormat,
                members[0].Evidence.Suffix,
                baseTitle)
            : string.Format(
                CultureInfo.CurrentCulture,
                MergeCopy.ReasonSuffixManyFormat,
                members.Count.ToString("N0", CultureInfo.CurrentCulture),
                baseTitle);

        var evidence = members[0].Evidence;
        var clauses = new List<string>(2);
        switch (evidence.PublisherAgrees)
        {
            case true:
                clauses.Add(MergeCopy.ReasonSamePublisher);
                break;
            case false:
                clauses.Add(MergeCopy.ReasonDifferentPublishers);
                break;
        }

        if (members.Count == 1 && evidence.YearDelta is { } delta)
        {
            var gap = Math.Abs(delta);
            clauses.Add(gap switch
            {
                0 => MergeCopy.ReasonSameYear,
                1 => MergeCopy.ReasonOneYearApart,
                _ => string.Format(
                    CultureInfo.CurrentCulture,
                    MergeCopy.ReasonYearsApartFormat,
                    gap.ToString(CultureInfo.InvariantCulture)),
            });
        }

        if (clauses.Count == 0)
        {
            return opener;
        }

        var clause = string.Join(", ", clauses);
        return opener + " " + char.ToUpper(clause[0], CultureInfo.CurrentCulture) + clause[1..] + ".";
    }

    // ── Building strips from standing acts ───────────────────────────────────

    private sealed record StandingAct(
        IdentityAct Act, long ParentWorkId, IReadOnlyList<IdentityLink> Links)
    {
        public IEnumerable<long> WorkIds => Links.Select(link => link.ChildWorkId).Prepend(ParentWorkId);
    }

    // Grouped by act, because an act is the unit of undo. An act with no live
    // link left has been undone and is not a strip.
    private static List<StandingAct> StandingActs(
        IReadOnlyList<IdentityLink> history, IReadOnlyList<IdentityAct> acts)
    {
        var actById = new Dictionary<long, IdentityAct>(acts.Count);
        foreach (var act in acts)
        {
            actById[act.Id] = act;
        }

        var byAct = new Dictionary<long, List<IdentityLink>>();
        var order = new List<long>();
        foreach (var link in history)
        {
            if (!link.IsLive)
            {
                continue;
            }

            if (!byAct.TryGetValue(link.ActId, out var list))
            {
                byAct[link.ActId] = list = [];
                order.Add(link.ActId);
            }

            list.Add(link);
        }

        var standing = new List<StandingAct>(order.Count);
        foreach (var actId in order)
        {
            if (actById.TryGetValue(actId, out var act))
            {
                var links = byAct[actId];
                standing.Add(new StandingAct(act, links[0].ParentWorkId, links));
            }
        }

        // Newest first, so the most recent roll-up sits nearest the questions.
        standing.Reverse();
        return standing;
    }

    private async Task<List<MergeCardViewModel>> BuildStandingCardsAsync(
        IReadOnlyList<StandingAct> standing,
        IReadOnlyDictionary<long, IReadOnlyList<long>> releasesOfWork,
        LibrarySnapshot library,
        DateTime now,
        CancellationToken ct)
    {
        var cards = new List<MergeCardViewModel>(standing.Count);
        foreach (var act in standing)
        {
            var rows = new List<MergeRowViewModel>(act.Links.Count + 1);
            foreach (var workId in act.WorkIds)
            {
                var releaseIds = releasesOfWork.GetValueOrDefault(workId) ?? [];
                rows.Add(new MergeRowViewModel(
                    workId,
                    await DescribeWorkAsync(workId, releaseIds, library, ct),
                    releaseIds,
                    library.FactsOf(releaseIds),
                    now,
                    canPromote: false,
                    isPack: false));
            }

            if (rows.Count < 2)
            {
                continue;
            }

            var kind = act.Links[0].Kind;
            var label = act.Links[0].RelationLabel;
            MergeSectionKind section;
            if (kind == IdentityLinkKinds.SameGame)
            {
                section = DistinctStores(rows).Count >= 2 ? MergeSectionKind.Stores : MergeSectionKind.Editions;
            }
            else
            {
                section = SectionOf(kind, label);
            }

            cards.Add(new MergeCardViewModel(
                string.Create(CultureInfo.InvariantCulture, $"act-{act.Act.Id}"),
                section,
                MergeConfidence.Exact,
                score: 0,
                reason: string.Empty,
                rows,
                headerIndex: 0,
                kind,
                label,
                edges: [],
                refusalPairs: [],
                standingActId: act.Act.Id));
        }

        return cards;
    }

    // ── The library snapshot ─────────────────────────────────────────────────

    // The row's face comes from the WORK, because a row is a work and its
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
            work?.FirstReleaseYear,
            work?.Publisher,
            coverKey,
            _covers,
            stores);
    }

    /// <summary>What one load read about the releases the queue names.</summary>
    private sealed record LibrarySnapshot(
        Dictionary<long, string> Titles,
        Dictionary<long, CoverKey> CoverKeys,
        Dictionary<long, long> WorkOfRelease,
        Dictionary<long, SurvivorCandidate> Works,
        Dictionary<long, IReadOnlyList<string>> Stores,
        Dictionary<long, List<OwnershipBucket>> Played,
        Dictionary<long, List<Ownership>> Owned)
    {
        /// <summary>Folds the read model over a work's releases, the one permitted way.</summary>
        public MergeRowFacts FactsOf(IReadOnlyList<long> releaseIds)
        {
            var entries = new List<OwnershipBucket>();
            var unread = false;
            DateTime? acquired = null;
            var installed = false;

            foreach (var releaseId in releaseIds)
            {
                if (Played.TryGetValue(releaseId, out var played))
                {
                    entries.AddRange(played);
                    foreach (var entry in played)
                    {
                        unread |= entry.Bucket == LibraryBuckets.StaleButPatched;
                    }
                }

                if (Owned.TryGetValue(releaseId, out var owned))
                {
                    foreach (var ownership in owned)
                    {
                        installed |= ownership.Installed;
                        if (ownership.AcquiredAt is { } at && (acquired is null || at < acquired))
                        {
                            acquired = at;
                        }
                    }
                }
            }

            if (entries.Count == 0 && acquired is null)
            {
                return MergeRowFacts.None;
            }

            var playtime = CoveragePlaytime.Across(entries);
            return new MergeRowFacts(
                playtime.PlaytimeMinutes, playtime.LastPlayedAt, unread, acquired, installed);
        }
    }

    private async Task<LibrarySnapshot> DescribeAsync(
        IEnumerable<long> releaseIds, CancellationToken ct)
    {
        var titles = new Dictionary<long, string>();
        var coverKeys = new Dictionary<long, CoverKey>();
        var workOfRelease = new Dictionary<long, long>();
        var works = new Dictionary<long, SurvivorCandidate>();
        var stores = new Dictionary<long, IReadOnlyList<string>>();
        var owned = new Dictionary<long, List<Ownership>>();

        // The read model the grid draws from, so a row's hours, idle time and
        // unread dot agree with its tile. Read once per load, every entry,
        // because the queue names releases across the whole library.
        var played = new Dictionary<long, List<OwnershipBucket>>();
        var buckets = await _libraryQueries.GetOwnershipBucketsAsync(
            BucketThresholds.Default with { ShowNonGameEntries = true }, ct);
        foreach (var bucket in buckets)
        {
            if (!played.TryGetValue(bucket.ReleaseId, out var list))
            {
                played[bucket.ReleaseId] = list = [];
            }

            list.Add(bucket);
        }

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
            var ownerships = await _ownership.GetByReleaseAsync(releaseId, ct);
            if (ownerships.Count > 0)
            {
                stores[releaseId] = [.. ownerships.Select(o => o.Store)];
                owned[releaseId] = [.. ownerships];
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

        return new LibrarySnapshot(titles, coverKeys, workOfRelease, works, stores, played, owned);
    }

    // ── Undo bookkeeping ─────────────────────────────────────────────────────

    private enum UndoKind
    {
        Merge,

        Dismiss,
    }

    private sealed record LinkedAct(
        MergeCardViewModel Card,
        long ActId,
        IReadOnlyList<long> RejectedCandidateIds,
        IReadOnlyList<ExpansionRefusalRequest> RefusedPairs);

    private sealed record UndoRun(
        UndoKind Kind,
        List<LinkedAct> Linked,
        List<(MergeSectionViewModel Section, MergeCardViewModel Card, int Index)> Dismissed);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _dockTimer?.Dispose();
        _dockTimer = null;
    }
}
