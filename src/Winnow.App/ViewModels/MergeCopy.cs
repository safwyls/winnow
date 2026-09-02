namespace Winnow.App.ViewModels;

/// <summary>
/// User-facing copy for the Same Game screen. All strings in one file so the
/// review queue, group cards, outcome reports and history can be reviewed
/// together. Answering a group writes a link, not a merge; every label must
/// be exact about what pressing it does.
/// </summary>
public static class MergeCopy
{
    // ══ Chrome ════════════════════════════════════════════════════════════

    /// <summary>Small uppercase label at the left of the 48px header,
    /// beside the segment control.</summary>
    public const string ScreenLabel = "SAME GAME";

    /// <summary>The question the review surface asks, display L weight.</summary>
    public const string ScreenQuestion = "Same game?";

    /// <summary>Uppercase segment label for the review queue.</summary>
    public const string SegmentReview = "REVIEW";

    /// <summary>Uppercase segment label for the link history.</summary>
    public const string SegmentHistory = "HISTORY";

    /// <summary>Tooltip on the Review segment. The unit is a group of store
    /// entries, never a pair.</summary>
    public const string SegmentReviewTooltip =
        "Groups waiting for an answer";

    /// <summary>Tooltip on the History segment. The list holds both relations,
    /// same-game links and expansion groupings, and only the acts still in
    /// force.</summary>
    public const string SegmentHistoryTooltip =
        "What you have linked and grouped";

    /// <summary>Automation name for the REVIEW tab. <c>{0}</c> is the count.
    /// The tab draws the label and a bare number side by side, so this
    /// string is the only place a screen reader learns what the number
    /// counts.</summary>
    public const string SegmentReviewAutomationFormat = "Review, {0} to answer";

    /// <summary>Automation name for the HISTORY tab. <c>{0}</c> is the count
    /// of acts still in force. History is a log, not outstanding work.</summary>
    public const string SegmentHistoryAutomationFormat = "History, {0} recorded";

    /// <summary>Tooltip on the rail's SAME GAME? row. The row counts review
    /// plus expansions, so the sentence covers both questions.</summary>
    public const string RailTooltip =
        "Groups that might be the same game, or expansions of one";

    // ══ Review — the queue ════════════════════════════════════════════════

    /// <summary>
    /// Standing introduction under the screen title. The one place that says
    /// what every card used to repeat: answering makes one tile with a chip
    /// per store, and the answer can be undone. A few words, not a blurb
    /// (notes.md).
    /// </summary>
    public const string QueueIntro =
        "One tile, a chip per store. Undo any time.";

    /// <summary>Tooltip on Same game. States the keyboard shortcut and
    /// that pressing it links the checked entries.</summary>
    public const string SameGameTooltip =
        "Link these entries (S)";

    /// <summary>Tooltip on Different games. States the shortcut and that
    /// the answer is not re-queued.</summary>
    public const string DifferentGamesTooltip =
        "Record as different, not re-queued (D)";

    // ══ The group card ════════════════════════════════════════════════════

    /// <summary>Label on the affirmative answer (§7: "Same game", never
    /// "Merge records").</summary>
    public const string SameGameButton = "Same game";

    /// <summary>Label on the negative answer. Not a cancel; it is the other
    /// half of the answer.</summary>
    public const string DifferentGamesButton = "Different games";

    /// <summary>Uppercase label before the matcher's confidence figure at
    /// the head of the card.</summary>
    public const string ConfidenceLabel = "CONFIDENCE";

    /// <summary>Uppercase label beside the count of members on the card. The
    /// unit is a title, not a store entry.</summary>
    public const string MemberCountLabel = "TITLES";

    /// <summary>
    /// The mark on a card whose strongest proposal the matcher placed in its
    /// top confidence band. Several cards carry it at once; it says nothing
    /// about position, only confidence. No tooltip.
    /// </summary>
    public const string PriorityBandLabel = "STRONG MATCH";

    /// <summary>Uppercase label before the title distance on a member's
    /// condensed evidence line.</summary>
    public const string TitleDistanceLabel = "TITLE";

    /// <summary>Uppercase label before the year delta.</summary>
    public const string YearDeltaLabel = "YEAR";

    /// <summary>Uppercase label before the publisher verdict.</summary>
    public const string PublisherMatchLabel = "PUBLISHER";

    // ══ The primary chooser ═══════════════════════════════════════════════

    /// <summary>Small uppercase label rendered beside the reason phrase.</summary>
    public const string SurvivorReasonLabel = "WHY";

    /// <summary>The survivor holds an IGDB match the other side does not.</summary>
    public const string SurvivorReasonIgdbMatch = "IGDB match";

    /// <summary>The survivor carries a real title from a store, not a
    /// placeholder.</summary>
    public const string SurvivorReasonNamedByStore = "Named by store";

    /// <summary>The survivor already has more store entries hanging off
    /// it.</summary>
    public const string SurvivorReasonMostStoreEntries = "Most store entries";

    /// <summary>Nothing else discriminated. The survivor won because it was
    /// ingested first, and the card says so rather than staying silent.</summary>
    public const string SurvivorReasonAddedFirst = "Added first";

    /// <summary>The user picked the survivor (TASK-70.3 ships the picker).
    /// Overrides every rung of the ladder.</summary>
    public const string SurvivorReasonChosenByYou = "Your choice";

    /// <summary>Label beside a member's primary radio. Must not read as a verb
    /// phrase for the whole card.</summary>
    public const string PrimaryControlLabel = "Keep this title";

    /// <summary>Label beside the checkbox that decides whether a member joins.</summary>
    public const string IncludeControlLabel = "Include";

    /// <summary>Names the sibling a member reaches the group through when no
    /// proposal named it and the chosen title together. <c>{0}</c> the sibling's
    /// title.</summary>
    public const string MemberThroughFormat = "Indirect, via {0}";

    /// <summary>Shown in place of a breakdown when a proposal carries no
    /// recorded evidence.</summary>
    public const string NoSignals = "No breakdown recorded.";

    /// <summary>Toggle label for the matcher's own sentences, closed.</summary>
    public const string EvidenceShow = "Show evidence";

    /// <summary>Toggle label for the matcher's own sentences, open.</summary>
    public const string EvidenceHide = "Hide evidence";

    /// <summary>Publisher verdict: the two agree.</summary>
    public const string PublisherSame = "SAME";

    /// <summary>Publisher verdict: the two disagree.</summary>
    public const string PublisherDifferent = "DIFFERENT";

    /// <summary>One member's evidence on one line, for a screen reader.
    /// <c>{0}</c> title distance, <c>{1}</c> year delta, <c>{2}</c> publisher
    /// verdict.</summary>
    public const string EdgeSummaryFormat = "Title {0}, year {1}, publisher {2}";

    /// <summary>Empty review state after a sweep completed and found nothing.
    /// §7: empty states are directions, not moods.</summary>
    public const string EmptySwept = "No matches to review.";

    /// <summary>Empty review state when no sweep has completed yet.</summary>
    public const string EmptyNotSwept = "Still scanning your library.";

    // ══ What answering reported ═══════════════════════════════════════════

    /// <summary>Report after a group was linked. <c>{0}</c> the title the
    /// library keeps, <c>{1}</c> how many titles joined it.</summary>
    public const string LinkedReportFormat = "Linked {1} under {0}.";

    /// <summary>Report when the answer included no member, so nothing was
    /// linked and the proposals were recorded as different games.</summary>
    public const string NothingLinked = "Nothing linked, recorded as different games.";

    /// <summary>Report after a link act was undone. The proposals return to
    /// the queue.</summary>
    public const string Undone = "Undone. Returns to review.";

    /// <summary>Report when the act had already been undone. A no-op, not
    /// an error.</summary>
    public const string UndoneAlready = "Already undone.";

    // ══ History ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Section heading over the history list. Covers both relations the list
    /// holds: same-game links ("linked under") and expansion groupings
    /// ("grouped under"). Display L weight, sentence case.
    /// </summary>
    public const string LinkHistoryHeading = "Linked and grouped";

    /// <summary>Introduction under that heading. States the sort order and that
    /// every row can be undone. Undone acts leave the list.</summary>
    public const string LinkHistoryIntro = "Newest first. Undo any time.";

    /// <summary>Empty state for the link list. §7: a direction, not a mood.</summary>
    public const string LinkHistoryEmpty = "Groups you link appear here.";

    /// <summary>
    /// Spoken form of a same-game link act. <c>{0}</c> the comma-joined
    /// linked titles, <c>{1}</c> the title the library keeps. Spoken only:
    /// it reaches the user through the Undo control's automation name and is
    /// never drawn. The drawn row uses position (headline plus subtext) to
    /// carry the relation.
    /// </summary>
    public const string LinkRowFormat = "{0} linked under {1}";

    /// <summary>
    /// History's sole disambiguation format: <c>{0}</c> the title, <c>{1}</c>
    /// the store names. This is the whole of history's qualifier; it never
    /// adds year, publisher or position. <see cref="MemberLabelFormat"/> uses
    /// the same "{0} ({1})" shape but carries an escalating ladder of facts
    /// through <see cref="MergeMemberLabels"/>. The divergence is deliberate:
    /// a card is one question being answered, a log is a list being scanned,
    /// and the log's rule lives in <see cref="MergeHistoryLabels"/>.
    /// </summary>
    public const string HistoryQualifierFormat = "{0} ({1})";

    /// <summary>Small uppercase label before the date on a link row.</summary>
    public const string LinkedAtLabel = "LINKED";

    /// <summary>
    /// The control that reverses a link act. Ordinary and repeatable, so it
    /// must not read as a last chance.
    /// </summary>
    public const string UndoButton = "Undo";

    /// <summary>Tooltip on the undo control. States where the proposals go.</summary>
    public const string UndoTooltip = "Proposals return to review.";

    // ══ Automation ════════════════════════════════════════════════════════

    /// <summary>Automation name for Same game. <c>{0}</c> the
    /// comma-joined member labels, <c>{1}</c> the kept title.</summary>
    public const string SameGameAutomationFormat =
        "Same game: {0}, keep {1}";

    /// <summary>Automation name for Different games. <c>{0}</c> the
    /// comma-joined member labels.</summary>
    public const string DifferentGamesAutomationFormat =
        "Different games: {0}";

    /// <summary>
    /// One member for a screen reader when its title alone would name two
    /// members. <c>{0}</c> the title, <c>{1}</c> the qualifying facts,
    /// comma-joined: stores, year, publisher, added one at a time and only
    /// while a collision remains. These are the facts already drawn on the
    /// row (§10.5 rejected surfacing database ids).
    /// </summary>
    public const string MemberLabelFormat = "{0} ({1})";

    /// <summary>Joins the qualifying facts inside a member label.</summary>
    public const string MemberQualifierSeparator = ", ";

    /// <summary>
    /// Last resort for two members a storefront describes identically down
    /// to the publisher. <c>{0}</c> this member's position on the card,
    /// <c>{1}</c> how many members the card holds.
    /// </summary>
    public const string MemberPositionFormat = "{0} of {1}";

    /// <summary>Joins member labels in a list.</summary>
    public const string MemberSeparator = ", ";

    /// <summary>Automation name for a member's primary radio. <c>{0}</c> the
    /// member label. Names the member, never the verb.</summary>
    public const string PrimaryAutomationFormat = "Keep {0}";

    /// <summary>Automation name for a member's include checkbox. <c>{0}</c> the
    /// member label. Names the member, never the verb.</summary>
    public const string IncludeAutomationFormat = "Include {0}";

    /// <summary>Automation name for the undo control on a history row.
    /// <c>{0}</c> the row's description, so a column of undo buttons is not
    /// one target.</summary>
    public const string UndoAutomationFormat = "Undo: {0}";
}
