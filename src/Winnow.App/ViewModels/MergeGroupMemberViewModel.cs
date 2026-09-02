using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Winnow.App.ViewModels;

/// <summary>
/// One member of a group: a work and the store entries the queue asked about
/// under it. A member is a WORK, not a release, so two proposals naming the
/// same work are one row rather than two, and a work already carrying several
/// entries reads as one game.
///
/// <para>Two controls hang off it. The radio chooses which title the library
/// keeps. The checkbox chooses whether this member joins at all, which is how
/// none, some or all is one gesture rather than N questions.</para>
/// </summary>
public partial class MergeGroupMemberViewModel : ObservableObject
{
    /// <summary>Cover geometry for a roster row: the same 2:3 portrait at a third the width.</summary>
    public const double ChipWidth = 64;

    /// <summary>2:3 portrait, matching the pair card's capsule geometry.</summary>
    public const double ChipHeight = ChipWidth * 1.5;

    private Action<long>? _makePrimary;

    public MergeGroupMemberViewModel(
        long workId,
        MergeSideViewModel side,
        IReadOnlyList<long> releaseIds,
        double bestScore,
        bool isDefaultIncluded)
    {
        WorkId = workId;
        Side = side;
        ReleaseIds = releaseIds;
        BestScore = bestScore;
        IsIncluded = isDefaultIncluded;
        IsDefaultIncluded = isDefaultIncluded;
    }

    /// <summary>The work this member is.</summary>
    public long WorkId { get; }

    /// <summary>
    /// Scopes the primary radios to one card, so choosing a title on one group
    /// does not unselect the choice on another.
    /// </summary>
    public string GroupName { get; private set; } = string.Empty;

    /// <summary>The cover and the facts that tell this member from its siblings.</summary>
    public MergeSideViewModel Side { get; }

    /// <summary>Store entries under this work that the queue asked about, ascending.</summary>
    public IReadOnlyList<long> ReleaseIds { get; }

    /// <summary>Strongest edge this member has to any other member of its group.</summary>
    public double BestScore { get; }

    /// <summary>What the grouper decided before the user touched anything.</summary>
    public bool IsDefaultIncluded { get; }

    /// <summary>
    /// True when this member is the one whose title the library keeps. The
    /// primary is always included, so its checkbox is not offered.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowIncludeControl), nameof(PrimaryAutomationName))]
    public partial bool IsPrimary { get; set; }

    /// <summary>True when this member joins the link.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IncludeAutomationName))]
    public partial bool IsIncluded { get; set; }

    /// <summary>
    /// The evidence between this member and the current primary, or null when
    /// no proposal ever named the two together.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDirectEvidence), nameof(HasSignals))]
    public partial MergeEdgeViewModel? Evidence { get; set; }

    /// <summary>
    /// The sibling this member reaches the group through, when it has no direct
    /// edge to the primary. Named so transitive membership is visible rather
    /// than implied, which is the guard against Prey (2006) and Prey (2017)
    /// arriving as one game.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIndirect), nameof(IndirectText))]
    public partial string? ThroughTitle { get; set; }

    /// <summary>The matcher's own sentences, open or shut.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EvidenceToggleText))]
    public partial bool IsEvidenceOpen { get; set; }

    /// <summary>True when a proposal named this member and the primary directly.</summary>
    public bool HasDirectEvidence => Evidence is not null;

    /// <summary>True when the direct edge carried a recorded breakdown.</summary>
    public bool HasSignals => Evidence?.HasSignals ?? false;

    /// <summary>True when this member reaches the group only through a sibling.</summary>
    public bool IsIndirect => ThroughTitle is { Length: > 0 };

    /// <summary>Names the sibling this member arrived through.</summary>
    public string IndirectText => ThroughTitle is { Length: > 0 } through
        ? string.Format(CultureInfo.CurrentCulture, MergeCopy.MemberThroughFormat, through)
        : string.Empty;

    /// <summary>The checkbox is offered on every member except the primary.</summary>
    public bool ShowIncludeControl => !IsPrimary;

    /// <summary>Strongest edge score, in the data face.</summary>
    public string BestScoreText => BestScore.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>The store entries this member covers, in the data face.</summary>
    public string ReleasesText => Side.ReleaseText;

    /// <summary>Badge text for each store, forwarded from the side. Bound by the chip row at both densities.</summary>
    public IReadOnlyList<string> StoreChips => Side.StoreChips;

    /// <summary>Display names comma-joined, for the chip row's tooltip and the automation name.</summary>
    public string StoreNames => Side.StoreNames;

    /// <summary>False when no ownership row named a store; the chip row is hidden and the automation name uses the store-less format.</summary>
    public bool HasStores => Side.HasStores;

    /// <summary>The title with its store and its entry numbers, which is what tells two members with one title apart.</summary>
    public string Label => HasStores
        ? string.Format(
            CultureInfo.CurrentCulture,
            MergeCopy.MemberWithStoreAutomationFormat,
            Side.Title,
            StoreNames,
            ReleasesText)
        : string.Format(
            CultureInfo.CurrentCulture, MergeCopy.MemberAutomationFormat, Side.Title, ReleasesText);

    /// <summary>Label beside this member's primary radio.</summary>
    public string PrimaryControlText => MergeCopy.PrimaryControlLabel;

    /// <summary>Label beside this member's include checkbox.</summary>
    public string IncludeControlText => MergeCopy.IncludeControlLabel;

    /// <summary>Toggle label for the matcher's own sentences.</summary>
    public string EvidenceToggleText =>
        IsEvidenceOpen ? MergeCopy.EvidenceHide : MergeCopy.EvidenceShow;

    /// <summary>Names the member, never the verb, so a column of radios is not one target.</summary>
    public string PrimaryAutomationName => string.Format(
        CultureInfo.CurrentCulture, MergeCopy.PrimaryAutomationFormat, Label);

    /// <summary>Names the member, never the verb, so a column of checkboxes is not one target.</summary>
    public string IncludeAutomationName => string.Format(
        CultureInfo.CurrentCulture, MergeCopy.IncludeAutomationFormat, Label);

    /// <summary>Opens or shuts the matcher's own sentences for this member.</summary>
    [RelayCommand]
    private void ToggleEvidence() => IsEvidenceOpen = !IsEvidenceOpen;

    /// <summary>Fetches the member's cover at display resolution, off-thread.</summary>
    public void RequestCover(double displayWidthPixels) => Side.RequestCover(displayWidthPixels);

    /// <summary>Called by the group once, to give the member its radio scope and its callback.</summary>
    internal void Attach(string groupName, Action<long> makePrimary)
    {
        GroupName = groupName;
        _makePrimary = makePrimary;
        OnPropertyChanged(nameof(GroupName));
    }

    // The radio writes IsPrimary directly, so the group hears the choice here
    // rather than through a command the markup has to route up the visual tree.
    // The group guards against re-entry while it is applying a choice of its own.
    partial void OnIsPrimaryChanged(bool value)
    {
        if (value)
        {
            _makePrimary?.Invoke(WorkId);
        }
    }
}
