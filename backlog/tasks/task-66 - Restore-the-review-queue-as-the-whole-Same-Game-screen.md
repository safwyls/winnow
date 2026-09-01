---
id: TASK-66
title: Restore the review queue as the whole Same Game screen
status: In Progress
assignee:
  - '@safwyl'
created_date: '2026-09-01 15:43'
updated_date: '2026-09-01 20:47'
labels: []
dependencies: []
ordinal: 83000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
TASK-64 added apply and history to the Same Game screen exactly as briefed, and the brief was wrong. Nothing was deleted (341 lines added, 21 removed; the two-cover card and its 200x300 capsules are intact), but the review queue stopped being the whole screen and became the top third of one scroll carrying two more list sections. A focused decision surface became a page of three lists, and not being a list was the thing that made it good.

The correction, decided by the user and binding:

1. Folding applying into the answer. There is no separate "Ready to apply" section.
2. Confirming applies immediately: answering "Same game" merges the pair then and there. Undo is what makes that safe, and undo now exists (TASK-62).
3. History and undo move to their own surface rather than stacking below the queue.

The queue is the whole screen again: two covers side by side at 200x300, the signal diff, the two answers, all pending pairs on one scroll, no Flare. Each card previews inline and tersely what its answer will DO - which identity survives, and whether the two store entries collapse to one or stay two under one game - because there is no second step to catch a wrong outcome. A pair whose plan is blocked shows the block before the answer, never after.

History (the four disabled reasons, the named blocking merge with its undo-that-one-first action, the per-table counts disclosure, the recompute-on-every-load invariant) keeps everything it has, on its own surface reached through a segmented control in the app grammar the settings surface already established.

Users upgrading across this change may hold pairs answered "Same game" under the old two-step flow that were never applied. They need a home that is not the queue.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The review queue is the primary surface of the Same Game screen: the pending pairs are the only list on it, the two-cover 200x300 card and its signal diff are intact, and no Flare appears anywhere on the screen
- [ ] #2 Each pending card previews its outcome inline before the answer, naming the surviving identity and whether the two store entries collapse to one or stay two under one game
- [ ] #3 Answering "Same game" applies the merge immediately and reports what actually happened, with undo reachable from that report
- [ ] #4 A pair whose plan is blocked states the block above the answer, and answering it does exactly what the card says it will do
- [ ] #5 "Different games" still records a permanent rejection and applies nothing
- [ ] #6 History lives on its own surface and retains all four disabled reasons, the named blocking merge with its undo-that-merge-first action, the per-table counts disclosure, and the recompute-on-every-load invariant
- [ ] #7 Pairs confirmed but never applied under the previous two-step flow are reachable and applicable from a surface that is not the review queue
- [ ] #8 MergeQueueViewModel names no Winnow.Data or Winnow.Ingest type; the build is clean under TreatWarningsAsErrors and the full test suite passes
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Data: add a read-only prospective planning path. IMergeExecutionRepository gains PreviewAsync(MergeRequest), which admits a pending OR confirmed row; PlanAsync and ApplyAsync keep their literal confirmed-only statement untouched, so the never-auto-merge predicate still lives in SQL on the write path. MergeExecutor.PreviewAsync repoints onto it (it had no caller and could only ever answer CandidateNotConfirmed for a pending pair).
2. View model: MergeCandidateViewModel gains an inline Preview (MergePreviewViewModel) built from the prospective plan - survivor line, mode line, blocked flag and block sentence. MergeQueueViewModel.LoadAsync plans every pending pair once and hands each card its preview.
3. SameGameAsync becomes write-then-apply: SetStatus(confirmed), ApplyAsync, report from the outcome the engine returned. Report carries an undo target when the applied merge is reversible, verified by asking for its undo plan rather than assuming. DifferentGamesAsync is unchanged. Remaining cards re-plan in place after an apply so no card keeps a promise the merge just invalidated; the cursor stays on the pair that took the answered one place.
4. View: MergeQueueView becomes a 48px header with SAME GAME plus a REVIEW / HISTORY segmented control in the settings surface Button.seg.tab grammar, over a Panel of two surfaces. Review is the queue and nothing else: header, report note, the cards. Each card gains an OUTCOME block between the signal diff and the two answers.
5. History surface: the applied-merge list moves there whole (four disabled reasons, blocking-merge action, counts disclosure), and the confirmed-but-unapplied backlog from the old two-step flow sits above it in a section that only exists while that count is non-zero, with its per-pair preview cards and the batch apply. History recomputes on every screen load and on every switch to the surface.
6. Copy: every user-facing string is authored by the docs-writer subagent, briefed that answering now writes to the library.
7. Tests: update MergeQueueViewModelTests and MergeApplyViewModelTests in place, plus new coverage for the inline preview, the applies-immediately path, the blocked-pair behaviour, the surface switch and the legacy backlog. Scoped run, then the full suite, all through --artifacts-path into the scratchpad because the users app holds src/Winnow.App/bin.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implementation landed; prose and conformance review outstanding.

Data: IMergeExecutionRepository.PreviewAsync added as a read-only prospective plan. MergeExecutionRepository.BuildPlanAsync now picks between two literal SQL constants (ConfirmedPairSql, ProspectivePairSql) on an admitPending flag defaulted to false; both write-path call sites take the default, so the never-auto-merge predicate still lives in SQL. MergeExecutor.PreviewAsync repointed onto it - before this it could only ever answer CandidateNotConfirmed for a pending pair and had no caller.

View models: new MergePreviewViewModel (survivor line, effect line, blocked flag, tooltips, automation name). MergeCandidateViewModel gains Preview/HasPreview/IsPreviewBlocked and the Different games automation name. MergeQueueViewModel gains IsHistoryVisible with ShowReview/ShowHistory commands, SameGameAsync now writes confirmed then applies and reports from the outcome the engine returned, UndoReport arms only when the engine says the merge is reversible, and RefreshPreviewsAsync restates every remaining card after any write.

View: MergeQueueView is a 48px SAME GAME header with a REVIEW / HISTORY segmented control over two surfaces. Review is the queue alone with the outcome block between the signal diff and the answers. History carries the leftover confirmed-unapplied section above the applied-merge list.

Blocked-pair decision: both answers stay enabled and the card states the block above them. For a pending pair, NothingToDo means the two releases already share a work and a collapse blocker forbids collapsing the rows, so nothing is left to merge. Disabling the answers would strand the pair forever and push the user toward Different games, which would record a false rejection.

Leftover confirmed-unapplied pairs live on the history surface above the applied list, hidden entirely when the count is zero.

Full suite green through --artifacts-path: 2460 + 98 + 70 passed, 0 failed.

Conformance review returned eleven findings; addressing F1, F2, F4, F3, F10, then F5-F9, then F11. Confirmed sound by the review: the never-auto-merge SQL split, module boundaries, Flare absence, design-system conformance.

Fix round complete. F1 wraps ApplyAsync in catch (InvalidOperationException), reports into the Amber block, releases the IsDecided latch, and refreshes the applied lists because a rollback is the one answer path that leaves a confirmed-unapplied pair. F2 drops RefreshAppliedAsync from the success path and narrows the re-plan to AffectedBy(outcome); measured 2052 ms to 91 ms per answer at 200 pairs, 200 ms to 33 ms at 20. F3 pairs plans to cards by reference. F4 puts OutstandingCountText on the HISTORY segment and an Amber notice in the REVIEW empty state. F5 gives the report undo a verb. F6 moves the answer automation names to the card and names both sides by title and release number. F7 moves Button.seg into Themes/controls.axaml. F8 corrects the report comment. F9 adds two PreviewAsync repository tests. F10 makes selection follow focus into the card list. F11 cut the copy and fixed two documentation contradictions.

One test changed rather than the behaviour: Same_game_applies_the_merge_then_and_there_and_reports_it asserted queue.History was populated straight after an answer, which the agreed F2 design removed. It now asserts CanUndoReport (undo is reachable without reading the history surface) and then switches to HISTORY before asserting the row, which is how a user reaches it.

MergeBlocker.AlreadyApplied is unreachable in BuildPlanAsync: its branch is guarded by !collapses, and collapses is blocker == None, so the ternary can never take the AlreadyApplied arm. BlockedLine is now an explicit switch over the three collapse blockers with a default that asserts nothing about same-work.

Final suite: 2463 + 98 + 70 passed, 0 failed.
<!-- SECTION:NOTES:END -->
