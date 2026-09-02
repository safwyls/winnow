namespace Winnow.App.ViewModels;

/// <summary>
/// User-facing copy for the Same Game screen. All strings in one file so the
/// review queue, group cards, outcome reports and history can be reviewed
/// together. Answering a group writes a link, not a merge; every label must
/// be exact about what pressing it does, and honest that the link is
/// currently inert in the library grid.
/// </summary>
public static class MergeCopy
{
    // ══ Chrome ════════════════════════════════════════════════════════════

    /// <summary>Small uppercase label at the left of the 48px header,
    /// beside the segment control.</summary>
    public const string ScreenLabel = "SAME GAME";

    /// <summary>Uppercase segment label for the review queue.</summary>
    public const string SegmentReview = "REVIEW";

    /// <summary>Uppercase segment label for the history of link acts and
    /// applied merges.</summary>
    public const string SegmentHistory = "HISTORY";

    /// <summary>Tooltip on the Review segment.</summary>
    public const string SegmentReviewTooltip =
        "Pairs waiting for an answer";

    /// <summary>Tooltip on the History segment.</summary>
    public const string SegmentHistoryTooltip =
        "Links and applied merges";

    // ══ Review — the queue ════════════════════════════════════════════════

    /// <summary>Standing introduction under the screen title. States
    /// that a link is retractable.</summary>
    public const string QueueIntro =
        "Links can be retracted.";

    /// <summary>Tooltip on Same game. States the keyboard shortcut and
    /// that pressing it links the checked entries.</summary>
    public const string SameGameTooltip =
        "Link these entries (S)";

    /// <summary>Tooltip on Different games. States the shortcut and that
    /// the answer is not re-queued.</summary>
    public const string DifferentGamesTooltip =
        "Record as different, not re-queued (D)";

    // ══ The inline preview ════════════════════════════════════════════════

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

    // ══ Merge modes and limits ════════════════════════════════════════════

    // ══ Automation ════════════════════════════════════════════════════════

    /// <summary>Automation name for Same game. <c>{0}</c> the
    /// comma-joined member labels, <c>{1}</c> the kept title.</summary>
    public const string SameGameAutomationFormat =
        "Same game: {0}, keep {1}";

    /// <summary>Automation name for Different games. <c>{0}</c> the
    /// comma-joined member labels.</summary>
    public const string DifferentGamesAutomationFormat =
        "Different games: {0}";

    // ══ The group card ════════════════════════

    /// <summary>Uppercase label beside the pending count in the review header.
    /// The unit is a group of store entries, not a pair.</summary>
    public const string PendingCountLabel = "GROUPS";

    /// <summary>Empty review state after a sweep completed and found nothing.
    /// §7: empty states are directions, not moods.</summary>
    public const string EmptySwept = "No matches to review.";

    /// <summary>Empty review state when no sweep has completed yet.</summary>
    public const string EmptyNotSwept = "Still scanning your library.";

    /// <summary>Small uppercase label above the title the library keeps.</summary>
    public const string PrimaryLabel = "KEEP";

    /// <summary>Label beside a member's primary radio. Must not read as a verb
    /// phrase for the whole card.</summary>
    public const string PrimaryControlLabel = "Keep this title";

    /// <summary>Label beside the checkbox that decides whether a member joins.</summary>
    public const string IncludeControlLabel = "Include";

    /// <summary>What answering the card does. Must be honest that the library
    /// still shows one entry per store until later stages land.</summary>
    public const string LinkEffect = "Entries still appear separately.";

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

    // ══ What answering reported ════════════════

    /// <summary>Report after a group was linked. <c>{0}</c> the title the
    /// library keeps, <c>{1}</c> how many titles joined it. Must not claim the
    /// library already looks different, because nothing reads a link yet.</summary>
    public const string LinkedReportFormat = "Linked {1} under {0}. Still shown separately.";

    /// <summary>Report when the answer included no member, so nothing was
    /// linked and the proposals were recorded as different games.</summary>
    public const string NothingLinked = "Nothing linked, recorded as different games.";

    /// <summary>Report after a link act was retracted.</summary>
    public const string Retracted = "Link retracted. Returns to review.";

    /// <summary>Report when the act had already been retracted, which is a
    /// no-op rather than an error.</summary>
    public const string RetractedAlready = "Already retracted.";

    // ══ History — link acts ══════════════════

    /// <summary>Section heading for the list of link acts. Display L weight,
    /// sentence case.</summary>
    public const string LinkHistoryHeading = "Linked games";

    /// <summary>Introduction under that heading.</summary>
    public const string LinkHistoryIntro = "Newest first. Retract any time.";

    /// <summary>Empty state for the link list. §7: a direction, not a mood.</summary>
    public const string LinkHistoryEmpty = "Groups you link appear here.";

    /// <summary>Row describing an act that linked one title. <c>{0}</c> the
    /// linked title, <c>{1}</c> the title the library keeps.</summary>
    public const string LinkRowFormat = "{0} linked under {1}";

    /// <summary>Row describing an act that linked several titles. <c>{0}</c>
    /// the linked titles, <c>{1}</c> the title the library keeps.</summary>
    public const string LinkRowManyFormat = "{0} linked under {1}";

    /// <summary>Small uppercase label before the date on a link row.</summary>
    public const string LinkedAtLabel = "LINKED";

    /// <summary>Small uppercase label marking a row that has been retracted.
    /// Not a control; purely informational.</summary>
    public const string RetractedLabel = "RETRACTED";

    /// <summary>Control that retracts a link act. Retraction is ordinary and
    /// repeatable, so this must not read as a last chance.</summary>
    public const string RetractButton = "Retract";

    /// <summary>Tooltip on the retract control.</summary>
    public const string RetractTooltip = "Proposals return to review.";

    // ══ Automation ══════════════════════

    /// <summary>One member for a screen reader. <c>{0}</c> title, <c>{1}</c>
    /// its store entry numbers, which is what tells two members with the same
    /// title apart. Used only when no ownership row names a store.</summary>
    public const string MemberAutomationFormat = "{0} {1}";

    /// <summary>
    /// One member for a screen reader when its stores are known. <c>{0}</c>
    /// title, <c>{1}</c> the comma-joined store names ("Steam", "Steam, GOG"),
    /// <c>{2}</c> the entry numbers. The store comes before the numbers because
    /// it is the fact that tells the Steam entry from the Epic entry when both
    /// are called Prey.
    /// </summary>
    public const string MemberWithStoreAutomationFormat = "{0} {1} {2}";

    /// <summary>Joins member labels in a list.</summary>
    public const string MemberSeparator = ", ";

    /// <summary>Automation name for a member's primary radio. <c>{0}</c> the
    /// member label. Names the member, never the verb.</summary>
    public const string PrimaryAutomationFormat = "Keep {0}";

    /// <summary>Automation name for a member's include checkbox. <c>{0}</c> the
    /// member label. Names the member, never the verb.</summary>
    public const string IncludeAutomationFormat = "Include {0}";

    /// <summary>Automation name for a retract control. <c>{0}</c> the row's
    /// description, so a column of controls is not one target.</summary>
    public const string RetractAutomationFormat = "Retract: {0}";
}
