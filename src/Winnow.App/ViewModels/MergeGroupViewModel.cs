using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Winnow.Core.Merging;

namespace Winnow.App.ViewModels;

/// <summary>
/// One card: a connected component of the pending proposals, never a single
/// pair. Answering a member cannot make a sibling card stale, because the
/// members were never separate cards.
///
/// <para>The card is a chooser, not a verdict. A radio per member picks the
/// title the library keeps, pre-selected by the ladder and labelled with the
/// rung that decided it. A checkbox per member picks who joins, so none, some
/// or all is one gesture and one act.</para>
///
/// <para>Two densities of one card. At two members it keeps the pair layout:
/// two covers at 200x300 with the full signal diff between them. At three or
/// more the primary keeps its cover at 200x300 and the rest become a roster,
/// each row carrying a chip, its evidence against the primary on one line, and
/// the matcher's own sentences behind a disclosure.</para>
/// </summary>
public partial class MergeGroupViewModel : ObservableObject
{
    private readonly IReadOnlyList<SurvivorCandidate> _candidates;
    private readonly IReadOnlyList<MergeGroupMemberViewModel> _ordered;
    private readonly long _ladderPrimaryWorkId;
    private readonly MergeSurvivorReason _ladderReason;

    // The radio writes IsPrimary, which calls back into SetPrimary. Applying a
    // choice writes IsPrimary on every member, so without this the first write
    // would re-enter and recurse.
    private bool _applying;

    public MergeGroupViewModel(
        MergeGroup group,
        IReadOnlyList<MergeGroupMemberViewModel> members,
        IReadOnlyList<MergeEdgeViewModel> edges)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(edges);

        Members = members;
        Edges = edges;
        Score = group.Score;
        IsPriority = group.IsPriority;
        IsPair = group.IsPair;

        _ladderPrimaryWorkId = group.PrimaryWorkId;
        _ladderReason = group.PrimaryReason;
        PrimaryReason = group.PrimaryReason;

        var candidates = new List<SurvivorCandidate>(members.Count);
        foreach (var member in members)
        {
            candidates.Add(new SurvivorCandidate { WorkId = member.WorkId });
        }

        _candidates = candidates;

        // Stable left/right for the pair layout: by work id, never by which
        // side is primary, so moving the radio recolours the card instead of
        // swapping the two covers under the pointer.
        var ordered = new List<MergeGroupMemberViewModel>(members);
        ordered.Sort(static (a, b) => a.WorkId.CompareTo(b.WorkId));
        _ordered = ordered;

        var labels = MergeMemberLabels.For([.. ordered.Select(member => member.Side)]);
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Label = labels[i];
        }

        Key = string.Create(CultureInfo.InvariantCulture, $"merge-group-{ordered[0].WorkId}");
        foreach (var member in ordered)
        {
            member.Attach(Key, SetPrimary);
        }

        Apply(group.PrimaryWorkId);
    }

    /// <summary>Scopes this card's primary radios, so one card's choice does not reach another.</summary>
    public string Key { get; }

    /// <summary>Members, primary first.</summary>
    public IReadOnlyList<MergeGroupMemberViewModel> Members { get; private set; }

    /// <summary>Every surviving edge inside this component, with its evidence.</summary>
    public IReadOnlyList<MergeEdgeViewModel> Edges { get; }

    /// <summary>Strongest edge in the component. The queue sorts on this.</summary>
    public double Score { get; }

    /// <summary>The matcher put the strongest edge in its top band.</summary>
    public bool IsPriority { get; }

    /// <summary>Two members keeps the pair layout; three or more takes the roster.</summary>
    public bool IsPair { get; }

    /// <summary>Confidence as the card prints it: Plex Mono, two decimals, tabular.</summary>
    public string ScoreText => Score.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>How many members are in the group, in the data face.</summary>
    public string MemberCountText =>
        Members.Count.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>The member whose title the library keeps.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(Others), nameof(PrimaryTitle), nameof(SameGameAutomationName))]
    public partial MergeGroupMemberViewModel Primary { get; private set; } = null!;

    /// <summary>Every member except the primary, by work id. This is the roster.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<MergeGroupMemberViewModel> Others { get; private set; } = [];

    /// <summary>Which rung decided the primary, or the user's own choice.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryReasonText), nameof(HasPrimaryReason))]
    public partial MergeSurvivorReason PrimaryReason { get; private set; }

    /// <summary>Keyboard and pointer selection: 2px Volt edge, matching the grid.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Latched the moment an answer is given, so a double click cannot write twice.</summary>
    [ObservableProperty]
    public partial bool IsDecided { get; set; }

    /// <summary>The title the library keeps.</summary>
    public string PrimaryTitle => Primary.Side.Title;

    /// <summary>The reason worded for display, or empty when there is nothing to say.</summary>
    public string PrimaryReasonText => PrimaryReason switch
    {
        MergeSurvivorReason.IgdbMatch => MergeCopy.SurvivorReasonIgdbMatch,
        MergeSurvivorReason.NamedByStore => MergeCopy.SurvivorReasonNamedByStore,
        MergeSurvivorReason.MostStoreEntries => MergeCopy.SurvivorReasonMostStoreEntries,
        MergeSurvivorReason.AddedFirst => MergeCopy.SurvivorReasonAddedFirst,
        MergeSurvivorReason.ChosenByYou => MergeCopy.SurvivorReasonChosenByYou,
        _ => string.Empty,
    };

    /// <summary>True when the card should show the reason line.</summary>
    public bool HasPrimaryReason => PrimaryReasonText.Length > 0;

    /// <summary>Small uppercase label beside the reason phrase.</summary>
    public string PrimaryReasonLabel => MergeCopy.SurvivorReasonLabel;

    /// <summary>Uppercase label before the matcher's confidence figure.</summary>
    public string ConfidenceLabel => MergeCopy.ConfidenceLabel;

    /// <summary>Uppercase label beside the member count.</summary>
    public string MemberCountLabel => MergeCopy.MemberCountLabel;

    /// <summary>The mark shown when the matcher put this group's strongest edge
    /// in its top confidence band. Several cards carry it at once; it says
    /// nothing about position.</summary>
    public string PriorityBandLabel => MergeCopy.PriorityBandLabel;

    /// <summary>Label on the affirmative answer (§7).</summary>
    public string SameGameButtonText => MergeCopy.SameGameButton;

    /// <summary>Label on the negative answer (§7).</summary>
    public string DifferentGamesButtonText => MergeCopy.DifferentGamesButton;

    /// <summary>Tooltip on Same game.</summary>
    public string SameGameTooltip => MergeCopy.SameGameTooltip;

    /// <summary>Tooltip on Different games.</summary>
    public string DifferentGamesTooltip => MergeCopy.DifferentGamesTooltip;

    /// <summary>The works that will be linked under the primary.</summary>
    public IReadOnlyList<long> IncludedChildWorkIds
    {
        get
        {
            var included = new List<long>();
            foreach (var member in Members)
            {
                if (!member.IsPrimary && member.IsIncluded)
                {
                    included.Add(member.WorkId);
                }
            }

            return included;
        }
    }

    /// <summary>True when at least one member joins, which is what makes the answer a link.</summary>
    public bool HasIncludedChildren => IncludedChildWorkIds.Count > 0;

    /// <summary>
    /// The proposals answered "different games" by the shape of the answer: an
    /// edge with exactly one end inside the link. Without this a rejection made
    /// inside a group evaporates and the next sweep re-proposes it. An edge with
    /// both ends outside is left pending, because the user said nothing about it.
    /// </summary>
    public IReadOnlyList<long> RejectedCandidateIds
    {
        get
        {
            var included = new HashSet<long>(IncludedChildWorkIds) { Primary.WorkId };

            var rejected = new List<long>();
            foreach (var edge in Edges)
            {
                if (included.Contains(edge.Edge.LeftWorkId)
                    != included.Contains(edge.Edge.RightWorkId))
                {
                    rejected.Add(edge.CandidateId);
                }
            }

            return rejected;
        }
    }

    /// <summary>Every proposal in the component, which is what Different games answers.</summary>
    public IReadOnlyList<long> AllCandidateIds
    {
        get
        {
            var ids = new List<long>(Edges.Count);
            foreach (var edge in Edges)
            {
                ids.Add(edge.CandidateId);
            }

            return ids;
        }
    }

    /// <summary>
    /// Names the group and its members rather than repeating the verb, so a
    /// column of Same game buttons is not one indistinguishable target. Two
    /// members with the same title are told apart by their entry numbers.
    /// </summary>
    public string SameGameAutomationName => string.Format(
        CultureInfo.CurrentCulture, MergeCopy.SameGameAutomationFormat, MemberList, PrimaryTitle);

    /// <summary>Same structure, without the primary.</summary>
    public string DifferentGamesAutomationName => string.Format(
        CultureInfo.CurrentCulture, MergeCopy.DifferentGamesAutomationFormat, MemberList);

    private string MemberList
    {
        get
        {
            var labels = new List<string>(Members.Count);
            foreach (var member in Members)
            {
                labels.Add(member.Label);
            }

            return string.Join(MergeCopy.MemberSeparator, labels);
        }
    }

    /// <summary>
    /// Moves the primary. A work that is not a member is refused rather than
    /// ignored, so a stale choice can never link in a direction nobody asked
    /// for. Making a member primary includes it, because the primary is the
    /// parent and a parent is always in its own group.
    /// </summary>
    public void SetPrimary(long workId)
    {
        if (_applying)
        {
            return;
        }

        // One home for the refusal: the same validation the grouper applies.
        _ = MergeGrouping.ChoosePrimary(_candidates, workId);

        if (Primary?.WorkId == workId)
        {
            return;
        }

        PrimaryReason = workId == _ladderPrimaryWorkId
            ? _ladderReason
            : MergeSurvivorReason.ChosenByYou;

        Apply(workId);
    }

    /// <summary>
    /// Fetches every member's cover at the size it is actually drawn at. A
    /// roster chip is a third the width of the primary's capsule and decodes at
    /// a third the resolution; nothing here decodes a 600x900 source.
    /// </summary>
    public void RequestCovers(double displayWidthPixels)
    {
        foreach (var member in Members)
        {
            member.RequestCover(
                displayWidthPixels * member.CoverWidth / MergeQueueViewModel.CoverWidth);
        }
    }

    // Re-points every member at the chosen primary: who is primary, who is
    // included, which edge is that member's evidence, and which sibling it
    // arrived through when no proposal named it and the primary together.
    private void Apply(long primaryWorkId)
    {
        _applying = true;
        try
        {
            ApplyCore(primaryWorkId);
        }
        finally
        {
            _applying = false;
        }
    }

    private void ApplyCore(long primaryWorkId)
    {
        MergeGroupMemberViewModel? primary = null;
        var others = new List<MergeGroupMemberViewModel>();
        var titles = new Dictionary<long, string>(Members.Count);

        foreach (var member in _ordered)
        {
            titles[member.WorkId] = member.Side.Title;
        }

        foreach (var member in _ordered)
        {
            member.IsPrimary = member.WorkId == primaryWorkId;
            if (member.IsPrimary)
            {
                member.IsIncluded = true;
                member.IsSoleChild = false;
                member.Evidence = null;
                member.ThroughTitle = null;
                primary = member;
                continue;
            }

            others.Add(member);

            MergeEdgeViewModel? direct = null;
            MergeEdgeViewModel? strongest = null;
            foreach (var edge in Edges)
            {
                if (edge.Edge.Other(member.WorkId) is not { } neighbour)
                {
                    continue;
                }

                if (neighbour == primaryWorkId)
                {
                    direct = edge;
                }

                if (strongest is null || edge.Score > strongest.Score)
                {
                    strongest = edge;
                }
            }

            member.Evidence = direct;
            member.ThroughTitle =
                direct is null
                && strongest?.Edge.Other(member.WorkId) is { } through
                && titles.TryGetValue(through, out var throughTitle)
                    ? throughTitle
                    : null;
        }

        // The two-member card draws its one child at 200x300 with the full diff
        // and no checkbox, so the child must arrive included: the two answer
        // buttons are the only include control it has.
        foreach (var member in others)
        {
            member.IsSoleChild = others.Count == 1;
            if (member.IsSoleChild)
            {
                member.IsIncluded = true;
            }
        }

        Primary = primary!;
        Others = others;

        var ordered = new List<MergeGroupMemberViewModel>(Members.Count) { primary! };
        ordered.AddRange(others);
        Members = ordered;
    }
}
