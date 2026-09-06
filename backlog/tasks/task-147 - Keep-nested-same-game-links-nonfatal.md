---
id: TASK-147
title: Prevent impossible merge proposals from crashing Winnow
status: Done
assignee:
  - '@codex'
created_date: '2026-09-06 20:32'
updated_date: '2026-09-06 20:47'
labels:
  - identity
  - ui
dependencies: []
documentation:
  - design-system.md
modified_files:
  - src/Winnow.App/ViewModels/MergeQueueViewModel.cs
  - src/Winnow.App/ViewModels/MergeCopy.cs
  - src/Winnow.App/ViewModels/LibraryViewModel.cs
  - src/Winnow.App/Views/MainWindow.axaml
  - tests/Winnow.Tests/MergeQueueViewModelTests.cs
  - tests/Winnow.Tests/IgdbAssignmentModalTests.cs
  - design-system.md
priority: high
type: bug
ordinal: 174000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Fix the Merges queue crash reproduced by Arma 2, Operation Arrowhead, and its beta. Operation Arrowhead is already an expansion child, but the queue offered a same-game link that would make it a parent; the repository correctly refused and the uncaught async-command exception terminated the app.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Same-game candidates involving a work that is already an expansion or variant child are omitted from the Merges queue.
- [x] #2 A stale same-game card refused by the identity repository is removed nonfatally, reports that nothing changed, and cannot terminate Winnow.
- [x] #3 The related IGDB details link path translates structural refusals into its existing nonfatal modal failure.
- [x] #4 Temporary-database regressions cover the Arma-shaped existing-parent scenario without modifying the user's library.
- [x] #5 Relevant tests and the full solution build and test suite pass.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Filter impossible same-game candidate edges before grouping by consulting expansion and variant parents in the loaded identity snapshot. Add a defensive refusal result around queue link commands, remove stale cards, and show a dock notice without Undo. Translate the related details-path refusal to its existing false result. Add real temporary-SQLite regressions, update the Merges specification, and run focused plus full scratch-output verification.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
The live library was inspected read-only to identify the exact graph and was not modified. The fix preserves the repository's depth-one invariant: candidate edges touching expansion or variant children are excluded before same-game grouping. Repository refusals on stale queue cards are consumed, the stale card is removed, and the dock shows a no-change notice without Undo. The IGDB details caller translates the same refusal to its existing false result. Verification: 4 focused regressions passed; full serial dotnet test passed 3,761 tests (84 Covers, 152 Recommend, 3,498 main, 27 UI); dotnet build succeeded with 0 warnings and 0 errors.
<!-- SECTION:NOTES:END -->

## Comments

<!-- COMMENTS:BEGIN -->
created: 2026-09-06 20:39
---
Read-only diagnosis found work 47 already linked under work 46 as expansion_of, with a pending same-game candidate between work 47 and beta work 816. The Merges queue—not the IGDB details offer—was the crashing caller, so the task scope and plan were corrected before implementation.
---
<!-- COMMENTS:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Prevented the Arma 2 / Operation Arrowhead beta crash by filtering structurally impossible same-game proposals and handling stale repository refusals as nonfatal UI outcomes. Added temporary-SQLite regressions for expansion and variant children, stale queue state, and the related details path; all 3,761 tests and the warning-free full build pass.
<!-- SECTION:FINAL_SUMMARY:END -->
