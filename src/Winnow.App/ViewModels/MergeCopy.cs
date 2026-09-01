namespace Winnow.App.ViewModels;

/// <summary>
/// User-facing copy for the Same Game screen. All strings in one file so the
/// review queue, its inline previews, the outcome reports and the history can
/// be reviewed together. Confirming a pair merges it immediately; every label
/// must be exact about what pressing it does.
/// </summary>
public static class MergeCopy
{
    // ══ Chrome ════════════════════════════════════════════════════════════

    /// <summary>Small uppercase label at the left of the 48px header,
    /// beside the segment control.</summary>
    public const string ScreenLabel = "SAME GAME";

    /// <summary>Uppercase segment label for the review queue.</summary>
    public const string SegmentReview = "REVIEW";

    /// <summary>Uppercase segment label for the history of applied
    /// merges.</summary>
    public const string SegmentHistory = "HISTORY";

    /// <summary>Tooltip on the Review segment.</summary>
    public const string SegmentReviewTooltip =
        "Pairs waiting for an answer";

    /// <summary>Tooltip on the History segment.</summary>
    public const string SegmentHistoryTooltip =
        "Applied merges and undo";

    // ══ Review — the queue ════════════════════════════════════════════════

    /// <summary>Standing introduction under the screen title. Must
    /// contain "undone" and must not contain "separate step".</summary>
    public const string QueueIntro =
        "Confirming merges immediately. Merges can be undone.";

    /// <summary>Tooltip on Same game when mergeable. Must contain
    /// "merges".</summary>
    public const string SameGameTooltip =
        "Same game (S) — merges now, undo available";

    /// <summary>Tooltip on Different games. States the shortcut, the
    /// permanence, and the consequence.</summary>
    public const string DifferentGamesTooltip =
        "Different games (D) — permanent; this pair is never queued again";

    // ══ The inline preview ════════════════════════════════════════════════

    /// <summary>Small uppercase label above the outcome block on a
    /// mergeable pair's card.</summary>
    public const string OutcomeLabel = "OUTCOME";

    /// <summary>Which identity the library keeps. <c>{0}</c> surviving
    /// title, <c>{1}</c> absorbed title.</summary>
    public const string PreviewSurvivorFormat =
        "Keeps {0}. {1} folds in.";

    /// <summary>Survivor line when the absorbed side has no title.
    /// <c>{0}</c> surviving title, <c>{1}</c> release label.</summary>
    public const string PreviewSurvivorUnnamedFormat =
        "Keeps {0}. {1} folds in.";

    /// <summary>The two entries collapse into one row.</summary>
    public const string PreviewCollapse =
        "Entries merge into one.";

    /// <summary>Entries stay separate: different editions.</summary>
    public const string PreviewWorkOnlyDistinctEditions =
        "Separate entries, different editions.";

    /// <summary>Entries stay separate: achievements from different
    /// stores.</summary>
    public const string PreviewWorkOnlyAchievements =
        "Separate entries, achievements would mix.";

    /// <summary>Entries stay separate: conflicting update facts.</summary>
    public const string PreviewWorkOnlyUpdateEvents =
        "Separate entries, conflicting update records.";

    /// <summary>Fallback: entries stay separate, no specific reason.</summary>
    public const string PreviewWorkOnlyOther =
        "Separate entries under one game.";

    // ══ The blocked pair ══════════════════════════════════════════════════

    /// <summary>Small uppercase label above a blocked-pair message or a
    /// refusal sentence.</summary>
    public const string BlockedLabel = "BLOCKED";

    /// <summary>The two entries already share one game.</summary>
    public const string PreviewBlockedAlreadyOneGame =
        "Already one game. Nothing to merge.";

    /// <summary>The pair no longer exists.</summary>
    public const string PreviewBlockedNotFound =
        "Pair no longer on record.";

    /// <summary>What the answer does on a blocked card: files the
    /// decision, changes nothing.</summary>
    public const string PreviewBlockedAnswerEffect =
        "Closes the question. Nothing changes.";

    /// <summary>Tooltip on Same game for a blocked pair. Must not
    /// contain "merges".</summary>
    public const string SameGameBlockedTooltip =
        "Same game (S) — records answer, nothing written";

    // ══ Merge modes and limits ════════════════════════════════════════════

    /// <summary>Survivor line in the leftovers section. <c>{0}</c>
    /// surviving title, <c>{1}</c> absorbed title.</summary>
    public const string SurvivorLineFormat =
        "{1} folds into {0}.";

    /// <summary>Survivor line when the absorbed side has no title.
    /// <c>{0}</c> surviving title, <c>{1}</c> release label.</summary>
    public const string SurvivorLineUnnamedFormat =
        "{1} folds into {0}.";

    /// <summary>Mode name for work-only. Sentence fragment, also the
    /// <c>{2}</c> placeholder in <see cref="AppliedReportFormat"/>.</summary>
    public const string ModeWorkOnly =
        "One game, entries stay separate";

    /// <summary>Mode name for release collapse. Sentence fragment, also
    /// the <c>{2}</c> placeholder in <see cref="AppliedReportFormat"/>.</summary>
    public const string ModeReleaseCollapse =
        "One game, one entry";

    /// <summary>Small uppercase label beside the mode text.</summary>
    public const string ModeLabel = "MODE";

    /// <summary>Collapse limited: different editions.</summary>
    public const string LimitedDistinctEditions =
        "Different editions, entries stay separate.";

    /// <summary>Collapse limited: achievements from different stores
    /// would mix.</summary>
    public const string LimitedAchievementsOnBothSides =
        "Both sides have achievements; collapsing would mix them.";

    /// <summary>Collapse limited: conflicting update facts.</summary>
    public const string LimitedConflictingUpdateEvents =
        "Conflicting update records; collapsing would lose one.";

    /// <summary>Refused: already one game, different editions.</summary>
    public const string RefusedDistinctEditions =
        "Different editions, already one game.";

    /// <summary>Refused: achievements from different stores.</summary>
    public const string RefusedAchievementsOnBothSides =
        "Both sides have achievements; collapsing would mix them.";

    /// <summary>Refused: conflicting update facts.</summary>
    public const string RefusedConflictingUpdateEvents =
        "Conflicting update records; merging would lose one.";

    /// <summary>Already one game.</summary>
    public const string RefusedAlreadyApplied =
        "Already one game. Nothing to merge.";

    /// <summary>Not yet confirmed.</summary>
    public const string RefusedCandidateNotConfirmed =
        "Pair not confirmed.";

    /// <summary>Pair no longer exists.</summary>
    public const string RefusedCandidateNotFound =
        "Pair no longer on record.";

    // ══ What answering reported ═══════════════════════════════════════════

    /// <summary>Past-tense report after a successful merge triggered by
    /// confirming a pair. <c>{0}</c> surviving title, <c>{1}</c>
    /// absorbed title, <c>{2}</c> an already-formatted sentence fragment
    /// naming the mode outcome (see <see cref="ModeWorkOnly"/> and
    /// <see cref="ModeReleaseCollapse"/>). The fragment begins with a
    /// capital and reads as its own sentence after the period.</summary>
    public const string AppliedReportFormat =
        "Merged {1} into {0}. {2}.";

    /// <summary>Report when a merge was refused and nothing was written.
    /// <c>{0}</c> is the refusal sentence.</summary>
    public const string AppliedNothingFormat =
        "Nothing was changed. {0}";

    /// <summary>Control beside the outcome report. Undoes the merge that
    /// the report describes. Must not read as a generic undo that could
    /// apply to anything else on the screen.</summary>
    public const string ReportUndoButton = "Undo this merge";

    /// <summary>Tooltip on the report's undo control.</summary>
    public const string ReportUndoTooltip =
        "Complete reversal or nothing.";

    // ══ History — pending from previous version ═══════════════════════════

    /// <summary>Section heading for pairs confirmed under the previous
    /// two-step flow where answering and applying were separate. Display
    /// L weight, sentence case.</summary>
    public const string ApplyHeading = "Answered, not yet applied";

    /// <summary>Introduction under the apply heading. These pairs
    /// predate the immediate-merge flow.</summary>
    public const string ApplyIntro =
        "Confirmed before merges applied on answer. Not yet written.";

    /// <summary>Small uppercase label above a single pair's effect
    /// preview in the pending-from-previous-version section.</summary>
    public const string ApplySectionLabel = "EFFECT";

    /// <summary>Per-pair apply control. Applies this one pair only.
    /// Must not promise more than one pair.</summary>
    public const string ApplyButton = "Apply this pair";

    /// <summary>Batch apply control. The count is rendered separately
    /// beside it in the data face, so the label carries no
    /// number.</summary>
    public const string ApplyAllButton = "Apply all";

    /// <summary>Tooltip on the batch apply control.</summary>
    public const string ApplyAllTooltip =
        "One transaction each. Unsafe pairs are skipped.";

    /// <summary>Batch report. <c>{0}</c> applied count, <c>{1}</c>
    /// considered count, <c>{2}</c> skipped count. Numbers are plain
    /// placeholders rendered inline.</summary>
    public const string AppliedBatchFormat =
        "{0} of {1} pairs applied, {2} skipped.";

    /// <summary>Batch report when nothing could be applied. <c>{0}</c>
    /// considered count.</summary>
    public const string AppliedBatchNoneFormat =
        "None of the {0} pairs could be applied.";

    // ══ History — applied merges ══════════════════════════════════════════

    /// <summary>Section heading for the list of applied merges. Display
    /// L weight, sentence case.</summary>
    public const string HistoryHeading = "Applied merges";

    /// <summary>Introduction under the history heading.</summary>
    public const string HistoryIntro =
        "Newest first. Reversibility checked live.";

    /// <summary>Empty state for the applied-merge list. §7: empty states
    /// are directions, not moods.</summary>
    public const string HistoryEmpty =
        "No merges yet. Confirm a pair in Review.";

    /// <summary>History row describing which two games became one.
    /// <c>{0}</c> absorbed title, <c>{1}</c> surviving title. Short,
    /// this is a row, not a paragraph.</summary>
    public const string HistoryRowFormat =
        "{0} folded into {1}";

    /// <summary>History row when the absorbed title was not journalled.
    /// <c>{0}</c> surviving title.</summary>
    public const string HistoryRowUnnamedFormat =
        "Unknown folded into {0}";

    /// <summary>Small uppercase label before the date on a history
    /// row.</summary>
    public const string AppliedAtLabel = "APPLIED";

    /// <summary>Small uppercase label marking a row that has been undone.
    /// Not a control; purely informational.</summary>
    public const string UndoneLabel = "UNDONE";

    // ══ The counts disclosure ═════════════════════════════════════════════

    /// <summary>Label for the disclosure toggle when collapsed: opens
    /// the per-table counts.</summary>
    public const string CountsShow = "Show row counts";

    /// <summary>Label for the disclosure toggle when expanded: closes
    /// the per-table counts.</summary>
    public const string CountsHide = "Hide row counts";

    /// <summary>Introduction for the counts panel.</summary>
    public const string CountsIntro =
        "Rows moved by this merge.";

    /// <summary>No counts recorded for this merge.</summary>
    public const string CountsUnavailable =
        "No row counts recorded.";

    // ══ Per-table count labels ════════════════════════════════════════════

    /// <summary>Label for the count of store entries moved from the
    /// absorbed game to the surviving one.</summary>
    public const string CountReleases = "Store entries";

    /// <summary>Label for the count of store identifiers (a Steam appid,
    /// a GOG id) moved.</summary>
    public const string CountExternalIds = "Store identifiers";

    /// <summary>Label for the count of ownership records moved.</summary>
    public const string CountOwnerships = "Ownership records";

    /// <summary>Label for the count of ownerships that could not simply
    /// move because the surviving side already had one for the same store;
    /// the two were combined into one.</summary>
    public const string CountOwnershipsFolded = "Ownerships combined";

    /// <summary>Label for the count of per-account playtime and
    /// last-played records moved.</summary>
    public const string CountOwnershipAccounts = "Account records";

    /// <summary>Label for the count of individual recorded playtime
    /// observations moved.</summary>
    public const string CountPlayRecords = "Play observations";

    /// <summary>Label for the count of Winnow's own periodic playtime
    /// readings moved. These form the longitudinal series.</summary>
    public const string CountPlaytimeSnapshots = "Playtime snapshots";

    /// <summary>Label for the count of individual sittings from the
    /// process watcher moved.</summary>
    public const string CountSessions = "Sessions";

    /// <summary>Label for the count of patches and announcements
    /// moved.</summary>
    public const string CountUpdateEvents = "Updates";

    /// <summary>Label for the count of the user's "I've seen this patch"
    /// marks moved.</summary>
    public const string CountUpdateAcknowledgements = "Update acknowledgements";

    /// <summary>Label for the count of memberships in the user's
    /// hand-built lists moved.</summary>
    public const string CountListItems = "List memberships";

    /// <summary>Label for the count of genre, theme and mode tags on the
    /// game moved.</summary>
    public const string CountWorkFacets = "Game tags";

    /// <summary>Label for the count of genre, theme and mode tags on the
    /// store entry moved.</summary>
    public const string CountReleaseFacets = "Entry tags";

    /// <summary>Label for the count of the user's answers to feed
    /// suggestions moved.</summary>
    public const string CountFeedVerdicts = "Feed answers";

    /// <summary>Label for the count of records that the feed showed this
    /// game on a given shelf, moved.</summary>
    public const string CountFeedSurfacings = "Feed appearances";

    /// <summary>Label for the count of other proposed same-game pairs that
    /// named one of the two merged entries, repointed.</summary>
    public const string CountMergeCandidates = "Related merge candidates";

    /// <summary>Label for the count of achievements. Structurally always
    /// zero: achievements never move, and a merge with achievements on
    /// both sides is refused. Listed for completeness; not rendered when
    /// zero.</summary>
    public const string CountAchievements = "Achievements";

    /// <summary>Label for the count of achievement unlocks. Also
    /// structurally always zero, for the same reason as
    /// <see cref="CountAchievements"/>.</summary>
    public const string CountAchievementUnlocks = "Achievement unlocks";

    /// <summary>Label for the count of rows removed because a
    /// byte-identical row already existed on the surviving side. This is
    /// deduplication across every table above, not data loss.</summary>
    public const string CountDuplicateRowsDropped = "Deduplicated rows";

    // ══ Undo ══════════════════════════════════════════════════════════════

    /// <summary>Per-row undo control in the history list.</summary>
    public const string UndoButton = "Undo";

    /// <summary>Tooltip on the per-row undo control.</summary>
    public const string UndoTooltip =
        "Complete reversal or nothing.";

    /// <summary>Blocked: a later merge depends on this one. <c>{0}</c>
    /// is the blocking merge's row description.</summary>
    public const string UndoBlockedLaterMergeFormat =
        "Blocked by a later merge ({0}). Undo it first.";

    /// <summary>Blocked by a later unnamed merge. <c>{0}</c> merge
    /// number.</summary>
    public const string UndoBlockedLaterMergeUnnamedFormat =
        "Blocked by a later merge (#{0}). Undo it first.";

    /// <summary>Control that jumps to or undoes the blocking merge first.
    /// Must not claim to undo the row it sits on.</summary>
    public const string UndoBlockingButton = "Undo that merge";

    /// <summary>No undo journal recorded for this merge.</summary>
    public const string UndoBlockedPredatesUndoSupport =
        "Predates undo support. No record to reverse from.";

    /// <summary>Game no longer in the library.</summary>
    public const string UndoBlockedGameNoLongerExists =
        "Game no longer in library. Nothing to restore.";

    /// <summary>Already reversed.</summary>
    public const string UndoBlockedAlreadyUndone =
        "Already undone.";

    /// <summary>Undo report. <c>{0}</c> restored title, <c>{1}</c> row
    /// count. Must state the pair will not return to the queue; undo
    /// sets the status to terminal, and nothing else on screen signals
    /// that.</summary>
    public const string UndoneReportFormat =
        "{0} restored, {1} rows moved back. The pair will not return to review.";

    /// <summary>Report when an undo was refused. <c>{0}</c> the reason.
    /// Nothing was written.</summary>
    public const string UndoRefusedFormat =
        "Nothing was changed. {0}";

    // ══ Automation ════════════════════════════════════════════════════════

    /// <summary>Report when the merge threw and rolled back. Shown in
    /// the Amber block.</summary>
    public const string AppliedFailed =
        "Merge did not complete. Nothing was written.";

    /// <summary>Notice under the empty review state when pairs answered
    /// under the old flow wait behind History. <c>{0}</c> count.</summary>
    public const string OutstandingNoticeFormat =
        "{0} answered pairs not yet applied in History.";

    /// <summary>Blocked-card sentence for a blocker that is not "already
    /// one game": a pair answered out from under the screen, for instance.
    /// Asserts nothing about the library's state.</summary>
    public const string PreviewBlockedNothingToDo =
        "Nothing to do here.";

    /// <summary>One side of a pair for a screen reader. <c>{0}</c> title,
    /// <c>{1}</c> release number.</summary>
    public const string PairAutomationFormat = "{0} {1}";

    /// <summary>Automation name for Same game. <c>{0}</c> left side
    /// composed by <see cref="PairAutomationFormat"/>, <c>{1}</c> right
    /// side, <c>{2}</c> the outcome sentence (empty when no plan has
    /// arrived). Must read correctly with <c>{2}</c> empty.</summary>
    public const string SameGameAutomationFormat =
        "Same game: {0} and {1}. {2}";

    /// <summary>Automation name for Different games. <c>{0}</c> left
    /// side, <c>{1}</c> right side.</summary>
    public const string DifferentGamesAutomationFormat =
        "Different games: {0} and {1}";
}
