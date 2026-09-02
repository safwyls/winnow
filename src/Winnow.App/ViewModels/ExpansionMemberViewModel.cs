using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Winnow.Core.Identity;

namespace Winnow.App.ViewModels;

/// <summary>
/// One proposed expansion on an expansion card.
///
/// <para>It has an include checkbox, checked by default only when the relation
/// is the one the surface asks about (expansion_of), and no primary radio.
/// That is the one structural difference from
/// <see cref="MergeGroupMemberViewModel"/>: on a same-game card either title
/// could be the one the library keeps, so the choice is the user's; here the
/// parent is determined by the relation itself, and a radio would let someone
/// assert that Civilization IV is an expansion of Beyond the Sword. A row
/// carrying variant_of (demo, beta, playtest) is shown because that is where
/// the pair was found, but arrives unticked so the primary button cannot
/// assert a relation the header never asked about.</para>
/// </summary>
public partial class ExpansionMemberViewModel : ObservableObject
{
    /// <summary>Creates a roster row, checked when the relation is the one the surface asks about.</summary>
    /// <param name="workId">The proposed expansion's work id.</param>
    /// <param name="side">Its face: title, cover, stores.</param>
    /// <param name="evidence">What the detector observed about this pair.</param>
    /// <param name="relationLabel">
    /// The storefront's own word for the relation (demo, beta, playtest,
    /// expansion, dlc, standalone expansion, remaster, remake, port, mod,
    /// superseded, among others), or null when nothing named it. The row
    /// shows this label rather than the link kind, so a playtest stops
    /// reading as an expansion. When null, <paramref name="kind"/> supplies
    /// the word if it can.
    /// </param>
    /// <param name="kind">
    /// The link kind (expansion_of, variant_of). Supplies the relation word
    /// when <paramref name="relationLabel"/> is null and the kind has an
    /// unambiguous default; see <see cref="Word"/>.
    /// </param>
    public ExpansionMemberViewModel(
        long workId,
        MergeSideViewModel side,
        ExpansionEvidence evidence,
        string? relationLabel = null,
        string? kind = null)
    {
        ArgumentNullException.ThrowIfNull(side);
        ArgumentNullException.ThrowIfNull(evidence);

        WorkId = workId;
        Side = side;
        Evidence = evidence;
        RelationText = Word(relationLabel, kind);
        IsAskedRelation = kind is null || kind == IdentityLinkKinds.ExpansionOf;

        // Checked by default only when the relation is the one the surface
        // asks about. A same-game group is a connected component, so an
        // unchecked default guards against the closure asserting more than any
        // single proposal did; an expansion proposal is one direct claim about
        // one pair and has no closure to guard. The condition matters because
        // a variant_of row (demo, beta, playtest) answers a different question
        // than the one the header asks, and pre-ticking it would make the
        // primary button assert a relation the header never named.
        IsIncluded = IsAskedRelation;
    }

    /// <summary>The work this row proposes as an expansion. The child of the link a Group writes.</summary>
    public long WorkId { get; }

    /// <summary>Its face: title, cover, stores, entry numbers.</summary>
    public MergeSideViewModel Side { get; }

    /// <summary>What the detector observed about this pair.</summary>
    public ExpansionEvidence Evidence { get; }

    /// <summary>
    /// The relation word, uppercased for the row. Empty only when neither a
    /// source nor the kind supplied one; in practice, expansion_of always
    /// yields a word through the kind fallback, so every expansion row states
    /// its relation. Shown instead of the raw link kind because the vocabulary
    /// is open and calling a playtest an expansion was the confusion this fixes.
    /// </summary>
    public string RelationText { get; }

    /// <summary>False when neither a source nor the kind supplied a relation
    /// word. The row draws no relation label in that case.</summary>
    public bool HasRelation => RelationText.Length > 0;

    /// <summary>
    /// True when the row's relation is the one the surface asks about
    /// (expansion_of). False for a variant_of row (demo, beta, playtest),
    /// which is shown because that is where the pair was found but starts
    /// unticked because the header's question does not cover it.
    /// </summary>
    public bool IsAskedRelation { get; }

    /// <summary>Whether this pack joins the act. Checked by default when <see cref="IsAskedRelation"/> is true; see the constructor.</summary>
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

    // A source's own word wins. When no source named one, the kind supplies
    // a word from the same vocabulary rather than leaving the row silent.
    // Before this, every title-heuristic proposal drew a blank column because
    // ExpansionDetector sets no label on that path.
    //
    // variant_of is deliberately left blank when unnamed: the detector only
    // produces that kind together with the word that earned it (demo, beta,
    // playtest), so a blank there is a bug elsewhere, and inventing a generic
    // word would hide it.
    private static string Word(string? relationLabel, string? kind)
    {
        if (!string.IsNullOrWhiteSpace(relationLabel))
        {
            return relationLabel.Trim().ToUpperInvariant();
        }

        return kind == IdentityLinkKinds.ExpansionOf
            ? RelationLabels.Expansion.ToUpperInvariant()
            : string.Empty;
    }
}
