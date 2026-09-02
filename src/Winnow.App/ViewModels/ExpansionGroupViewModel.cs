using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Winnow.Core.Identity;

namespace Winnow.App.ViewModels;

/// <summary>
/// One base game and the expansions proposed under it: one card, answered as
/// one act.
///
/// <para>The TASK-70.3 same-game card was NOT reused, and the reason is
/// structural rather than aesthetic. A same-game group is N peers of
/// one game, and its KEEP radio asks a real question: either title could be
/// the one the library shows. An expansion group is one base plus N
/// extensions, and the parent is fixed by the relation — the base is the title
/// the others extend — so the same control here would let someone assert that
/// Civilization IV is an expansion of Beyond the Sword. The evidence differs
/// too: the matcher's four-signal diff scores DISTANCE between two titles,
/// which is exactly why it can never find these pairs, and what this card
/// shows instead is the extension itself.</para>
///
/// <para>What IS reused is the grammar: the same
/// <see cref="MergeSideViewModel"/> faces, the 200×300 capsule for the base
/// beside 64×96 roster chips, include checkboxes for none/some/all, one act,
/// one retraction.</para>
///
/// <para>Grouping is PRESENTATION ONLY. No count, no playtime, no bucket and
/// no recommendation moves, and the card says so rather than letting the user
/// infer it.</para>
/// </summary>
public partial class ExpansionGroupViewModel : ObservableObject
{
    /// <summary>Base capsule geometry, matching the merge card's.</summary>
    public const double CoverWidth = MergeQueueViewModel.CoverWidth;

    /// <summary>2:3 portrait, the same capsule geometry the grid uses.</summary>
    public const double CoverHeight = MergeQueueViewModel.CoverHeight;

    /// <summary>Creates a card. A card with no proposed expansion is not a question, so it is refused.</summary>
    /// <param name="baseWorkId">The work the members would be grouped under.</param>
    /// <param name="baseSide">The base game's face, drawn as the 200×300 capsule.</param>
    /// <param name="members">The proposed expansions, at least one.</param>
    public ExpansionGroupViewModel(
        long baseWorkId, MergeSideViewModel baseSide, IReadOnlyList<ExpansionMemberViewModel> members)
    {
        ArgumentNullException.ThrowIfNull(baseSide);
        ArgumentNullException.ThrowIfNull(members);

        if (members.Count == 0)
        {
            throw new ArgumentException(
                "An expansion group needs at least one proposed expansion.", nameof(members));
        }

        BaseWorkId = baseWorkId;
        Base = baseSide;
        Members = members;
    }

    /// <summary>The parent of every link this card writes. Fixed by the relation, never chosen.</summary>
    public long BaseWorkId { get; }

    /// <summary>The base game's face: title, cover, year, publisher, stores.</summary>
    public MergeSideViewModel Base { get; }

    /// <summary>The proposed expansions, in the order the scan produced them.</summary>
    public IReadOnlyList<ExpansionMemberViewModel> Members { get; }

    /// <summary>True while this card is the one the keyboard acts on.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Latched before the write, so a double click cannot answer twice.</summary>
    [ObservableProperty]
    public partial bool IsDecided { get; set; }

    /// <summary>The title the packs would be grouped under.</summary>
    public string BaseTitle => Base.Title;

    /// <summary>How many packs the card proposes. Plex Mono, tabular (§3).</summary>
    public string MemberCountText =>
        Members.Count.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>Uppercase label above the base game.</summary>
    public string BaseLabel => ExpansionCopy.BaseLabel;

    /// <summary>Uppercase label beside the pack count.</summary>
    public string MemberCountLabel => ExpansionCopy.MemberCountLabel;

    /// <summary>The sentence about what grouping does not do. Hours and counts stay put.</summary>
    public string EffectLine => ExpansionCopy.GroupEffect;

    /// <summary>Label on the affirmative answer.</summary>
    public string GroupButtonText => ExpansionCopy.GroupButton;

    /// <summary>Label on the negative answer.</summary>
    public string NotExpansionsButtonText => ExpansionCopy.NotExpansionsButton;

    /// <summary>Tooltip on Group, naming the G shortcut.</summary>
    public string GroupTooltip => ExpansionCopy.GroupTooltip;

    /// <summary>Tooltip on Not expansions, naming the N shortcut.</summary>
    public string NotExpansionsTooltip => ExpansionCopy.NotExpansionsTooltip;

    /// <summary>The checked members, in work id order — the children of one act.</summary>
    public IReadOnlyList<long> IncludedChildWorkIds
    {
        get
        {
            var included = new List<long>(Members.Count);
            foreach (var member in Members)
            {
                if (member.IsIncluded)
                {
                    included.Add(member.WorkId);
                }
            }

            included.Sort();
            return included;
        }
    }

    /// <summary>
    /// The pairs the answer says NO to: every member the user left unchecked.
    /// Recorded, so an unchecked member is an answer and not a card that comes
    /// back on the next scan.
    /// </summary>
    public IReadOnlyList<ExpansionRefusalRequest> RefusedPairs => Pairs(included: false);

    /// <summary>Every pair on the card, for the answer that takes none of them.</summary>
    public IReadOnlyList<ExpansionRefusalRequest> AllPairs
    {
        get
        {
            var pairs = new List<ExpansionRefusalRequest>(Members.Count);
            foreach (var member in Members)
            {
                pairs.Add(new ExpansionRefusalRequest(BaseWorkId, member.WorkId));
            }

            return pairs;
        }
    }

    /// <summary>Names the packs and the base, never the verb alone (§8).</summary>
    public string GroupAutomationName => string.Format(
        CultureInfo.CurrentCulture,
        ExpansionCopy.GroupAutomationFormat,
        string.Join(MergeCopy.MemberSeparator, Members.Select(m => m.Label)),
        BaseTitle);

    /// <summary>Names the packs the answer is about (§8).</summary>
    public string NotExpansionsAutomationName => string.Format(
        CultureInfo.CurrentCulture,
        ExpansionCopy.NotExpansionsAutomationFormat,
        string.Join(MergeCopy.MemberSeparator, Members.Select(m => m.Label)));

    /// <summary>Sets the display resolution for cover decoding, base and packs alike.</summary>
    /// <param name="displayWidthPixels">Device pixels the capsule will occupy.</param>
    public void RequestCovers(double displayWidthPixels)
    {
        Base.RequestCover(displayWidthPixels);
        foreach (var member in Members)
        {
            member.RequestCover(displayWidthPixels);
        }
    }

    private IReadOnlyList<ExpansionRefusalRequest> Pairs(bool included)
    {
        var pairs = new List<ExpansionRefusalRequest>(Members.Count);
        foreach (var member in Members)
        {
            if (member.IsIncluded == included)
            {
                pairs.Add(new ExpansionRefusalRequest(BaseWorkId, member.WorkId));
            }
        }

        return pairs;
    }
}
