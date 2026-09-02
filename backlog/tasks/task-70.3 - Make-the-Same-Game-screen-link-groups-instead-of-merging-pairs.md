---
id: TASK-70.3
title: Make the Same Game screen link groups instead of merging pairs
status: To Do
assignee: []
created_date: '2026-09-02 00:13'
updated_date: '2026-09-02 00:15'
labels: []
dependencies:
  - TASK-70.2
parent_task_id: TASK-70
ordinal: 90000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Stage 2 of TASK-70, and the stage that answers points 1, 2, 3 (same-game half) and 5. The Same Game screen stops merging and starts linking, and its unit stops being a pair and becomes a group.

**The queue becomes a living view of groups.** Read pending pairs, resolve both sides through the live link map, drop any pair whose sides resolve to one work, then take the connected components of what remains. One card per component: N store entries, not N-1 pairwise questions. Approving one member can no longer make another card stale, because the members were never separate cards. This is the whole answer to point 2, and it is also the answer to point 3 for the same-game half.

**The card is a chooser, not a verdict.** A radio per member selects the primary, pre-selected by the existing `ChooseWork` ladder and labelled with the rung that decided it (see TASK-70.1), so the user can override it. A checkbox per member selects who is included, so none, some or all is one gesture. Members are default-checked only where every pairwise edge among the checked set exists and clears the priority band; a weaker edge is shown unchecked with its evidence, which is the guard against transitive over-grouping (Prey 2006 and Prey 2017 must not arrive in one component pre-checked).

**Answering writes links, not merges.** One act, one transaction, one link per included child. A member the user unchecks and then confirms records a `rejected` pair against the chosen primary, so the per-edge answer is not lost when the group is applied; without this a rejection inside a group silently evaporates and the next sweep re-proposes it.

**Undo is retraction and it is ordinary.** The report offers Undo this grouping, which retracts the whole act. The pair returns to the queue as pending and can be linked again immediately. There is no `undone` status, no re-confirmation affordance, no terminal state and no reason a second attempt can be refused. `merge_candidates` keeps only `pending` and `rejected`; a pair is answered affirmatively if and only if a live link exists between its resolved works.

**The sweep learns to resolve.** `SoftMatchAdmission.CouldPropose` and `LibrarySoftMatchSweep.BuildRequests` replace their `left.WorkId == right.WorkId` test with resolved equality. The existing retire path then withdraws linked pairs by itself, with no new machinery.

**Tests.** Three releases of one game across three stores produce one card, not three. Approving it writes three-way identity in one act and empties the queue. Unchecking one member and approving records a rejection for that edge and does not link it. A component containing a below-band edge arrives with that member unchecked. Link, undo, link again, undo again works four times with no state change and no refusal. After a link, the sweep proposes nothing for the linked set and retires any leftover pending row naming it. No card in any state renders BLOCKED or an already-one-game message. The screen never calls `MergeExecutor.ApplyAsync`.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The queue shows one card per group of store entries that resolve to the same game, never one card per pair
- [ ] #2 Answering a card cannot make any other card stale or unanswerable, in any order of answering
- [ ] #3 The card lets the user choose the primary title, shows why the default was chosen, and lets the user include none, some or all of the members
- [ ] #4 Approving a group is one act and one transaction, and undoing it retracts the whole act
- [ ] #5 A pair can be linked and unlinked repeatedly, and after an undo the pair returns to the queue as an ordinary pending pair
- [ ] #6 A member excluded from a group records a rejection for that edge, so a later sweep does not re-propose it
- [ ] #7 The soft-match sweep and admission resolve links, so a linked pair is never proposed again and leftover pending rows are retired
- [ ] #8 merge_candidates carries only pending and rejected, and no screen can produce an undone status
<!-- AC:END -->
