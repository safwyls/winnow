using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Winnow.App.Services;
using Winnow.Core.Identity;

namespace Winnow.App.ViewModels;

/// <summary>
/// One candidate row on a proposal card: a WORK and everything the library
/// knows about it. A row is a work rather than a store entry because the link
/// an answer writes joins works, so a work already carrying two store entries
/// is one row wearing two badges, not two rows one of which could be promoted
/// over the other to no effect.
///
/// <para>Two controls hang off it. The radio makes the row the header, the
/// title the library keeps. The checkbox decides whether the row joins the
/// roll-up at all, so a group that arrived with one wrong member can be
/// answered without refusing the rest. Clicking the row itself opens the
/// game's details, so the entries can be compared before answering.</para>
/// </summary>
public partial class MergeRowViewModel : ObservableObject, IPlayedEntry
{
    private Action<MergeRowViewModel>? _makeHeader;

    public MergeRowViewModel(
        long workId,
        MergeSideViewModel side,
        IReadOnlyList<long> releaseIds,
        MergeRowFacts facts,
        DateTime nowUtc,
        bool canPromote,
        bool isPack)
    {
        ArgumentNullException.ThrowIfNull(side);
        ArgumentNullException.ThrowIfNull(releaseIds);
        ArgumentNullException.ThrowIfNull(facts);

        WorkId = workId;
        Side = side;
        ReleaseIds = releaseIds;
        Facts = facts;
        CanPromote = canPromote;
        IsPack = isPack;
        DormancyAlpha = Dormancy.VividAlphaFor(facts.LastPlayedAt, nowUtc);
        PlaytimeText = BuildPlaytimeText(facts.PlaytimeMinutes, isPack);
        IdleText = BuildIdleText(facts, nowUtc, isPack);
        DetailText = BuildDetailText(side, facts, isPack);
    }

    /// <summary>The work this row is. The child, or the parent, of the link.</summary>
    public long WorkId { get; }

    /// <summary>Title, cover and stores.</summary>
    public MergeSideViewModel Side { get; }

    /// <summary>Every store entry under the work that the queue asked about.</summary>
    public IReadOnlyList<long> ReleaseIds { get; }

    /// <summary>What the read model said about the work.</summary>
    public MergeRowFacts Facts { get; }

    /// <summary>
    /// False on an expansion card, where the base is the header by the shape
    /// of the relation and a radio on a pack would let someone assert that
    /// Stellaris is an expansion of Utopia. The radio is not drawn.
    /// </summary>
    public bool CanPromote { get; }

    /// <summary>
    /// True for a proposed expansion, whose zero playtime is an absence rather
    /// than a fact: a pack records no hours of its own, so the column shows an
    /// em dash instead of a zero that would read as never opened.
    /// </summary>
    public bool IsPack { get; }

    /// <summary>Scopes the header radios to one card, so a choice on one card does not unset another's.</summary>
    public string GroupName { get; private set; } = string.Empty;

    /// <summary>The title the row shows.</summary>
    public string Title => Side.Title;

    /// <summary>Store badges, uppercase, one per store the work is owned on.</summary>
    public IReadOnlyList<string> StoreChips => Side.StoreChips;

    /// <summary>Store names, comma-joined, for a screen reader.</summary>
    public string StoreNames => Side.StoreNames;

    /// <summary>False when no ownership row named a store.</summary>
    public bool HasStores => Side.HasStores;

    /// <inheritdoc />
    public long PlaytimeMinutes => Facts.PlaytimeMinutes;

    /// <inheritdoc />
    public DateTime? LastPlayedAt => Facts.LastPlayedAt;

    /// <summary>True when the row has been patched since it was last played.</summary>
    public bool HasUnread => Facts.HasUnread;

    /// <summary>Playtime in the data face: <c>312h</c>, <c>45m</c>, <c>0h</c>, or an em dash for a pack.</summary>
    public string PlaytimeText { get; }

    /// <summary>Idle time in the data face: <c>8mo</c>, <c>3y</c>, <c>never</c>, or an em dash for a pack.</summary>
    public string IdleText { get; }

    /// <summary>The row's own facts, joined by the app's one separator, shown in the reason slot while the row is hovered.</summary>
    public string DetailText { get; }

    /// <summary>The dormancy ramp's vivid alpha for the cover, from the row's last-played.</summary>
    public double DormancyAlpha { get; }

    /// <summary>Tooltip on the unread dot.</summary>
    public string UnreadTip => MergeCopy.RowUnreadTip;

    /// <summary>Tooltip on the row: a click opens the game's details.</summary>
    public string DetailsTip => MergeCopy.DetailsTip;

    /// <summary>Tooltip on the header radio.</summary>
    public string PromoteTip => MergeCopy.PromoteTip;

    /// <summary>Tooltip on the include checkbox, by its state.</summary>
    public string IncludeTip => IsIncluded ? MergeCopy.LeaveOutTip : MergeCopy.IncludeTip;

    /// <summary>
    /// True on the row whose title the library will keep. Written by the
    /// radio, so the card hears the choice through <see cref="Attach"/>
    /// rather than through a command routed up the visual tree.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(HeaderMark), nameof(CanExclude), nameof(PromoteAutomationName), nameof(IncludeAutomationName))]
    public partial bool IsHeader { get; set; }

    /// <summary>
    /// True while the row joins the roll-up. The header is always in; every
    /// other row can be left out, which records its proposals as answered no
    /// without refusing the rest of the card.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderMark), nameof(IncludeTip), nameof(IncludeAutomationName))]
    public partial bool IsIncluded { get; set; } = true;

    /// <summary>True while the pointer is over the row.</summary>
    [ObservableProperty]
    public partial bool IsHovered { get; set; }

    /// <summary>True while the keyboard cursor is on the row.</summary>
    [ObservableProperty]
    public partial bool IsFocused { get; set; }

    /// <summary>
    /// What a screen reader calls this row. Assigned by the card through
    /// <see cref="MergeMemberLabels"/>, which adds stores, year and publisher
    /// only while two rows would otherwise share a label.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PromoteAutomationName), nameof(IncludeAutomationName), nameof(DetailsAutomationName))]
    public partial string Label { get; set; } = string.Empty;

    /// <summary>The checkbox is offered on every row but the header, which is always in.</summary>
    public bool CanExclude => !IsHeader;

    /// <summary>The one status word beside the title: HEADER, NESTS UNDER or LEFT OUT.</summary>
    public string HeaderMark => IsHeader
        ? MergeCopy.HeaderMark
        : IsIncluded
            ? MergeCopy.NestsUnderMark
            : MergeCopy.LeftOutMark;

    /// <summary>Names the row on its radio, never the verb alone.</summary>
    public string PromoteAutomationName => IsHeader
        ? string.Format(CultureInfo.CurrentCulture, MergeCopy.HeaderRowAutomationFormat, Label)
        : string.Format(CultureInfo.CurrentCulture, MergeCopy.PromoteAutomationFormat, Label);

    /// <summary>Names the row on its include checkbox, never the verb alone.</summary>
    public string IncludeAutomationName => string.Format(
        CultureInfo.CurrentCulture, MergeCopy.IncludeAutomationFormat, Label);

    /// <summary>Names the row on the row itself, whose click opens details.</summary>
    public string DetailsAutomationName => string.Format(
        CultureInfo.CurrentCulture, MergeCopy.DetailsAutomationFormat, Label);

    /// <summary>Fetches the row's cover at the size the thumbnail draws it.</summary>
    public void RequestCover(double displayWidthPixels) => Side.RequestCover(displayWidthPixels);

    /// <summary>Called by the card once, to give the row its radio scope and its callback.</summary>
    internal void Attach(string groupName, Action<MergeRowViewModel> makeHeader)
    {
        GroupName = groupName;
        _makeHeader = makeHeader;
        OnPropertyChanged(nameof(GroupName));
    }

    // The radio writes IsHeader directly, so the card hears the choice here.
    // The card refuses the call while it is applying a choice of its own.
    partial void OnIsHeaderChanged(bool value)
    {
        if (value)
        {
            _makeHeader?.Invoke(this);
        }
    }

    private static string BuildPlaytimeText(long minutes, bool isPack)
    {
        if (minutes > 0)
        {
            return GameTileViewModel.BuildPlaytimeText(minutes);
        }

        return isPack ? MergeCopy.NoValue : MergeCopy.ZeroHours;
    }

    private static string BuildIdleText(MergeRowFacts facts, DateTime nowUtc, bool isPack)
    {
        if (facts.LastPlayedAt is { } played)
        {
            return GameTileViewModel.IdleSpanText(nowUtc - played);
        }

        return isPack && facts.PlaytimeMinutes <= 0 ? MergeCopy.NoValue : MergeCopy.NeverIdle;
    }

    private static string BuildDetailText(MergeSideViewModel side, MergeRowFacts facts, bool isPack)
    {
        var parts = new List<string>(6);

        if (side.HasStores)
        {
            parts.Add(string.Join(MergeCopy.StoreJoiner, side.StoreChips));
        }

        if (isPack && facts.PlaytimeMinutes <= 0)
        {
            parts.Add(MergeCopy.DetailPackNoPlaytime);
        }
        else if (facts.PlaytimeMinutes <= 0)
        {
            parts.Add(MergeCopy.DetailNeverOpened);
        }
        else if (facts.AcquiredAt is { } since)
        {
            parts.Add(string.Format(
                CultureInfo.CurrentCulture,
                MergeCopy.DetailPlaytimeSinceFormat,
                GameTileViewModel.BuildPlaytimeText(facts.PlaytimeMinutes),
                since.Year.ToString(CultureInfo.InvariantCulture)));
        }
        else
        {
            parts.Add(GameTileViewModel.BuildPlaytimeText(facts.PlaytimeMinutes));
        }

        if (facts.LastPlayedAt is { } lastPlayed)
        {
            parts.Add(string.Format(
                CultureInfo.CurrentCulture,
                MergeCopy.DetailLastPlayedFormat,
                UpdateEventViewModel.LocalDateText(lastPlayed)));
        }
        else if (facts.AcquiredAt is { } added)
        {
            parts.Add(string.Format(
                CultureInfo.CurrentCulture,
                MergeCopy.DetailAddedFormat,
                UpdateEventViewModel.LocalDateText(added)));
        }

        switch (facts.Installed)
        {
            case true:
                parts.Add(MergeCopy.DetailInstalled);
                break;
            case false:
                parts.Add(MergeCopy.DetailNotInstalled);
                break;
        }

        if (facts.HasUnread)
        {
            parts.Add(MergeCopy.RowUnreadTip);
        }

        return string.Join(MergeCopy.Separator, parts);
    }
}
