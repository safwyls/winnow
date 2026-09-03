using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Winnow.Core.Identity;
using Winnow.Core.Merging;

namespace Winnow.App.ViewModels;

/// <summary>
/// One proposal card. Pending, it is a header row the user can move, the
/// rows that nest under it, one reason sentence and two answers. Resolved, it
/// collapses to a strip that stays in place so the list does not reflow under
/// the pointer, and offers Separate again.
///
/// <para>One class for both relations. A same-game card carries the proposal
/// edges its answers write to; an expansion card carries the directional
/// pairs its answers refuse. The other list is empty. The link kind is one
/// per card because the queue splits a base game's packs by section before a
/// card exists, so a card never mixes <c>expansion_of</c> with
/// <c>variant_of</c>.</para>
///
/// <para>Who joins is the user's call, row by row. Same game links every row
/// still checked; a row left out has its proposals with the linked rows
/// recorded as answered no, so it neither comes back on the next sweep nor
/// takes the rest of the card down with it.</para>
/// </summary>
public partial class MergeCardViewModel : ObservableObject
{
    private readonly int _defaultHeaderIndex;
    private readonly IReadOnlyList<ExpansionRefusalRequest> _allPairs;

    // The radio writes IsHeader, which calls back into Promote. Applying a
    // choice writes IsHeader on every row, so without this the first write
    // would re-enter.
    private bool _applying;

    public MergeCardViewModel(
        string key,
        MergeSectionKind section,
        MergeConfidence confidence,
        double score,
        string reason,
        IReadOnlyList<MergeRowViewModel> rows,
        int headerIndex,
        string linkKind,
        string? relationLabel,
        IReadOnlyList<MergeGroupEdge> edges,
        IReadOnlyList<ExpansionRefusalRequest> refusalPairs,
        long? standingActId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(refusalPairs);

        if (rows.Count < 2)
        {
            throw new ArgumentException("A proposal names at least two entries.", nameof(rows));
        }

        if (headerIndex < 0 || headerIndex >= rows.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(headerIndex));
        }

        Key = key;
        Section = section;
        Confidence = confidence;
        Score = score;
        Reason = reason;
        Rows = rows;
        LinkKind = linkKind;
        RelationLabel = relationLabel;
        Edges = edges;
        _allPairs = refusalPairs;
        _defaultHeaderIndex = headerIndex;
        IsFromHistory = standingActId is not null;

        var labels = MergeMemberLabels.For([.. rows.Select(row => row.Side)]);
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].Label = labels[i];
            rows[i].Attach(key, Promote);
            rows[i].PropertyChanged += OnRowChanged;
        }

        UnreadCount = rows.Count(row => row.HasUnread);

        DateTime? earliest = null;
        foreach (var row in rows)
        {
            if (row.Facts.AcquiredAt is { } acquired && (earliest is null || acquired < earliest))
            {
                earliest = acquired;
            }
        }

        OwnedSinceYear = earliest?.Year;

        HeaderIndex = headerIndex;
        Apply();

        if (standingActId is { } actId)
        {
            MarkResolved(actId);
        }
    }

    /// <summary>Stable identity for the card within one load, and the scope of its header radios.</summary>
    public string Key { get; }

    /// <summary>Which section the card sits in.</summary>
    public MergeSectionKind Section { get; }

    /// <summary>Confidence as a word.</summary>
    public MergeConfidence Confidence { get; }

    /// <summary>The matcher's strongest score in the group, for tie-breaking the sort. Never shown.</summary>
    public double Score { get; }

    /// <summary>What the match used, in one sentence.</summary>
    public string Reason { get; }

    /// <summary>The rows, header first by default.</summary>
    public IReadOnlyList<MergeRowViewModel> Rows { get; }

    /// <summary>The kind every link this card writes carries.</summary>
    public string LinkKind { get; }

    /// <summary>The storefront's word for the relation, when every row shares one.</summary>
    public string? RelationLabel { get; }

    /// <summary>Every proposal inside the group. Empty on an expansion card.</summary>
    public IReadOnlyList<MergeGroupEdge> Edges { get; }

    /// <summary>Every <c>merge_candidates</c> row on the card, which is what Different games rejects.</summary>
    public IReadOnlyList<long> CandidateIds => [.. Edges.Select(edge => edge.CandidateId)];

    /// <summary>Every directional pair on the card, which is what Different games refuses. Empty on a same-game card.</summary>
    public IReadOnlyList<ExpansionRefusalRequest> RefusalPairs => _allPairs;

    /// <summary>
    /// True when the card was built from a link act that already stood when
    /// the screen loaded. Separate again on such a card reloads the queue,
    /// because the proposals it came from are not on the card.
    /// </summary>
    public bool IsFromHistory { get; }

    /// <summary>How many rows have been patched since they were last played.</summary>
    public int UnreadCount { get; }

    /// <summary>The earliest ownership year across the rows, or null when no store recorded one.</summary>
    public int? OwnedSinceYear { get; }

    /// <summary>Minutes across the rows still in, folded the one permitted way.</summary>
    public long TotalMinutes => CoveragePlaytime.Across(IncludedRows).PlaytimeMinutes;

    /// <summary>True when the confidence badge reads EXACT MATCH.</summary>
    public bool IsExact => Confidence == MergeConfidence.Exact;

    /// <summary>True when the confidence badge reads LIKELY.</summary>
    public bool IsLikely => Confidence == MergeConfidence.Likely;

    /// <summary>True when the confidence badge reads WORTH A LOOK.</summary>
    public bool IsWorthALook => Confidence == MergeConfidence.WorthALook;

    /// <summary>The badge's words.</summary>
    public string ConfidenceLabel => Confidence switch
    {
        MergeConfidence.Exact => MergeCopy.ConfidenceExact,
        MergeConfidence.Likely => MergeCopy.ConfidenceLikely,
        _ => MergeCopy.ConfidenceWorthALook,
    };

    /// <summary>True when any row is unread; the card inherits the dot.</summary>
    public bool HasUnread => UnreadCount > 0;

    /// <summary>Tooltip on the card's unread dot.</summary>
    public string UnreadTip => string.Format(
        CultureInfo.CurrentCulture, MergeCopy.CardUnreadTipFormat, UnreadCount);

    /// <summary>Summed playtime of the rows still in, in the data face; <c>0h</c> at zero.</summary>
    public string TotalPlaytimeText => TotalMinutes > 0
        ? GameTileViewModel.BuildPlaytimeText(TotalMinutes)
        : MergeCopy.ZeroHours;

    /// <summary>How many rows the card holds, in the data face.</summary>
    public string EntryCountText => Rows.Count.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>Index into <see cref="Rows"/> of the header.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(Header), nameof(HeaderTitle), nameof(ResolvedMeta),
        nameof(SameGameAutomationName), nameof(DifferentGamesAutomationName),
        nameof(SeparateAutomationName), nameof(SelectAutomationName))]
    public partial int HeaderIndex { get; private set; }

    /// <summary>True once the user has promoted a row on this card.</summary>
    [ObservableProperty]
    public partial bool IsTouched { get; private set; }

    /// <summary>The checkbox: this card joins Merge selected.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>True while the keyboard cursor is on one of this card's rows.</summary>
    [ObservableProperty]
    public partial bool IsFocused { get; set; }

    /// <summary>Latched the moment an answer is given, so a double click cannot write twice.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAnswer))]
    public partial bool IsDecided { get; set; }

    /// <summary>True once the card has become a resolved strip.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPending), nameof(CanAnswer))]
    public partial bool IsResolved { get; private set; }

    /// <summary>The link act the strip can be separated from, or null while pending.</summary>
    [ObservableProperty]
    public partial long? ActId { get; private set; }

    /// <summary>The row under the pointer, whose detail takes over the reason slot.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailText), nameof(IsDetailFromRow))]
    public partial MergeRowViewModel? HoveredRow { get; set; }

    /// <summary>True while the card is a question.</summary>
    public bool IsPending => !IsResolved;

    /// <summary>True while Same game would link something: pending, unlatched, at least one row in besides the header.</summary>
    public bool CanAnswer => IsPending && !IsDecided && ChildWorkIds.Count > 0;

    /// <summary>The row whose title the library keeps.</summary>
    public MergeRowViewModel Header => Rows[HeaderIndex];

    /// <summary>The header's title, drawn at the top of the card.</summary>
    public string HeaderTitle => Header.Title;

    /// <summary>The parent of every link this card writes.</summary>
    public long ParentWorkId => Header.WorkId;

    /// <summary>The header and every row still checked.</summary>
    public IReadOnlyList<MergeRowViewModel> IncludedRows =>
        [.. Rows.Where(row => row.IsHeader || row.IsIncluded)];

    /// <summary>Every row left out.</summary>
    public IReadOnlyList<MergeRowViewModel> ExcludedRows =>
        [.. Rows.Where(row => !row.IsHeader && !row.IsIncluded)];

    /// <summary>Every included row's work except the header's, ascending. The children of one act.</summary>
    public IReadOnlyList<long> ChildWorkIds
    {
        get
        {
            var children = new List<long>(Rows.Count - 1);
            foreach (var row in Rows)
            {
                if (!row.IsHeader && row.IsIncluded)
                {
                    children.Add(row.WorkId);
                }
            }

            children.Sort();
            return children;
        }
    }

    /// <summary>
    /// The proposals Same game answers no to by the shape of the answer: an
    /// edge with exactly one end inside the link. Recorded, so a row left
    /// out is an answer and not a card that comes back on the next sweep.
    /// An edge with both ends outside is left pending, because the user
    /// said nothing about it.
    /// </summary>
    public IReadOnlyList<long> RejectedCandidateIds
    {
        get
        {
            var inside = new HashSet<long>(ChildWorkIds) { ParentWorkId };
            var rejected = new List<long>();
            foreach (var edge in Edges)
            {
                if (inside.Contains(edge.LeftWorkId) != inside.Contains(edge.RightWorkId))
                {
                    rejected.Add(edge.CandidateId);
                }
            }

            return rejected;
        }
    }

    /// <summary>The pairs Same game refuses: every pack row left out. Empty on a same-game card.</summary>
    public IReadOnlyList<ExpansionRefusalRequest> RefusedPairs
    {
        get
        {
            var excluded = new HashSet<long>(ExcludedRows.Select(row => row.WorkId));
            return [.. _allPairs.Where(pair => excluded.Contains(pair.ChildWorkId))];
        }
    }

    /// <summary>The roll-up line: hours, entries, rows left out, ownership year, unread count.</summary>
    public string RollupText
    {
        get
        {
            var bits = new List<string>(5)
            {
                string.Format(CultureInfo.CurrentCulture, MergeCopy.RollupPlaytimeFormat, TotalPlaytimeText),
                string.Format(CultureInfo.CurrentCulture, MergeCopy.RollupEntriesFormat, EntryCountText),
            };

            var leftOut = ExcludedRows.Count;
            if (leftOut > 0)
            {
                bits.Add(string.Format(
                    CultureInfo.CurrentCulture,
                    MergeCopy.RollupLeftOutFormat,
                    leftOut.ToString("N0", CultureInfo.CurrentCulture)));
            }

            if (OwnedSinceYear is { } year)
            {
                bits.Add(string.Format(
                    CultureInfo.CurrentCulture,
                    MergeCopy.RollupOwnedSinceFormat,
                    year.ToString(CultureInfo.InvariantCulture)));
            }

            if (UnreadCount > 0)
            {
                bits.Add(string.Format(
                    CultureInfo.CurrentCulture,
                    UnreadCount == 1 ? MergeCopy.RollupUnreadOneFormat : MergeCopy.RollupUnreadManyFormat,
                    UnreadCount.ToString("N0", CultureInfo.CurrentCulture)));
            }

            return string.Join(MergeCopy.Separator, bits);
        }
    }

    /// <summary>The strip's meta line: the rows nested, their hours, and the promise.</summary>
    public string ResolvedMeta => string.Format(
        CultureInfo.CurrentCulture,
        MergeCopy.ResolvedMetaFormat,
        IncludedRows.Count.ToString("N0", CultureInfo.CurrentCulture),
        TotalPlaytimeText);

    /// <summary>The reason slot's text: the hovered row's detail, or the reason.</summary>
    public string DetailText => HoveredRow?.DetailText ?? Reason;

    /// <summary>True while the slot is showing a row rather than the reason, which brightens the ink.</summary>
    public bool IsDetailFromRow => HoveredRow is not null;

    /// <summary>Names the rows that join and the header, never the verb alone (§8).</summary>
    public string SameGameAutomationName => string.Format(
        CultureInfo.CurrentCulture,
        MergeCopy.SameGameAutomationFormat,
        string.Join(MergeCopy.MemberSeparator, IncludedRows.Select(row => row.Label)),
        HeaderTitle);

    /// <summary>Names the rows the answer is about (§8).</summary>
    public string DifferentGamesAutomationName => string.Format(
        CultureInfo.CurrentCulture,
        MergeCopy.DifferentGamesAutomationFormat,
        string.Join(MergeCopy.MemberSeparator, Rows.Select(row => row.Label)));

    /// <summary>Names the strip's header (§8).</summary>
    public string SeparateAutomationName => string.Format(
        CultureInfo.CurrentCulture, MergeCopy.SeparateAutomationFormat, HeaderTitle);

    /// <summary>Names the group the checkbox selects (§8).</summary>
    public string SelectAutomationName => string.Format(
        CultureInfo.CurrentCulture, MergeCopy.SelectAutomationFormat, HeaderTitle);

    /// <summary>
    /// Makes <paramref name="row"/> the header. Refused rather than ignored for
    /// a row that is not on the card, so a stale reference can never link in a
    /// direction nobody asked for. A no-op on a row that cannot be promoted.
    /// Making a row the header brings it in, because the parent is always in
    /// its own group.
    /// </summary>
    public void Promote(MergeRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var index = -1;
        for (var i = 0; i < Rows.Count; i++)
        {
            if (ReferenceEquals(Rows[i], row))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            throw new ArgumentException("The row is not on this card.", nameof(row));
        }

        if (_applying || !row.CanPromote || IsResolved || index == HeaderIndex)
        {
            return;
        }

        HeaderIndex = index;
        IsTouched = true;
        row.IsIncluded = true;
        Apply();
    }

    /// <summary>Turns the card into a resolved strip for <paramref name="actId"/>.</summary>
    public void MarkResolved(long actId)
    {
        ActId = actId;
        IsResolved = true;
        IsSelected = false;
        HoveredRow = null;
    }

    /// <summary>Returns the strip to a pending card, the header and the checks where the user left them.</summary>
    public void MarkPending()
    {
        ActId = null;
        IsResolved = false;
        IsDecided = false;
    }

    /// <summary>Fetches every row's cover at the size the thumbnail draws it.</summary>
    public void RequestCovers(double displayWidthPixels)
    {
        foreach (var row in Rows)
        {
            row.RequestCover(displayWidthPixels);
        }
    }

    /// <summary>Puts the header back where the ladder put it. Used by tests.</summary>
    internal void ResetHeader()
    {
        HeaderIndex = _defaultHeaderIndex;
        IsTouched = false;
        Apply();
    }

    private void Apply()
    {
        _applying = true;
        try
        {
            for (var i = 0; i < Rows.Count; i++)
            {
                Rows[i].IsHeader = i == HeaderIndex;
            }
        }
        finally
        {
            _applying = false;
        }

        AnnounceMembership();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MergeRowViewModel.IsIncluded))
        {
            AnnounceMembership();
        }
    }

    private void AnnounceMembership()
    {
        OnPropertyChanged(nameof(ChildWorkIds));
        OnPropertyChanged(nameof(IncludedRows));
        OnPropertyChanged(nameof(ExcludedRows));
        OnPropertyChanged(nameof(RejectedCandidateIds));
        OnPropertyChanged(nameof(RefusedPairs));
        OnPropertyChanged(nameof(TotalMinutes));
        OnPropertyChanged(nameof(TotalPlaytimeText));
        OnPropertyChanged(nameof(RollupText));
        OnPropertyChanged(nameof(ResolvedMeta));
        OnPropertyChanged(nameof(CanAnswer));
        OnPropertyChanged(nameof(SameGameAutomationName));
    }
}
