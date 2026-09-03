---
id: TASK-70.1
title: >-
  Stop the Same Game queue offering pairs that are already one game, and state
  why a title wins
status: In Progress
assignee:
  - '@claude'
created_date: '2026-09-02 00:12'
updated_date: '2026-09-02 00:58'
labels: []
dependencies: []
parent_task_id: TASK-70
ordinal: 88000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Stage 0 of TASK-70, and the only stage that is independent of the link decision. It improves the shipped build with no schema change, and it stands even if the link recommendation is rejected.

Two defects, both visible on the live library.

**The queue shows pairs that cannot be acted on.** `MergeCandidateRepository.GetPendingAsync` returns every `status = pending` row regardless of whether the two releases already sit under one work. `MergeExecutionRepository.GetConfirmedUnappliedCandidateIdsAsync` already applies exactly the right predicate (`l.work_id <> r.work_id`) on the confirmed read; the pending read never got it. The result is the BLOCKED card and the already-one-game sentence in `MergePreviewViewModel.BlockedLine`, which the user should never have been shown. Moving the same predicate onto the pending read removes that state from the screen entirely. `SoftMatchAdmission.CouldPropose` already agrees that such a pair is unproposable; only the sweep, which runs at launch, currently acts on it.

**The card does not say why one title wins.** `MergeExecutionRepository.ChooseWork` runs a four-rung ladder (holds `igdb_id`; name not provisional; more releases; lowest id) and the last rung is ingestion order. In a cross-store pair where both sides are enriched or both provisional, the answer is always the last rung, and `MergePreviewViewModel.SurvivorLine` states the outcome without the reason. Surface the rung that decided as a short phrase beside the survivor line: IGDB match, Named by store, Most store entries, Added first. Saying Added first out loud is the point; it is the honest admission the user is asking for.

**Tests.** A pending pair whose two releases share a work is absent from `GetPendingAsync`. A pending pair across two works is present. The screen renders no BLOCKED card for any pair the pending read returns. Each of the four ladder rungs produces its own reason phrase, and a pair discriminated only by id reports Added first. Existing `MergeQueueViewModelTests` continue to pass.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The pending queue excludes any pair whose two releases already belong to one work, using the same predicate the confirmed read already applies
- [ ] #2 No BLOCKED card and no already-one-game message can appear for any pair the queue shows
- [ ] #3 Each card names the reason the proposed surviving title was chosen, in one short phrase, including when the reason is only that it was added first
- [ ] #4 Tests cover a same-work pair being absent from the pending read and each tiebreak rung producing its own reason phrase
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Core: add MergeSurvivorReason enum (Winnow.Core/Merging) with rungs None, AlreadyOneGame, IgdbMatch, NamedByStore, MostStoreEntries, AddedFirst, ChosenByYou. It is a VALUE the UI renders, never a prose string built in the repository.
2. Core: extract the ChooseWork ladder out of MergeExecutionRepository into a pure, BCL-only type — SurvivorCandidate (WorkId, HasIgdbId, NameIsProvisional, ReleaseCount) + SurvivorLadder.Choose(a, b, preferredWorkId) returning SurvivorDecision (SurvivingWorkId, AbsorbedWorkId, Reason). Identical rung order and identical outcomes to today when no preference is passed. This is the type that survives the retirement of the destructive executor in 70.7 (demoted to the default suggestion in the primary picker).
3. Core: survivor-choice contract for 70.3 — MergeRequest.PreferredSurvivingWorkId (long?). Null keeps the ladder. A value that names one of the pair's two works overrides the ladder and reports Reason = ChosenByYou. A value naming neither is refused, not ignored: new MergeBlocker.PreferredSurvivorNotInPair and MergeMode.NothingToDo, so a stale UI preference can never merge in the wrong direction.
4. Data: MergeExecutionRepository maps its WorkRow onto SurvivorCandidate and calls the ladder; MergePlan gains SurvivorReason. No behaviour change for existing callers (they pass no preference).
5. Data: MergeCandidateRepository.GetPendingAsync gains the predicate the confirmed read already applies — JOIN releases l/r ... WHERE l.work_id <> r.work_id. Same predicate, same reason.
6. App: MergeCopy gains one short phrase per rung (IGDB match / Named by store / Most store entries / Added first / Your choice), authored by docs-writer. MergePreviewViewModel exposes SurvivorReasonText + HasSurvivorReason; MergeQueueView renders it beside the survivor line.
7. App: MergeQueueViewModel.RefreshPreviewsAsync drops any card whose fresh plan is NothingToDo instead of giving it a BLOCKED preview. Filtering the pending read handles load; this handles the post-answer path, where answering one pair makes a neighbour already-one-game. Together they make the BLOCKED already-one-game card unreachable from the queue.
8. Tests: same-work pending pair absent from GetPendingAsync and a cross-work pair present; each of the four ladder rungs reports its own reason and an id-only discrimination reports AddedFirst; preferred survivor honoured and out-of-pair preference refused; no card the queue shows is blocked; a card that becomes already-one-game after an answer leaves the queue.
9. Scoped tests, then the FULL suite across all three projects, all via --artifacts-path into the scratchpad (the user is holding src/Winnow.App/bin).
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
IMPLEMENTED, not finalized. Full suite green (see TASK-70.2 notes for the combined run).

THE PENDING READ. MergeCandidateRepository.GetPendingAsync now joins releases twice and adds AND l.work_id <> r.work_id — the same predicate MergeExecutionRepository.GetConfirmedUnappliedCandidateIdsAsync already applied to the confirmed read. The row is not deleted and not answered: GetAllAsync and FindByPairAsync still return it, because those are reads about the ROW while this is the read about the QUESTION, and the question is closed. The sweep's own withdrawal pass removes it later.

THE REASON CONTRACT, which is a value and not a sentence. New MergeSurvivorReason enum in Winnow.Core.Merging: None, AlreadyOneGame, IgdbMatch, NamedByStore, MostStoreEntries, AddedFirst, ChosenByYou. MergePlan gained SurvivorReason. The view layer words it (MergeCopy.SurvivorReason*, MergePreviewViewModel.SurvivorReasonText/HasSurvivorReason), so the repository never builds a prose string.

THE LADDER MOVED TO CORE. MergeExecutionRepository.ChooseWork was a private four-rung expression; it is now SurvivorLadder.Choose in Winnow.Core/Merging over a pure SurvivorCandidate record, returning SurvivorDecision (surviving, absorbed, reason). Rung order is unchanged and the outcome is identical for every existing caller. It moved because TASK-70.7 deletes the executor and the ladder survives as the default suggestion in the primary picker; and because a pure type lets each rung be tested without a database.

THE SURVIVOR-CHOICE CONTRACT (picker UI lands in 70.3). MergeRequest.PreferredSurvivingWorkId, long?. Null keeps the ladder. A value naming one of the pair's two works overrides every rung and reports ChosenByYou. A value naming NEITHER is refused, never ignored: new MergeBlocker.PreferredSurvivorNotInPair and MergeMode.NothingToDo, so a stale UI preference cannot merge in the wrong direction. Validation lives on the shared BuildPlanAsync path, so preview and apply cannot disagree.

FINDING, and it is an argument for the link model. Wiring the choice through revealed that the destructive executor CANNOT honour an arbitrary survivor. works.igdb_id is UNIQUE and UnifyWorksAsync COALESCE-fills it from the absorbed row, so choosing the side that does not hold the igdb_id makes the fill collide with the very row it is about to delete — SQLite Error 19, caught by a test. Under the ladder alone this is unreachable, because rung one always keeps the holder; it becomes reachable the moment a survivor can be chosen. Added MergeBlocker.SurvivorCannotHoldIgdbId, which refuses the plan rather than throwing mid-transaction, with copy so the fallback does not print a false 'Already one game'. The condition is stated as a fact about the executor rather than about the choice: the absorbed work holds an igdb_id the survivor does not. The link model does not have the problem at all — both igdb_id values stay on their own rows and the child keeps being enriched.

NO BLOCKED CARD, FOR A WHOLE SESSION. Filtering the pending read only fixes the load path. Answering one pair can make a NEIGHBOURING pair already-one-game, and the shipped code gave that card a BLOCKED preview. MergeQueueViewModel.RefreshPreviewsAsync now collects cards whose freshly read plan is NothingToDo and Removes them instead. The card leaves the screen; the row is NOT answered on the user's behalf and stays pending, because a rejection would record a decision the user never made. LoadAsync's post-refresh Select was changed to read Candidates rather than the pre-prune local list.

TEST REPLACED, DELIBERATELY. MergeQueueViewModelTests.A_blocked_pair_states_the_block_before_the_answer_and_answering_writes_nothing asserted the exact state AC #2 forbids. It is replaced by four tests: A_pair_that_is_already_one_game_never_reaches_the_screen, No_card_the_queue_shows_is_blocked, Every_card_names_the_reason_its_survivor_was_chosen, A_card_that_becomes_one_game_leaves_the_queue_when_its_neighbour_is_answered. RepositoryRoundTripTests.Merge_candidates_queue_and_resolve seeded both releases under ONE work; it now seeds two, and a companion test asserts the same-work pair is hidden from the queue but still reachable by GetAllAsync and FindByPairAsync.

NEW/CHANGED FILES. src/Winnow.Core/Merging/{MergeSurvivorReason,SurvivorLadder}.cs (new); MergePlan.cs, MergeRequest.cs, MergeBlocker.cs; src/Winnow.Core/Repositories/IMergeCandidateRepository.cs; src/Winnow.Data/Repositories/{MergeCandidateRepository,MergeExecutionRepository}.cs; src/Winnow.App/ViewModels/{MergeCopy,MergePreviewViewModel,MergeQueueViewModel,MergeApplyViewModel}.cs; src/Winnow.App/Views/MergeQueueView.axaml; tests/Winnow.Tests/SurvivorLadderTests.cs (new), MergeQueueViewModelTests.cs, MergeExecutionTests.cs, RepositoryRoundTripTests.cs. All prose in them authored by the docs-writer agent.

VERBATIM, full suite across all three projects (this is the combined run after TASK-70.2 also landed; TASK-70.1 alone was green at 2481/102/70 before 70.2 added its 16 tests):
  Passed!  - Failed: 0, Passed: 70,   Skipped: 0, Total: 70,   Duration: 1 s   - Winnow.Covers.Tests.dll (net10.0)
  Passed!  - Failed: 0, Passed: 102,  Skipped: 0, Total: 102,  Duration: 49 s  - Winnow.Recommend.Tests.dll (net10.0)
  Passed!  - Failed: 0, Passed: 2497, Skipped: 0, Total: 2497, Duration: 52 s  - Winnow.Tests.dll (net10.0)
Build: 0 Warning(s), 0 Error(s) under TreatWarningsAsErrors. Built and run via --artifacts-path into the scratchpad because the user is holding src/Winnow.App/bin. Not committed.
<!-- SECTION:NOTES:END -->
