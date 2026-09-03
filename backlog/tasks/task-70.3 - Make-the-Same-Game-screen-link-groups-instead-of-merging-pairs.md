---
id: TASK-70.3
title: Make the Same Game screen link groups instead of merging pairs
status: In Progress
assignee:
  - '@claude'
created_date: '2026-09-02 00:13'
updated_date: '2026-09-02 02:02'
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

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. CORE, pure and BCL-only: src/Winnow.Core/Merging/MergeGrouping.cs. Input is the pending edges (candidate id, two release ids, score, priority band), a release->work map and a SameGameResolution. It resolves both endpoints, DROPS every edge whose endpoints resolve to one work (TASK-70.1's predicate extended through links), takes connected components over what remains, and returns one MergeGroup per component. A GROUP MEMBER IS A RESOLVED WORK, not a release, so a member can carry more than one store entry and two pairs naming the same work collapse into one member rather than two.

2. CORE, the primary: SurvivorLadder folded across the members (the rung order is a strict total order, so the fold is order-independent), then Choose(winner, best-of-the-rest) to report the rung that actually decided. Reason is the existing MergeSurvivorReason; ChosenByYou when the user moves the radio.

3. CORE, the default-checked set, which is the Prey 2006 / Prey 2017 guard. Start from the primary, walk the other members by best edge score descending (tiebreak work id ascending), include a member only when it has a DIRECT priority-band edge to every member already included. A clique, not a component. A member reachable only through a sibling arrives unchecked with the sibling named. Everything else arrives unchecked.

4. APP, the card. MergeGroupViewModel + MergeGroupMemberViewModel + MergeEdgeViewModel replace MergeCandidateViewModel and MergePreviewViewModel (deleted: the review card no longer previews a merge plan). MergeSideViewModel and MergeSignalViewModel are reused unchanged. TWO DENSITIES OF ONE CARD, switched on member count: at two members the TASK-66 shape survives exactly (200x300 cover, full signal diff, 200x300 cover) with only a primary radio added under each cover and NO checkboxes, because the two buttons already carry include/exclude; at three or more the left column keeps the primary's cover at 200x300 (so the card's outer geometry never changes) and the right column becomes a roster of member rows, each a 56x84 chip, the title, year/store/release ids, an include checkbox, a make-primary radio, and that member's evidence against the primary condensed to one data-face line, with the matcher's own sentences behind a per-member disclose toggle.

5. APP, answering. Same game links: one IdentityLinkRequest, one act, one transaction, primary as parent and every checked member as a child. It writes NO status for an included edge, because a pair is answered affirmatively if and only if a live link exists (AC #8, and it is why the screen can never produce confirmed or undone). Every edge from an unchecked member to an included member is written rejected, so the per-edge answer is not lost. Different games rejects every edge in the group and links nothing. Making a member primary checks it.

6. APP, the answer path reads nothing. Components are disjoint over resolved works, so linking inside one cannot change another; the card is removed and no other card is re-planned. This is the structural replacement for AffectedBy and the fix for the two-second freeze, and it is asserted with a counting repository decorator rather than a stopwatch.

7. APP, HISTORY gains a link-act section ABOVE the two merge sections, each rendered only when non-empty. Two sections rather than one interleaved list because a retraction and a fifteen-table undo are different facts, and interleaving them would need a per-row explanation of which is which -- the over-explanatory blurb notes.md asks us to drop. The merge sections are finite and shrinking (nothing this build adds to them) exactly like the existing leftovers section. Retract calls RetractActAsync; the pairs were never marked, so they return to the queue as ordinary pending rows on the next load, which is AC #5 with no new machinery.

8. RESOLVE, the sweep. LibrarySoftMatchSweep takes IIdentityLinkRepository (optional; absent means IdentityResolution.Empty) and resolves identity.WorkId at Admit time. One change point feeds both BuildAdmission (so SoftMatchAdmission.CouldPropose compares resolved works) and BuildRequests, and the existing retire path then withdraws linked pairs by itself.

9. COPY. Every string via the docs-writer agent with the brevity instruction and the honesty requirement: a link is inert until 70.4 and 70.6, so the report must not imply the library has already changed. QueueIntro, the two tooltips, the member and primary labels, the through-a-sibling line, the link report, the retract control, the link-history heading and its empty state, and every automation name (which must identify the group and its members, with the release id distinguishing two members that share a title).

10. TESTS, from the design: three stores collapse to one card and are approved in one act; an unchecked member records an edge rejection; a below-band edge arrives unchecked; approving a member does not stale a sibling card; link/retract/link four times ends identical to linking once; retract restores each child's prior parent; the survivor radio reports ChosenByYou and an out-of-group choice is refused; and the answer path issues no reads on a queue of many pairs. Plus: no card in any state renders BLOCKED or an already-one-game message, and the screen never calls MergeExecutor.ApplyAsync.

11. The destructive executor, its undo journal, MergeApplyViewModel, MergeHistoryRowViewModel and their tests are untouched and keep working until TASK-70.7 retires them. Only the REVIEW path stops using them.

12. Scoped tests, then the FULL suite across all three projects, all via --artifacts-path into the scratchpad because the user is holding src/Winnow.App/bin. Never touch the live database. Do not commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
IMPLEMENTED, not finalized. Full suite green. Not committed.

THE UNIT IS A GROUP. New pure BCL-only src/Winnow.Core/Merging/MergeGrouping.cs. Build() resolves both ends of every pending proposal through the live link map, DROPS every proposal whose ends resolve to one work, and takes connected components. One component is one card. A MEMBER IS A WORK, NOT A RELEASE, so two proposals naming the same work collapse to one member carrying several store entries, and a work that already holds two entries reads as one game. Cards sort strongest-edge-first with the lowest member id as tiebreak, so the order is total and does not shuffle between loads.

THE CARD AT TWO MEMBERS. TASK-66's shape survives untouched: cover 200x300, the full signal diff, cover 200x300, one scroll, no Flare. The only addition is a primary radio under each cover. NO CHECKBOXES at two members - the two buttons already carry include/exclude, and a checkbox beside them would be a third control for a second-order question. Left and right are ordered by work id and never by which side is primary, so moving the radio recolours the card instead of swapping the two covers under the pointer.

THE CARD AT SIX MEMBERS. Six 200x300 capsules is 1200px of art and turns the card into a table. So the chosen title KEEPS its capsule at 200x300 in the same left column, which is why the card's outer geometry never changes between the two densities, and every other member becomes a roster row: include checkbox, 64x96 chip (same 2:3, one third the width, decoded at one third the resolution), title, year, entry numbers, publisher, the diff condensed to one line of labelled values in the data face (TITLE 0.04 / YEAR d0 / PUBLISHER SAME), the strongest-edge score right-aligned, and a make-primary radio. The matcher's own sentences sit behind a per-member disclose toggle, so the full four-row grid is still reachable for the member being looked at without every member paying for it. Changing the primary moves the big cover, which is the feedback that says what the library will call this game.

THE THIRD STATE THE ROSTER HAD TO CARRY. A member can be in the component without any proposal naming it and the chosen title together. That row shows its strongest edge, names the sibling it arrived through in Amber, and arrives unchecked. Transitive membership is visible rather than implied, which is the Prey 2006 / Prey 2017 guard made legible rather than merely enforced.

DEFAULT-CHECKED IS A CLIQUE, NOT A COMPONENT. Start from the primary, walk the others by best edge score descending (work id ascending as tiebreak), include only where a direct priority-band edge exists to every member already included. DEPARTURE FROM THE BRIEF, deliberate: TWO-MEMBER GROUPS ARE EXEMPT and arrive checked. The rule guards TRANSITIVITY - it stops the closure asserting what no proposal asserted - and a group of two has no closure; the card asks exactly the question the proposal asked. Gating it on the band would read the band as a merge recommendation, which the shipped code already says it is not (it means "show the user this one first"), and it would make Same game silently mean "no" on the majority of cards, since a cross-store pair with one side unenriched scores Review and not Priority. Stated in the code and covered by A_complete_top_band_group_arrives_wholly_checked plus A_below_band_edge_arrives_unchecked.

THE PRIMARY. MergeGrouping.ChoosePrimary folds the existing SurvivorLadder across the whole group; the rung order is a strict total order so the fold is order-independent (The_order_of_the_proposals_does_not_change_the_result). The reason reported is the rung that separated the winner from the BEST OF THE REST, not from an arbitrary neighbour. A preference naming no member throws rather than falling back, so a stale choice cannot link in a direction nobody asked for; SetPrimary routes through the same validation so the refusal has one home.

ANSWERING WRITES A LINK. One IdentityLinkRequest, one act, one transaction, primary as parent and every checked member as a child. It writes NO status for an included edge, because a proposal is answered affirmatively if and only if a live link exists between its resolved works - which is why this screen can no longer produce confirmed or undone at all (AC #8, asserted by The_review_path_applies_no_merge). Every edge with exactly one end inside the link is written rejected, so a rejection made inside a group does not evaporate; an edge with both ends outside is left pending, because the user said nothing about it. Same game with nothing checked links nothing and records every edge, which is the none of none/some/all.

THE ANSWER PATH READS NOTHING. Components are disjoint over resolved works, so a link inside one cannot change another. AffectedBy, the work-of-release cache and the whole re-plan pass are DELETED, not tuned. Answering removes the card and touches no repository but the link write. Asserted by counting, not timing: Answering_reads_nothing_however_long_the_queue_is builds 60 groups, answers 20, and requires GetPendingAsync to stay at 1 call, SetStatusAsync at 0, and the surviving cards to be the same object references.

HISTORY, AND HOW BOTH PATHS COEXIST. Three lists in one scroll, each absent when empty, in this order: Linked games (link acts, retract per act), then Answered-not-yet-applied, then Applied merges. TWO SECTIONS RATHER THAN ONE INTERLEAVED LIST, and the reason is the user's own instruction: a retraction and a fifteen-table reversal are different facts, and interleaving them needs a sentence per row saying which kind each was - exactly the over-explanatory blurb notes.md asks us to drop. A heading carries that distinction structurally and costs no prose per row. The two merge lists are finite and SHRINKING: the review path no longer writes confirmed and no longer calls MergeExecutor.ApplyAsync, so nothing this build does adds to either, and a fresh install sees one section. MergeExecutor, MergeExecutionRepository, the undo journal, MergeApplyViewModel and MergeHistoryRowViewModel are untouched and still tested; Apply, ApplyAll, Undo and UndoBlocking are reachable only from HISTORY.

RETRACT IS ORDINARY. RetractActAsync from the report line or from a history row. The proposals were never marked, so they return as ordinary pending rows on the next load with no new machinery - AC #5 falls out of the model rather than being built. Link_retract_and_link_again_ends_where_linking_once_ends runs four full cycles and asserts the live-link set is identical each time and the card comes back undecided every time. Retracting_a_regrouping_restores_each_member_to_its_previous_group drives 70.2's displacement restore from the screen: b under a, then a chosen under c (which re-parents b inside the same act to hold depth one), then one retraction puts b back under a.

THE SWEEP RESOLVES. LibrarySoftMatchSweep takes IIdentityLinkRepository (optional; absent means every work resolves to itself, the pre-link behaviour exactly) and resolves identity.WorkId once per pass at Admit. One change point feeds both the blocking pass and SoftMatchAdmission.CouldPropose, so the existing retire path withdraws a linked pair by itself. Two new tests: a linked pair is retired and never re-proposed, and retracting makes it proposable again.

COPY, all authored by the docs-writer agent under the brevity instruction and the honesty requirement. QueueIntro "Links can be retracted."; SameGameTooltip "Link these entries (S)"; DifferentGamesTooltip "Record as different, not re-queued (D)"; PendingCountLabel "GROUPS"; PrimaryLabel "KEEP"; PrimaryControlLabel "Keep this title"; IncludeControlLabel "Include"; LinkEffect "Entries still appear separately."; MemberThroughFormat "Indirect, via {0}"; LinkedReportFormat "Linked {1} under {0}. Still shown separately."; NothingLinked "Nothing linked, recorded as different games."; Retracted "Link retracted. Returns to review."; RetractedAlready "Already retracted."; LinkHistoryHeading "Linked games"; LinkHistoryIntro "Newest first. Retract any time."; LinkHistoryEmpty "Groups you link appear here."; LinkRowFormat and LinkRowManyFormat "{0} linked under {1}"; LinkedAtLabel "LINKED"; RetractedLabel "RETRACTED"; RetractButton "Retract"; RetractTooltip "Proposals return to review."; EmptySwept "No matches to review."; EmptyNotSwept "Still scanning your library."; EvidenceShow and EvidenceHide "Show evidence" / "Hide evidence"; NoSignals "No breakdown recorded.". Automation: "Same game: {0}, keep {1}", "Different games: {0}", "Keep {0}", "Include {0}", "Retract: {0}", and a member label that is the title followed by its store entry numbers so two members with one title are distinguishable. THE HONESTY: nothing in the review or report copy contains the word merge, and LinkEffect plus LinkedReportFormat both say the entries still appear separately, because a link is inert until 70.4 and 70.6. Asserted by Answering_a_group_says_it_links_rather_than_merges, which also fails on any remaining placeholder.

ONE AVALONIA CORRECTION, verified against current docs rather than memory. A selector without /template/ never reaches a templated child, so the RadioButton content rules were inert as first written; font and ink moved onto the RadioButton itself, which TemplatedControl inherits into the presenter. GroupName is bound per card because Avalonia groups radios by GroupName and would otherwise make one choice across the whole queue. IsPrimary is written by the radio and calls back into the group, guarded against re-entry because applying a choice writes IsPrimary on every member.

DELETED: MergeCandidateViewModel and MergePreviewViewModel. The review card no longer previews a merge plan, so a plan-shaped preview would be a type with nothing true to say. Their good parts moved: payload decoding and the signal diff to MergeEdgeViewModel, the survivor-reason wording to MergeGroupViewModel, and the cover-and-facts face stays MergeSideViewModel, now also used for roster chips, with ReleaseText listing every entry under one member.

NEW FILES: src/Winnow.Core/Merging/MergeGrouping.cs; src/Winnow.App/ViewModels/MergeEdgeViewModel.cs, MergeGroupViewModel.cs, MergeGroupMemberViewModel.cs, MergeLinkHistoryRowViewModel.cs; tests/Winnow.Tests/MergeGroupingTests.cs. CHANGED: MergeQueueViewModel.cs (review half rewritten, merge half kept), MergeCopy.cs, MergeSideViewModel.cs, MergeQueueServiceCollectionExtensions.cs, Views/MergeQueueView.axaml and .axaml.cs, Views/MainWindow.axaml.cs (S and D act on SelectedGroup), src/Winnow.Resolve/LibrarySoftMatchSweep.cs, tests/Winnow.Tests/MergeQueueViewModelTests.cs, MergeApplyViewModelTests.cs, MergeScreenRegistrationTests.cs, LibrarySoftMatchSweepTests.cs, AccountStatsViewModelTests.cs, LibraryViewModelTests.cs, ListsViewModelTests.cs. All prose in every one of them authored by the docs-writer agent.

VERBATIM, full suite across all three projects, built and run via --artifacts-path into the scratchpad because the user is holding src/Winnow.App/bin:
  Passed!  - Failed: 0, Passed: 70,   Skipped: 0, Total: 70,   Duration: 1 s   - Winnow.Covers.Tests.dll (net10.0)
  Passed!  - Failed: 0, Passed: 102,  Skipped: 0, Total: 102,  Duration: 55 s  - Winnow.Recommend.Tests.dll (net10.0)
  Passed!  - Failed: 0, Passed: 2518, Skipped: 0, Total: 2518, Duration: 1 m   - Winnow.Tests.dll (net10.0)
Build: 0 Warning(s), 0 Error(s) under TreatWarningsAsErrors. Scoped runs first: MergeGrouping and MergeQueue and MergeScreenRegistration 53/53, sweep and soft-match 42/42. One flaky non-regression observed on an intermediate run, IgdbResilienceTests.Rate_limiter_caps_the_initial_burst_and_spaces_the_rest_at_4_per_second; it is a wall-clock rate-limiter assertion, untouched by this stage, and passed on every other run.

NOT FINALIZED: acceptance criteria not checked, no final summary, status left In Progress, nothing committed. The live database was never opened.
<!-- SECTION:NOTES:END -->
