namespace Winnow.App.ViewModels;

/// <summary>
/// User-facing copy for the "Same game" screen. All strings in one file so the
/// three-part flow (queue, apply, history) and every honesty caveat can be
/// reviewed together. The screen asks one question at a time and must never let
/// a button's label overstate what it does.
/// </summary>
public static class MergeCopy
{
    // ══ Queue — the correction ════════════════════════════════════════════

    /// <summary>Replaces the original intro under the screen title. States
    /// the three-part contract: the queue asks a question, answering records
    /// the answer, and applying is a separate step below. One compound
    /// sentence keeps the screen from needing a paragraph.</summary>
    public const string QueueIntro =
        "These pairs might be the same game. Answering records your decision "
        + "here; applying it to the library is a separate step further down.";

    /// <summary>Tooltip on the "Same game" button. The label itself is
    /// mandated by the copy table (design-system.md section 7) and stays put,
    /// so the tooltip is where the distinction between recording an answer
    /// and carrying it out lives. Names the keyboard shortcut.</summary>
    public const string SameGameTooltip =
        "Same game (S) — records your answer. The merge is applied "
        + "separately in the section below.";

    /// <summary>Tooltip on "Different games". States the shortcut, the
    /// permanence, and the consequence. Same register as the existing AXAML
    /// tooltip it replaces.</summary>
    public const string DifferentGamesTooltip =
        "Different games (D) — permanent; this pair is never queued again";

    // ══ Applying ══════════════════════════════════════════════════════════

    /// <summary>Section heading for the confirmed-but-unapplied pairs.
    /// Display L weight, sentence case. A short noun phrase naming the
    /// section's purpose.</summary>
    public const string ApplyHeading = "Ready to apply";

    /// <summary>Introduction under the apply heading. States that these are
    /// answers already given, waiting to be carried out, and that each pair
    /// previews its effect before anything is written.</summary>
    public const string ApplyIntro =
        "These pairs have been answered but not yet applied. Each one shows "
        + "what it will do before you confirm it.";

    /// <summary>Empty state for the apply section. A direction: tells the
    /// user where confirmed pairs come from.</summary>
    public const string ApplyEmpty =
        "No confirmed pairs are waiting. Answer a pair above and it appears here.";

    /// <summary>Small uppercase label above a single pair's preview inside
    /// the card. Names the preview's purpose.</summary>
    public const string ApplySectionLabel = "EFFECT";

    /// <summary>Per-pair apply control. Applies this one pair only. The
    /// verb is accurate because the button does write to the database, but
    /// it must not promise more than one pair.</summary>
    public const string ApplyButton = "Apply this pair";

    /// <summary>Batch apply control. The count is rendered separately beside
    /// it in the data face, so the label carries no number.</summary>
    public const string ApplyAllButton = "Apply all";

    /// <summary>Tooltip on the batch control. States that every listed pair
    /// is applied in its own transaction and that a pair that cannot be
    /// applied safely is skipped rather than blocking the rest.</summary>
    public const string ApplyAllTooltip =
        "Applies every pair listed here, one transaction each. A pair that "
        + "cannot be applied safely is skipped, not held back.";

    // ══ The preview ═══════════════════════════════════════════════════════

    /// <summary>Format string naming the surviving identity. <c>{0}</c> is
    /// the surviving title, <c>{1}</c> is the absorbed title. The sentence
    /// makes the user commit to which identity survives and which is folded
    /// into it.</summary>
    public const string SurvivorLineFormat =
        "{1} will be folded into {0}.";

    /// <summary>Same job as <see cref="SurvivorLineFormat"/> when the
    /// absorbed side has no title on record. <c>{0}</c> is the surviving
    /// title, <c>{1}</c> is a release id already formatted as e.g.
    /// "release 412".</summary>
    public const string SurvivorLineUnnamedFormat =
        "An untitled entry, {1}, will be folded into {0}.";

    /// <summary>Plain-language name of
    /// <see cref="Core.Merging.MergeMode.WorkOnly"/>: the two games become
    /// one, but the two store entries stay as separate rows under it.
    /// Sentence fragment, not a full sentence.</summary>
    public const string ModeWorkOnly =
        "Two games become one, but the two store entries stay as separate rows";

    /// <summary>Plain-language name of
    /// <see cref="Core.Merging.MergeMode.ReleaseCollapse"/>: the two become
    /// one game and one entry. Sentence fragment.</summary>
    public const string ModeReleaseCollapse =
        "Two games become one game and one entry";

    /// <summary>Small uppercase label beside the mode text.</summary>
    public const string ModeLabel = "MODE";

    // ══ Why a collapse was limited or refused ═════════════════════════════

    /// <summary>Explains why a release collapse was limited to a work-only
    /// merge: the two sides are different editions. Reads as a reason, not a
    /// refusal, because the merge still does something.</summary>
    public const string LimitedDistinctEditions =
        "These are different editions, so the two store entries stay as "
        + "separate rows under one game.";

    /// <summary>Refuses the merge entirely when the two sides are already
    /// under one game. There is nothing left to do.</summary>
    public const string RefusedDistinctEditions =
        "These are different editions and already share one game. There is "
        + "nothing left to merge.";

    /// <summary>Explains why a release collapse was limited: both sides
    /// carry achievements, and the achievements table has no store column,
    /// so two stores' achievement sets under one entry could not be told
    /// apart afterwards.</summary>
    public const string LimitedAchievementsOnBothSides =
        "Both sides have achievements, and collapsing them into one entry "
        + "would mix achievement sets that belong to different stores. The "
        + "two entries stay separate.";

    /// <summary>Refuses when both sides carry achievements and no work-only
    /// fallback is available.</summary>
    public const string RefusedAchievementsOnBothSides =
        "Both sides have achievements that belong to different stores. "
        + "Collapsing would mix them, and there is no way to keep them "
        + "apart afterwards.";

    /// <summary>Explains why a release collapse was limited: the two sides
    /// recorded different facts about the same update at the same moment.
    /// Collapsing would drop one, and losing a fact is worse than not
    /// collapsing.</summary>
    public const string LimitedConflictingUpdateEvents =
        "The two sides recorded different facts about the same update at "
        + "the same time. Collapsing would drop one, so the two entries "
        + "stay separate.";

    /// <summary>Refuses when conflicting update events block any merge at
    /// all.</summary>
    public const string RefusedConflictingUpdateEvents =
        "The two sides recorded different facts about the same update at "
        + "the same time. Merging would drop one, and losing a fact is "
        + "worse than not merging.";

    /// <summary>The two sides already share one game; nothing left to
    /// do.</summary>
    public const string RefusedAlreadyApplied =
        "These two entries already share one game. There is nothing left "
        + "to merge.";

    /// <summary>The pair has not been answered "Same game", so applying is
    /// not permitted.</summary>
    public const string RefusedCandidateNotConfirmed =
        "This pair has not been confirmed as the same game.";

    /// <summary>The pair is no longer on record.</summary>
    public const string RefusedCandidateNotFound =
        "This pair is no longer on record.";

    /// <summary>Small uppercase label above a refusal sentence.</summary>
    public const string BlockedLabel = "BLOCKED";

    // ══ What applying reported ════════════════════════════════════════════

    /// <summary>Past-tense report of a successful apply. <c>{0}</c>
    /// surviving title, <c>{1}</c> absorbed title, <c>{2}</c> an
    /// already-formatted phrase naming the mode outcome.</summary>
    public const string AppliedReportFormat =
        "{1} was folded into {0}: {2}.";

    /// <summary>Report when applying was refused and nothing was written.
    /// <c>{0}</c> is the refusal sentence.</summary>
    public const string AppliedNothingFormat =
        "Nothing was changed. {0}";

    /// <summary>Batch report. <c>{0}</c> applied count, <c>{1}</c>
    /// considered count, <c>{2}</c> skipped count. Numbers are plain
    /// placeholders rendered inline.</summary>
    public const string AppliedBatchFormat =
        "{0} of {1} pairs applied, {2} skipped.";

    /// <summary>Batch report when nothing could be applied. <c>{0}</c>
    /// considered count.</summary>
    public const string AppliedBatchNoneFormat =
        "None of the {0} pairs could be applied.";

    // ══ History ═══════════════════════════════════════════════════════════

    /// <summary>Section heading for the history of applied merges. Display
    /// L weight, sentence case. A short noun phrase.</summary>
    public const string HistoryHeading = "Applied merges";

    /// <summary>Introduction under the history heading. States what this
    /// list is and that reversibility is checked live, not cached from the
    /// time the merge was made.</summary>
    public const string HistoryIntro =
        "Every merge that has been applied, newest first. Reversibility is "
        + "checked now, not at the time the merge was made.";

    /// <summary>Empty state for the history section. A direction.</summary>
    public const string HistoryEmpty =
        "No merge has been applied yet. Confirmed pairs are applied from "
        + "the section above.";

    /// <summary>History row describing which two games became one. <c>{0}</c>
    /// absorbed title, <c>{1}</c> surviving title. Short, this is a row, not
    /// a paragraph.</summary>
    public const string HistoryRowFormat =
        "{0} folded into {1}";

    /// <summary>History row when the absorbed title was never journalled
    /// (a merge that predates undo support). <c>{0}</c> surviving title.
    /// Names the survivor and says the other side's name is not on
    /// record.</summary>
    public const string HistoryRowUnnamedFormat =
        "Something was folded into {0}; the absorbed game's name is not on record";

    /// <summary>Small uppercase label before the date on a history
    /// row.</summary>
    public const string AppliedAtLabel = "APPLIED";

    /// <summary>Small uppercase label marking a row that has been undone.
    /// Not a control; purely informational.</summary>
    public const string UndoneLabel = "UNDONE";

    // ══ The counts disclosure ═════════════════════════════════════════════

    /// <summary>Label for the Azure disclosure toggle when collapsed: opens
    /// the per-table counts.</summary>
    public const string CountsShow = "Show row counts";

    /// <summary>Label for the Azure disclosure toggle when expanded: closes
    /// the per-table counts.</summary>
    public const string CountsHide = "Hide row counts";

    /// <summary>Introduction for the counts panel. States that these are a
    /// record of what moved.</summary>
    public const string CountsIntro =
        "How many rows moved when this merge was applied.";

    /// <summary>Shown when this merge recorded no row counts (applied before
    /// the journal was introduced).</summary>
    public const string CountsUnavailable =
        "This merge did not record row counts.";

    // ══ Per-table count labels ═══════════════════════════════════════════

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

    /// <summary>Per-row undo control.</summary>
    public const string UndoButton = "Undo";

    /// <summary>Tooltip on the undo control. States the all-or-nothing
    /// guarantee.</summary>
    public const string UndoTooltip =
        "Reversal is complete or it does not happen. There is no partial undo.";

    /// <summary>Disabled reason: a later merge consumed one of this merge's
    /// identities, and undoing it first is the way through. <c>{0}</c> is
    /// the blocking merge's own row description, already formatted as e.g.
    /// "Prey became Prey (2 Sep 2026)". This is the only disabled reason
    /// with an action the user can take.</summary>
    public const string UndoBlockedLaterMergeFormat =
        "A later merge ({0}) used one of the identities this merge created. "
        + "Undo that merge first.";

    /// <summary>Same reason as <see cref="UndoBlockedLaterMergeFormat"/>
    /// when the blocking merge cannot be named. <c>{0}</c> is a merge
    /// number.</summary>
    public const string UndoBlockedLaterMergeUnnamedFormat =
        "A later merge (#{0}) used one of the identities this merge created. "
        + "Undo that merge first.";

    /// <summary>Control that jumps to or undoes the blocking merge first.
    /// Must not claim to undo the row it sits on.</summary>
    public const string UndoBlockingButton = "Undo that merge";

    /// <summary>Disabled reason: this merge was applied by a build that
    /// recorded nothing about which rows moved, so reversal is
    /// impossible.</summary>
    public const string UndoBlockedPredatesUndoSupport =
        "This merge was applied by a build that did not record what it "
        + "moved, so there is nothing to reverse from.";

    /// <summary>Disabled reason: a game this merge touched is no longer in
    /// the library, so there is nothing left to move back.</summary>
    public const string UndoBlockedGameNoLongerExists =
        "A game this merge touched is no longer in the library. There is "
        + "nothing left to move back.";

    /// <summary>Informational label: this merge has already been undone.
    /// The row carries no control.</summary>
    public const string UndoBlockedAlreadyUndone =
        "This merge has already been undone.";

    /// <summary>Past-tense report of a successful undo. <c>{0}</c> restored
    /// title, <c>{1}</c> row count restored.</summary>
    public const string UndoneReportFormat =
        "{0} was restored. {1} rows moved back.";

    /// <summary>Report when an undo was refused. <c>{0}</c> the reason.
    /// Nothing was written.</summary>
    public const string UndoRefusedFormat =
        "Nothing was changed. {0}";
}
