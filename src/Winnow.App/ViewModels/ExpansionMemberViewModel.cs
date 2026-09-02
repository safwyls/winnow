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
    /// <summary>Creates a roster row, checked.</summary>
    /// <param name="workId">The proposed expansion's work id.</param>
    /// <param name="side">Its face: title, cover, stores.</param>
    /// <param name="evidence">What the detector observed about this pair.</param>
    /// <param name="relationLabel">
    /// The storefront's own word for the relation — demo, beta, playtest,
    /// expansion, dlc, standalone expansion, remaster, remake, port, mod,
    /// superseded, among others — or null when nothing named it. The row
    /// shows this label rather than the link kind, so a playtest stops
    /// reading as an expansion.
    /// </param>
    public ExpansionMemberViewModel(
        long workId,
        MergeSideViewModel side,
        ExpansionEvidence evidence,
        string? relationLabel = null)
    {
        ArgumentNullException.ThrowIfNull(side);
        ArgumentNullException.ThrowIfNull(evidence);

        WorkId = workId;
        Side = side;
        Evidence = evidence;
        RelationText = string.IsNullOrWhiteSpace(relationLabel)
            ? string.Empty
            : relationLabel.Trim().ToUpperInvariant();

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

    /// <summary>
    /// The storefront's word for the relation, uppercased for the row, or
    /// empty when no source named one. Shown instead of the link kind,
    /// because the vocabulary is open and calling a playtest an expansion
    /// was the confusion this fixes.
    /// </summary>
    public string RelationText { get; }

    /// <summary>False when no source named the relation. The row draws no
    /// relation label in that case.</summary>
    public bool HasRelation => RelationText.Length > 0;

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

    /// <summary>Label beside the include checkbox.</summary>
    public string IncludeControlText => MergeCopy.IncludeControlLabel;

    /// <summary>Store badges for this pack.</summary>
    public IReadOnlyList<string> StoreChips => Side.StoreChips;

    /// <summary>Store names, comma-joined, for a screen reader.</summary>
    public string StoreNames => Side.StoreNames;

    /// <summary>False when no ownership row named a store, in which case the label falls back to entry numbers.</summary>
    public bool HasStores => Side.HasStores;

    /// <summary>
    /// What a screen reader calls this pack. Assigned by the card through
    /// <see cref="MergeMemberLabels"/>, which adds stores, year and publisher
    /// only while two packs would otherwise share a label. No database ids
    /// (§10.5).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IncludeAutomationName))]
    public partial string Label { get; set; } = string.Empty;

    /// <summary>Names the member, never the verb alone (§8).</summary>
    public string IncludeAutomationName => string.Format(
        CultureInfo.CurrentCulture, MergeCopy.IncludeAutomationFormat, Label);

    /// <summary>Sets the display resolution for cover decoding.</summary>
    /// <param name="displayWidthPixels">Device pixels the chip will occupy.</param>
    public void RequestCover(double displayWidthPixels) => Side.RequestCover(displayWidthPixels);
}
