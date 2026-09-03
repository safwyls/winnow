---
id: TASK-83
title: Replace the Same Game screen with the Merges queue design
status: Done
assignee:
  - '@safwyl'
created_date: '2026-09-03 02:17'
updated_date: '2026-09-03 04:21'
labels: []
dependencies: []
documentation:
  - docs/merge_queue_design/README.md
  - design-system.md
ordinal: 110000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Rebuild the merge queue screen to docs/merge_queue_design (Merge Queue.dc.html + README.md): one queue of proposal cards grouped into five sections (ACROSS STORES, EDITIONS, EXPANSIONS, PARTS, TEST BUILDS), each card a header row chosen by clicking a candidate row, Same game / Different games per card, checkbox multi-select with Merge N selected, Accept N exact matches for cross-store exact groups, sort (strongest match / playtime at stake / title) and a kind filter with a cut chip, a resolved strip with Separate again, and an ambient dock card with Undo that auto-dismisses after 7 seconds. The REVIEW / EXPANSIONS / HISTORY segments go away. Decisions taken with the user on 2026-09-02: past link acts fold into their sections as resolved strips across sessions (no HISTORY tab); Escape keeps the app convention (back to the library) and D is Different games; rail row and title follow the mock (MERGES / Merges / Merge N selected) and design-system.md section 7 is amended; per-member include checkboxes are dropped, Same game links every row under the header.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The merge screen renders the mock's three bands (header, cut bar, sectioned queue) and the proposal card composition, styled from tokens.axaml, with no REVIEW/EXPANSIONS/HISTORY segments
- [x] #2 Same-game groups and expansion proposals both appear as cards in the right section; clicking a same-game row promotes it to header and the header title and roll-up recompute
- [x] #3 Same game writes one identity link act with every other row as a child; Different games records rejections or refusals so the group is not re-proposed; both are reversible from the dock's Undo within 7 seconds
- [x] #4 Merge N selected and Accept N exact matches resolve several cards in one gesture and one dock card undoes the whole run
- [x] #5 Resolved strips show live link acts from earlier sessions and Separate again retracts the act
- [x] #6 Sort reorders cards within each section and the kind filter shows one section with a cut chip and a total -> shown count
- [x] #7 Keyboard: Up/Down walk candidate rows across cards, Space promotes, S/Enter answers Same game, D answers Different games, Escape returns to the library
- [x] #8 Rows show cover art with the dormancy ramp, store badge, playtime, idle time and the unread dot from the library's own read model
- [x] #9 dotnet build and dotnet test pass; the old view models, tests and copy the screen no longer uses are deleted
- [x] #10 design-system.md and docs/decisions.md record the copy rule change and the retired HISTORY surface
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Data: add RetractAsync(pairs) to IExpansionRefusalRepository and the SQLite implementation so a dismissed expansion proposal can be undone from the dock.
2. View models: new MergeSectionKind, MergeConfidence, MergeRowViewModel (one row per work: cover with dormancy ramp, stores, playtime, idle, unread, detail), MergeCardViewModel (rows, header index, roll-up, reason, confidence, selected, resolved strip, act id; two flavours for same-game and expansion proposals), MergeSectionViewModel (kind, title, blurb, sorted pending cards, resolved strips, filter visibility), sort and kind option VMs, dock state with a TimeProvider timer and an undo snapshot. Rewrite MergeQueueViewModel around them: load reads candidates, the expansion scan, live link acts, the bucket read model and ownership rows once; answers write links / rejections / refusals in place with no reload; bulk paths and undo runs. Rewrite MergeCopy; trim ExpansionCopy to the details modal plus section copy; extend MergeSideViewModel with the floor bitmap. Delete MergeGroupViewModel, MergeGroupMemberViewModel, ExpansionGroupViewModel, ExpansionMemberViewModel, MergeLinkHistoryRowViewModel, MergeHistoryLabels, MergeReportSurface, MergeSignalViewModel.
3. Views: rewrite MergeQueueView.axaml and code-behind (row hover, focus, promote, scroll-into-view); promote Button.ctl, the sort flyout and the cut chip styles from MainWindow/ActionBar into controls.axaml; rail row becomes MERGES with no count; merge dock card joins the ambient dock host; OnMergeQueueKeyDown walks rows (Up/Down), Space promotes, S/Enter and D answer, Escape leaves.
4. Tests: rewrite MergeQueueViewModelTests, SameGameSurfaceTests and SameGameSignalTests for the new model; update MergeScreenRegistrationTests and the other fixtures that construct the queue; update IdentityReadInventoryTests entries.
5. Docs: design-system.md section 6 and 7 rows, docs/decisions.md entries for the copy rule and the retired HISTORY surface.
6. Verify: dotnet build, dotnet test, run with --data-dir and --seed-sample, screenshot the screen.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Built the screen: new MergeSectionViewModel / MergeCardViewModel / MergeRowViewModel / MergeRowFacts / MergeSort / MergeConfidence / MergeSectionKind, rewritten MergeQueueViewModel and MergeCopy, ExpansionCopy trimmed to the details modal, MergeSideViewModel carries the floor bitmap, MergeEdgeViewModel slimmed to Parse / IsPriorityBand. IExpansionRefusalRepository gained RetractAsync for the dock's Undo. New MergeQueueView.axaml; Button.ctl, the sort flyout and Border.chip promoted into controls.axaml; MERGES rail row without a count; merge dock card in the ambient dock host; OnMergeQueueKeyDown walks rows. Old view models and their three test files deleted. App builds with zero warnings. Drove the screen live with --seed-sample --open-queue --data-dir: promotion, Same game strip, dock Undo, Different games run, Undo, and the EDITIONS filter all behave. Tightened EXACT MATCH to require the raw titles to agree, after the sample showed The Witcher 3 against its GOTY edition as exact. design-system.md section 6 and 7 rewritten; docs/decisions.md records the four user decisions and the assumptions.

Validation: dotnet build src/Winnow.App with zero warnings; dotnet test tests/Winnow.Tests passed 2751 of 2751 (58 in MergeQueueViewModelTests, 17 in MergesSurfaceTests, 5 in MergeMemberLabelTests, 4 in ExpansionRefusalRetractTests, StoreChipLayoutTests reduced to the one rule the new row keeps); tests/Winnow.Recommend.Tests passed 145 of 145. Live run with --seed-sample --open-queue --data-dir on a throwaway directory: promote, Same game strip, dock Undo, a Different games run and its Undo, and the EDITIONS filter all verified by screenshot (docs/screenshots/merges.png). Keyboard verified by the view-model and source-guard tests, not driven live.

Follow-up on the user's request after the first review: the header is now chosen by a radio at the row's head, an include checkbox at the row's end decides who joins (Same game links the checked rows and records a left-out row's proposals as answered no, one Undo reverses the whole answer, Same game is disabled with nothing checked), and a click on the row opens the library's detail modal through MainWindowViewModel wiring MergeQueue.DetailsRequested to LibraryViewModel.OpenDetailsForReleasesAsync. This reverses the earlier 'per-member include dropped' decision; docs/decisions.md carries the reversal and design-system.md section 6 the new row. Verified: 6 new view-model tests, 2 shell wiring tests (MergesDetailsTests), full main project green; live run with the scratch build showed the radio, the checkbox, LEFT OUT and the disabled answer. The details modal could not be opened on sample data because the seeder mints merge pairs without ownership rows, so no tile exists for them; the wiring test covers that path.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Replaced the Same Game screen with the Merges queue from docs/merge_queue_design: five sections of proposal cards with a header row the user promotes, Same game / Different games per card, checkbox multi-select with Merge N selected, Accept N exact matches for cross-store exact groups, sort and kind filter with a cut chip, resolved strips with Separate again for this session's acts and for standing acts from earlier sessions, and an ambient dock with Undo that dismisses after 7 seconds. Rows read hours, idle time and the unread dot from the library's bucket read model and ownership rows. IExpansionRefusalRepository gained RetractAsync so a dismissed expansion proposal can be undone. Old view models and tests deleted; MERGES rail row without a count; shared toolbar and chip styles moved into controls.axaml; design-system.md sections 6 and 7 and docs/decisions.md updated. Verified with dotnet build, dotnet test (2751 + 145 passing) and a driven live run.
<!-- SECTION:FINAL_SUMMARY:END -->
