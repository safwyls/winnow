---
id: TASK-64
title: >-
  Wire the merge executor to the Same Game screen so confirmed pairs actually
  apply
status: In Progress
assignee: []
created_date: '2026-09-01 03:09'
updated_date: '2026-09-01 04:31'
labels:
  - resolve
  - ui
dependencies:
  - TASK-62
priority: high
ordinal: 81000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
MergeExecutor and IMergeExecutionRepository are registered in Program.cs and resolved nowhere outside tests, so nothing in the running app has ever applied a merge. Confirmed pairs stay confirmed and unapplied, and merge_applications is empty. TASK-5 built the engine and recorded that wiring the screen was a later stage; no task covered it until now. The Same Game screen should offer applying a confirmed pair, showing the plan preview and any blocker before the user commits, and the batch path should apply outstanding confirmed pairs. Sequencing note: the undo journal from TASK-62 should land BEFORE or WITH this, so the first merge the user ever applies is already reversible.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The Same Game screen can apply a confirmed pair and reports what changed
- [ ] #2 The preview shows the surviving identity and any blocker before the user commits
- [ ] #3 Applying is refused with a visible reason when the plan reports a blocker
- [ ] #4 A merge applied through the UI writes an undo journal entry, so it is reversible from the moment the feature ships
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Plan: wire the executor to the Same Game screen (UI half, with TASK-62's UI half)

The two tasks are one screen, so this plan covers applying and TASK-62 step 6 together.

1. Core: MergeApplicationRecord gains SurvivingTitle and AbsorbedTitle (both nullable).
   The absorbed work row is deleted by the merge, so its name survives nowhere but the
   0017 journal's works-delete before_json. Without this the history row cannot say
   which two games became one, which is TASK-62 AC #1.
2. Data: MergeUndoRepository.LoadLogAsync gains two scalar sub-selects — the surviving
   work's current name, and json_extract(before_json, '$.name') from the journal's
   works/delete row. LogRow and ToRecord carry them. No behaviour change.
3. Resolve: MergeExecutor.OutstandingAsync() plans every confirmed-unapplied candidate
   in one pass. Keeps Winnow.App off IMergeExecutionRepository and off SQL (5.1).
4. App view models:
   - MergeCopy.cs, every user-facing string in one file, authored by docs-writer.
   - MergeApplyViewModel: one confirmed pair awaiting application. Surviving identity,
     mode (work only / work and release), blocker sentence, CanApply.
   - MergeHistoryRowViewModel: one applied merge. Two titles, when, the recomputed undo
     verdict, the four disabled sentences, the blocking application named, a counts
     disclosure, and the already-undone state that carries no control at all.
   - MergeQueueViewModel takes MergeExecutor as a REQUIRED dependency (omitting it is
     the failure this task exists to fix, so it must not degrade quietly). LoadAsync
     loads pending, outstanding and history in one pass; Apply, ApplyAll and Undo all
     reload, so the undo enabled state is recomputed every time and never cached.
   - The Same game button keeps its 7 label; the copy around it stops claiming the
     answer merges anything.
5. View: two new sections under the queue on the existing card idiom. Amber edge for
   attention, Azure disclose button for counts, no Flare anywhere, automation names on
   every control, nothing animates so reduced motion has nothing to suppress.
6. Program.cs: services.AddSingleton<IMergeUndoRepository, MergeUndoRepository>().
7. Tests: MergeApplyViewModelTests over a real migrated SQLite file and the real
   executor — preview renders identity and mode, a blocked plan refuses with its reason
   visible, applying reports what changed, the history list renders, each of the four
   disabled reasons maps to its copy, the later-merge case names the blocker, the
   enabled state changes between two loads when the underlying plan changes, undo
   reports success and the row moves to already-undone.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implementation landed 2026-08-31 (not finalized).

Program.cs now registers IMergeUndoRepository -> MergeUndoRepository, so MergeExecutor's
optional undo parameter is satisfied and the merge history is reachable.

MergeQueueViewModel takes MergeExecutor as a REQUIRED constructor parameter. Every existing
construction site was updated; tests/Winnow.Tests/TempDatabase.cs gained TestMergeExecutor,
which wires all three repositories the way Program does — including the undo repository,
whose omission a hand-built executor would swallow.

New on the screen, below the existing queue and on the same scroll:
  * Ready to apply — one card per confirmed-unapplied pair. The card is the preview: the
    sentence naming which identity survives and which is folded into it, the mode in plain
    language, and either a limitation (the collapse was held to the work layer, Amber-free
    inset note) or a refusal (Amber edge, Apply disabled). Per-pair Apply plus a batch
    Apply all with the outstanding count in the data face.
  * Applied merges — one entry per merge_applications row, newest first, reading as which
    two games became one and when. Per-row Undo, an Azure disclosure holding the per-table
    counts, and the four disabled reasons each written out beside the control.
  * A report note in the header carries what the last apply or undo actually did, written
    from the outcome the engine returned. A refused apply says nothing was changed and why.

The queue's own copy was corrected. The Same game / Different games labels are unchanged
(design-system 7 mandates them); what changed is the intro and the Same game tooltip, which
now say that answering records a decision and applying is a separate step below.

MergeExecutor gained OutstandingAsync(), a read-only wrapper that plans every confirmed
unapplied candidate, so Winnow.App never names IMergeExecutionRepository.

MergeApplicationRecord gained SurvivingTitle, AbsorbedTitle and Counts. The absorbed side's
name survives nowhere but the 0017 journal — the works row is deleted and for a collapse the
merge_candidates row that held both titles is cascaded away with the absorbed release — so
LoadLogAsync reads it out of the journal's works/delete before_json, with a releases/delete
fallback for a collapse of two entries that already shared a work.

Tests: tests/Winnow.Tests/MergeApplyViewModelTests.cs (16) and MergeScreenRegistrationTests.cs
(3). Full suite over all three projects: 2449 + 98 + 70 = 2617 passed, 0 failed, 0 warnings.
<!-- SECTION:NOTES:END -->
