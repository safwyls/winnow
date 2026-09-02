namespace Winnow.App.ViewModels;

/// <summary>
/// User-facing copy for the expansion relation: the EXPANSIONS segment of the
/// Same Game screen and the EXPANSIONS section of the game details modal. All
/// strings in one file so the question, the answers, the outcome reports and
/// the history rows can be read together.
///
/// <para>The one thing every string here has to get right: grouping an
/// expansion under a base game changes PRESENTATION ONLY. No count, no
/// playtime, no bucket and no recommendation moves. Nothing here may say or
/// imply that hours are combined, that the library got smaller, or that the
/// expansion stopped being a game the user owns.</para>
/// </summary>
public static class ExpansionCopy
{
    // ══ Chrome ════════════════════════════════════════════════════════════

    /// <summary>Uppercase segment label, the middle of REVIEW / EXPANSIONS / HISTORY.</summary>
    public const string SegmentExpansions = "EXPANSIONS";

    /// <summary>Tooltip on that segment. Names what the surface holds.</summary>
    public const string SegmentExpansionsTooltip = "Base games and their packs";

    /// <summary>Automation name for the EXPANSIONS tab. <c>{0}</c> is the
    /// count of base games; one card is one base game, never a pack. The
    /// tab draws the label and a bare number side by side, so this string
    /// is the only place a screen reader learns what the number
    /// counts.</summary>
    public const string SegmentExpansionsAutomationFormat = "Expansions, {0} to answer";

    /// <summary>The question the surface asks, display L.</summary>
    public const string ScreenQuestion = "Expansion?";

    /// <summary>
    /// Standing introduction under the question. The one place that says
    /// the three facts every card used to repeat: grouping is display only,
    /// hours and counts stay separate, and the act can be undone. A few
    /// words, not a blurb (notes.md).
    /// </summary>
    public const string Intro = "Display only. Hours and counts stay separate. Undo any time.";

    /// <summary>Empty state after a scan found nothing. §7: a direction, not a mood.</summary>
    public const string EmptyScanned = "No expansions to review.";

    /// <summary>Empty state before the first scan of the session has finished.</summary>
    public const string EmptyNotScanned = "Still scanning your library.";

    // ══ The card ══════════════════════════════════════════════════════════

    /// <summary>
    /// Uppercase label above the base game's title. Not a control: the base is
    /// fixed by the relation, so there is nothing here to choose.
    /// </summary>
    public const string BaseLabel = "BASE GAME";

    /// <summary>Uppercase label beside the member count.</summary>
    public const string MemberCountLabel = "PACKS";

    /// <summary>The affirmative answer. Names the act, not the relation.</summary>
    public const string GroupButton = "Group";

    /// <summary>
    /// The negative answer. Says what the pair is, not that the proposal was
    /// wrong, because the user is answering about games and not about a guess.
    /// </summary>
    public const string NotExpansionsButton = "Not expansions";

    /// <summary>Tooltip on Group. States that only checked packs are taken, and the shortcut.</summary>
    public const string GroupTooltip = "Group the checked packs (G)";

    /// <summary>Tooltip on Not expansions. States what is recorded, and the shortcut.</summary>
    public const string NotExpansionsTooltip = "Record as separate games (N)";

    // ══ The evidence line ═════════════════════════════════════════════════

    /// <summary>
    /// Uppercase label before the words the pack adds to the base title. This
    /// is the card's central fact, and it is what the same-game card's title
    /// distance cannot express.
    /// </summary>
    public const string ExtendsLabel = "EXTENDS BY";

    /// <summary>Uppercase label before the year gap. Signed, or an em dash when unknown.</summary>
    public const string YearLabel = "YEAR";

    /// <summary>Uppercase label before the publisher verdict.</summary>
    public const string PublisherLabel = "PUBLISHER";

    /// <summary>
    /// Uppercase label before the separator verdict. Named for what the user can
    /// see in the title rather than for the rule behind it.
    /// </summary>
    public const string SeparatorLabel = "TITLE SPLIT";

    /// <summary>The pack's own title splits at the base game's name.</summary>
    public const string SeparatorYes = "YES";

    /// <summary>It does not, so the proposal rests on the publisher or the years.</summary>
    public const string SeparatorNo = "NO";

    // ══ What answering reported ═══════════════════════════════════════════

    /// <summary>
    /// Report after a group was written. <c>{0}</c> the base title,
    /// <c>{1}</c> how many packs joined it. The second sentence is the load
    /// bearing one: nothing on the library screen moved, and the report says
    /// so rather than letting the user go looking for a change.
    /// </summary>
    public const string GroupedReportFormat = "Grouped {1} under {0}. Hours unchanged.";

    /// <summary>
    /// Report when the answer checked nothing, which is the same answer as
    /// Not expansions and is recorded the same way.
    /// </summary>
    public const string NothingGrouped = "Nothing grouped, recorded as separate games.";

    // ══ History ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Spoken form of an expansion group act. <c>{0}</c> the comma-joined
    /// pack titles, <c>{1}</c> the base game. Reads "grouped under", never
    /// "linked under": a same-game link says two entries are one game, and
    /// an expansion grouping says one game extends another and moves no
    /// number. A row that read the same for both would invite the user to
    /// undo the wrong one. The drawn row now carries this distinction in its
    /// GROUPED meta label; this format carries it for the spoken form alone
    /// (the Undo control's automation name).
    /// </summary>
    public const string GroupRowFormat = "{0} grouped under {1}";

    /// <summary>
    /// Uppercase label before the date on a group row, where a same-game row
    /// says LINKED.
    /// </summary>
    public const string GroupedAtLabel = "GROUPED";

    // ══ The details modal ═════════════════════════════════════════════════

    /// <summary>Section heading listing the packs grouped under this game.</summary>
    public const string ExpansionsHeading = "EXPANSIONS";

    /// <summary>
    /// Caption under that heading. The hours on the rows below are each pack's
    /// own and are not added to this game's, which is the one visible
    /// difference from ALSO COVERS, where a total is the point.
    /// </summary>
    public const string ExpansionsNote = "Counted separately. Not added above.";

    /// <summary>
    /// Uppercase label on a pack's own modal, naming the base game it extends.
    /// The other end of the same relation.
    /// </summary>
    public const string ExtendsHeading = "EXTENDS";

    /// <summary>
    /// Caption under that label. Says the thing a grouping could otherwise be
    /// read to deny: this is still its own game in the library.
    /// </summary>
    public const string ExtendsNote = "A separate game, grouped for display.";

    /// <summary>
    /// Control that ungroups one row. Ordinary and repeatable, so it must not
    /// read as a last chance.
    /// </summary>
    public const string UngroupButton = "Ungroup";

    /// <summary>
    /// Tooltip on that control. Describes the display change, which is the only
    /// change there is.
    /// </summary>
    public const string UngroupTooltip = "Show it on its own again";

    /// <summary>
    /// Automation name for the ungroup control. <c>{0}</c> the title,
    /// <c>{1}</c> the store names. Names the item, not the verb alone (§8),
    /// and carries the store because two rows can share a title.
    /// </summary>
    public const string UngroupAutomationFormat = "Ungroup {0} ({1})";

    /// <summary>A write that did not land. Amber, non-blocking.</summary>
    public const string UngroupProblem = "Couldn't ungroup that just now.";

    // ══ Automation ════════════════════════════════════════════════════════

    /// <summary>
    /// Automation name for Group. <c>{0}</c> the comma-joined member labels,
    /// <c>{1}</c> the base title. Names the packs and the base, never the verb
    /// alone (§8).
    /// </summary>
    public const string GroupAutomationFormat = "Group: {0} under {1}";

    /// <summary>
    /// Automation name for Not expansions. <c>{0}</c> the comma-joined member
    /// labels.
    /// </summary>
    public const string NotExpansionsAutomationFormat = "Not expansions: {0}";
}
