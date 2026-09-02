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

    private Action<long>? _makePrimary;

    public MergeGroupMemberViewModel(
        long workId,
        MergeSideViewModel side,
        IReadOnlyList<long> releaseIds,
        bool isDefaultIncluded)
    {
        WorkId = workId;
        Side = side;
        ReleaseIds = releaseIds;
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

    /// <summary>What the grouper decided before the user touched anything.</summary>
    public bool IsDefaultIncluded { get; }

    /// <summary>
    /// True when this member is the one whose title the library keeps. The
    /// primary is always included, so its checkbox is not offered.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(ShowIncludeControl), nameof(PrimaryAutomationName),
        nameof(CoverWidth), nameof(CoverHeight))]
    public partial bool IsPrimary { get; set; }

    /// <summary>
    /// True when this member is the group's only non-primary member, the
    /// two-member case. The row draws its cover at 200x300 with the full
    /// open diff and no include checkbox, because the two answer buttons
    /// already carry include and exclude. Set by the group whenever the
    /// primary moves.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(ShowIncludeControl), nameof(ShowFullEvidence), nameof(ShowCondensedEvidence),
        nameof(ShowEvidenceDisclosure), nameof(ShowNoEvidenceNote),
        nameof(CoverWidth), nameof(CoverHeight),
        nameof(PlaceholderFontSize), nameof(PlaceholderLineHeight))]
    public partial bool IsSoleChild { get; set; }

    /// <summary>True when this member joins the link.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IncludeAutomationName))]
    public partial bool IsIncluded { get; set; }

    /// <summary>
    /// The evidence between this member and the current primary, or null when
    /// no proposal ever named the two together.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(HasDirectEvidence), nameof(HasSignals), nameof(ShowFullEvidence),
        nameof(ShowCondensedEvidence), nameof(ShowEvidenceDisclosure),
        nameof(ShowNoEvidenceNote))]
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

    /// <summary>
    /// The checkbox is offered on every member except the primary and except
    /// the sole child of a two-member group, where the two answer buttons
    /// already carry include and exclude.
    /// </summary>
    public bool ShowIncludeControl => !IsPrimary && !IsSoleChild;

    /// <summary>The cover width this member draws at. The primary and the sole
    /// child of a two-member group keep the 200px capsule (§6). Everything
    /// else is a 64px chip in a roster.</summary>
    public double CoverWidth =>
        IsPrimary || IsSoleChild ? MergeQueueViewModel.CoverWidth : ChipWidth;

    /// <summary>Cover height, 2:3 portrait matching the grid's capsule geometry.</summary>
    public double CoverHeight => CoverWidth * 1.5;

    /// <summary>Placeholder title font size, scaled to the cover it sits on
    /// (§7: Bricolage on a Surface field, never a spinner).</summary>
    public double PlaceholderFontSize => IsSoleChild ? 22 : 10;

    /// <summary>Placeholder title line height, matching the font size above.</summary>
    public double PlaceholderLineHeight => IsSoleChild ? 24 : 11;

    /// <summary>True when the four-row signal diff is drawn open. Only the sole
    /// child of a two-member group has room for it.</summary>
    public bool ShowFullEvidence => IsSoleChild && HasSignals;

    /// <summary>True when the evidence condenses to one line, which is what a
    /// roster of three or more can afford.</summary>
    public bool ShowCondensedEvidence => !IsSoleChild && HasDirectEvidence;

    /// <summary>True when the disclosure toggle for the matcher's own sentences
    /// is drawn. Not shown for the sole child, whose diff is already open.</summary>
    public bool ShowEvidenceDisclosure => !IsSoleChild && HasSignals;

    /// <summary>True when a direct proposal carried no recorded breakdown. The
    /// sole child says so in words rather than drawing an empty diff.</summary>
    public bool ShowNoEvidenceNote => IsSoleChild && HasDirectEvidence && !HasSignals;

    /// <summary>Badge text for each store, forwarded from the side. Bound by the chip row at both densities.</summary>
    public IReadOnlyList<string> StoreChips => Side.StoreChips;

    /// <summary>Display names comma-joined, for the chip row's tooltip and the automation name.</summary>
    public string StoreNames => Side.StoreNames;

    /// <summary>False when no ownership row named a store; the chip row is hidden and the automation name uses the store-less format.</summary>
    public bool HasStores => Side.HasStores;

    /// <summary>
    /// What a screen reader calls this member. Built by
    /// <see cref="MergeMemberLabels"/> from the facts already on the row —
    /// title, stores, year, publisher — adding qualifiers only while two
    /// members would otherwise share a label. No database ids (§10.5).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryAutomationName), nameof(IncludeAutomationName))]
    public partial string Label { get; set; } = string.Empty;

    /// <summary>Label beside this member's primary radio.</summary>
    public string PrimaryControlText => MergeCopy.PrimaryControlLabel;

    /// <summary>Uppercase label before the title distance.</summary>
    public string TitleDistanceLabel => MergeCopy.TitleDistanceLabel;

    /// <summary>Uppercase label before the year delta.</summary>
    public string YearDeltaLabel => MergeCopy.YearDeltaLabel;

    /// <summary>Uppercase label before the publisher verdict.</summary>
    public string PublisherMatchLabel => MergeCopy.PublisherMatchLabel;

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
