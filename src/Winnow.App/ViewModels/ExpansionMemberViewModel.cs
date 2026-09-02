using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Winnow.Core.Identity;

namespace Winnow.App.ViewModels;

/// <summary>
/// One proposed expansion on an expansion card.
///
/// <para>It has an include checkbox and NO primary
/// radio, which is the one structural difference from
/// <see cref="MergeGroupMemberViewModel"/>: on a same-game card either title
/// could be the one the library keeps, so the choice is the user's; here the
/// parent is determined by the relation itself, and a radio would let someone
/// assert that Civilization IV is an expansion of Beyond the Sword.</para>
/// </summary>
public partial class ExpansionMemberViewModel : ObservableObject
{
    /// <summary>Roster chip geometry, the same 2:3 portrait the merge roster uses.</summary>
    public const double ChipWidth = MergeGroupMemberViewModel.ChipWidth;

    /// <summary>2:3 portrait, matching the roster chip.</summary>
    public const double ChipHeight = MergeGroupMemberViewModel.ChipHeight;

    /// <summary>Creates a roster row, checked.</summary>
    /// <param name="workId">The proposed expansion's work id.</param>
    /// <param name="side">Its face: title, cover, stores, entry numbers.</param>
    /// <param name="evidence">What the detector observed about this pair.</param>
    public ExpansionMemberViewModel(
        long workId, MergeSideViewModel side, ExpansionEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(side);
        ArgumentNullException.ThrowIfNull(evidence);

        WorkId = workId;
        Side = side;
        Evidence = evidence;

        // Checked by default, unlike a same-game roster row. A same-game group
        // is a connected component, so an unchecked default guards against the
        // closure asserting more than any single proposal did. An expansion
        // proposal is one direct claim about one pair, with corroboration
        // already required before it was made, so there is no closure to
        // guard and the card asks exactly the question the detector asked.
        IsIncluded = true;
    }

    /// <summary>The work this row proposes as an expansion. The child of the link a Group writes.</summary>
    public long WorkId { get; }

    /// <summary>Its face: title, cover, stores, entry numbers.</summary>
    public MergeSideViewModel Side { get; }

    /// <summary>What the detector observed about this pair.</summary>
    public ExpansionEvidence Evidence { get; }

    /// <summary>Whether this pack joins the act. Checked by default; see the constructor.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IncludeAutomationName))]
    public partial bool IsIncluded { get; set; }

    /// <summary>The part of the title that extends the base. The card's central fact.</summary>
    public string SuffixText => Evidence.Suffix;

    /// <summary>The year gap, signed, or an em dash when either year is unknown.</summary>
    public string YearDeltaText => ExpansionEvidenceText.YearText(Evidence);

    /// <summary>SAME, DIFFERENT, or an em dash when either publisher is unknown.</summary>
    public string PublisherText => ExpansionEvidenceText.PublisherText(Evidence);

    /// <summary>
    /// Whether the pack's own title separates the base from the extension with
    /// a colon or a dash. This is the corroboration that needs no enrichment,
    /// which is what makes the feature work on a library the metadata backfill
    /// has not reached.
    /// </summary>
    public string SeparatorText => Evidence.HasSeparatorBoundary
        ? ExpansionCopy.SeparatorYes
        : ExpansionCopy.SeparatorNo;

    /// <summary>Uppercase label before the extending words.</summary>
    public string ExtendsLabel => ExpansionCopy.ExtendsLabel;

    /// <summary>Uppercase label before the year gap.</summary>
    public string YearLabel => ExpansionCopy.YearLabel;

    /// <summary>Uppercase label before the publisher verdict.</summary>
    public string PublisherLabel => ExpansionCopy.PublisherLabel;

    /// <summary>Uppercase label before the separator verdict.</summary>
    public string SeparatorLabel => ExpansionCopy.SeparatorLabel;

    /// <summary>Label beside the include checkbox, shared with the merge roster.</summary>
    public string IncludeControlText => MergeCopy.IncludeControlLabel;

    /// <summary>Store badges for this pack.</summary>
    public IReadOnlyList<string> StoreChips => Side.StoreChips;

    /// <summary>Store names, comma-joined, for a screen reader.</summary>
    public string StoreNames => Side.StoreNames;

    /// <summary>False when no ownership row named a store, in which case the label falls back to entry numbers.</summary>
    public bool HasStores => Side.HasStores;

    /// <summary>This pack's store entry numbers.</summary>
    public string ReleasesText => Side.ReleaseText;

    /// <summary>
    /// The title with its stores and entry numbers, so two members that share a
    /// title are still distinguishable.
    /// </summary>
    public string Label => HasStores
        ? string.Format(
            CultureInfo.CurrentCulture,
            MergeCopy.MemberWithStoreAutomationFormat,
            Side.Title,
            StoreNames,
            ReleasesText)
        : string.Format(
            CultureInfo.CurrentCulture, MergeCopy.MemberAutomationFormat, Side.Title, ReleasesText);

    /// <summary>Names the member, never the verb alone (§8).</summary>
    public string IncludeAutomationName => string.Format(
        CultureInfo.CurrentCulture, MergeCopy.IncludeAutomationFormat, Label);

    /// <summary>Sets the display resolution for cover decoding.</summary>
    /// <param name="displayWidthPixels">Device pixels the chip will occupy.</param>
    public void RequestCover(double displayWidthPixels) => Side.RequestCover(displayWidthPixels);
}
