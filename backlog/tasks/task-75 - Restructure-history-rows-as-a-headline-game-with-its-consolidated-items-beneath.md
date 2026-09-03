---
id: TASK-75
title: >-
  Restructure history rows as a headline game with its consolidated items
  beneath
status: Done
assignee:
  - '@claude'
created_date: '2026-09-02 18:16'
updated_date: '2026-09-02 18:41'
labels: []
dependencies: []
ordinal: 102000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
History rows today put the whole act into one run-on sentence, so the row reads as a butchered title rather than a record:

  'Arma 2: Operation Arrowhead, Arma 2: Operation Arrowhead Beta (Obsolete) grouped under Arma 2'
  'The Stanley Parable (Steam) linked under The Stanley Parable (Epic)'

The act has a shape the sentence is fighting: one consolidated game, and the items consolidated into it. The row should use that shape directly.

Wanted, per the user: the consolidated top-level game name as the row headline, and the consolidated items as subtext beneath it.

  Arma 2
  Arma 2: Operation Arrowhead, Arma 2: Operation Arrowhead Beta (Obsolete)

  The Stanley Parable
  The Stanley Parable (Steam)

The parent is the headline and the children sit under it, so the relationship is carried by position and 'grouped under' / 'linked under' is no longer needed in the sentence. The existing meta line already states the verb and the date (GROUPED 2 Sep 2026), and the Undo button stays.

## Disambiguation, narrowed deliberately

TASK-71 gave history the cards' progressive ladder (title, then stores, then year, then publisher, then positional), which is why a three-way collision could render 'Prey (Steam, 2017, Bethesda Softworks, 1 of 3)'. That ladder is right for a card the user is answering and wrong for a log they are scanning, and it is the 'too much info' this task exists to remove.

New rule: the headline carries a qualifier ONLY when a child in the same row would otherwise render a string identical to it, and then only the shortest discriminator that separates them, the store, never the full ladder. Distinct titles carry nothing. A row where parent and children are all the same title must still be readable, since positional order alone does not say which line is the parent.

The cards keep the full ladder; this is a deliberate divergence between the two surfaces, and it is the answer to the open question TASK-71 left.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A history row renders the consolidated game as its headline and the consolidated items as subtext beneath it
- [x] #2 The row no longer builds a run-on sentence, and 'grouped under' / 'linked under' is gone from the row text; the verb and date stay on the existing meta line
- [x] #3 An act with several children lists them all as subtext without truncating the headline
- [x] #4 The headline is qualified only when a child would otherwise render an identical string, and then by store alone, never by the full year/publisher/positional ladder
- [x] #5 A row of distinct titles carries no qualifier anywhere
- [x] #6 The cards keep the full MergeMemberLabels ladder; only history diverges
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. New MergeHistoryLabels (src/Winnow.App/ViewModels/MergeHistoryLabels.cs): the narrowed, history-only rule. Input is index-aligned titles + comma-joined store names, [0] the parent. Children whose title is shared by another member take their store ONLY when that store actually separates them from a same-titled member; then the headline takes its store only if a child label still renders identically to it. No year, no publisher, no positional rung. Distinct titles get nothing.
2. MergeQueueViewModel.BuildLinkHistoryAsync: replace NameApartAsync's MergeMemberLabels call with MergeHistoryLabels. Keep the lazy store read (release + ownership per work) gated on a title collision, so the common row still costs one work read.
3. MergeLinkHistoryRowViewModel: ParentTitle is the headline, ChildTitlesText the subtext. Drop Description as visible row text; keep one spoken sentence, used only by UndoAutomationName, because a flat automation string has no position to carry the relation (design-system.md section 8). Add HasChildTitles for the subtext line.
4. MergeQueueView.axaml history template: headline in body-l, children beneath in body 12px TextDim wrapped. GROUPED/LINKED meta line and the Undo button unchanged.
5. Copy: add MergeCopy.HistoryQualifierFormat and record the deliberate divergence from the card ladder there. Retire LinkRowManyFormat / GroupRowManyFormat (byte-identical to their singular forms). All prose delegated to docs-writer.
6. IMergeMemberFacts: keep the interface (MergeSideViewModel implements it, the cards' ladder reads it); delete the MergeMemberFacts record, which only history used.
7. Tests: retarget A_history_row_tells_two_same_titled_works_apart and A_history_row_of_distinct_titles_carries_no_qualifier at the headline/subtext structure; retarget The_history_surface_lists_the_link_act_and_names_what_it_grouped, which asserted the positional last resort this task removes. Add direct MergeHistoryLabels coverage. SameGameSignalTests' ladder tests stay untouched as AC#6's evidence.
8. dotnet build + full dotnet test with --artifacts-path into the scratchpad.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Row structure: MergeLinkHistoryRowViewModel.ParentTitle is the headline (body-l) and ChildTitlesText the subtext (body 12px TextDim) in MergeQueueView.axaml's history DataTemplate. The GROUPED/LINKED meta label, the date and the Undo button are unchanged. Description is gone; the run-on sentence survives only as SpokenDescription, which feeds UndoAutomationName and is never drawn - a flat automation string has no position to carry the relation the drawn row now carries by layout (design-system.md section 8).

Disambiguation: new MergeHistoryLabels replaces MergeMemberLabels for history. Children whose title is shared with another member of the row take their store, and only when that store actually differs from the store of the member they collide with; the headline then takes its own store only if a child still renders identically to it. Never year, publisher or position. This two-phase order is what leaves the plain game name on the headline in the ordinary Steam-against-Epic case. The cards keep the full ladder (SameGameSignalTests' ladder tests untouched).

Verification: dotnet build clean (0 warnings, TreatWarningsAsErrors) and full dotnet test green at 2,874 - the 2,868 baseline plus six new tests. Artifacts written to the scratchpad, never the repo. The user's live database was never opened; no app launch was needed because every criterion is covered by the view model and the label rule under test.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
History rows now draw the consolidated game as a headline with the items consolidated into it as subtext beneath, replacing the run-on sentence. The GROUPED/LINKED meta label, the date and the Undo button are unchanged. Disambiguation is narrowed for history alone: the new MergeHistoryLabels qualifies a same-titled child by its store, and only where that store actually differs from the member it collides with; the headline takes its own store only if a child still renders identically to it after that. Never year, publisher or position. The merge cards keep the full MergeMemberLabels ladder and their tests are untouched, so the divergence is deliberate and is recorded in MergeHistoryLabels, in MergeCopy.HistoryQualifierFormat beside the card format it diverges from, in MergeQueueViewModel.BuildLinkHistoryAsync and next to the ladder tests it sits beside. The sentence survives only as SpokenDescription, feeding the Undo control's automation name, because a flat automation string has no position to carry the relation the drawn row now carries by layout. Verified by dotnet build (0 warnings under TreatWarningsAsErrors) and the full dotnet test suite green at 2,874, the 2,868 baseline plus six new tests: five over MergeHistoryLabels directly and one over the row structure, with three TASK-71 tests retargeted rather than deleted.
<!-- SECTION:FINAL_SUMMARY:END -->
