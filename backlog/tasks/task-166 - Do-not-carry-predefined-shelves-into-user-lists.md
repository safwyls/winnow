---
id: TASK-166
title: Do not carry predefined shelves into user lists
status: Done
assignee:
  - '@codex'
created_date: '2026-09-09 02:37'
updated_date: '2026-09-09 02:41'
labels: []
dependencies: []
references:
  - src/Winnow.App/ViewModels/LibraryViewModel.cs
documentation:
  - design-system.md
modified_files:
  - src/Winnow.App/ViewModels/LibraryViewModel.cs
  - tests/Winnow.Tests/ListsViewModelTests.cs
  - design-system.md
  - docs/decisions.md
priority: medium
type: bug
ordinal: 198000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Opening a user-created list from a predefined library bucket currently intersects the list with that bucket, hiding list members and making the list appear different from what the user created. Entering a manual list should start from its stored membership without inheriting the previous bucket. A live list should continue to restore exactly the bucket saved in its rules, including no bucket.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Opening a manual list from a predefined bucket clears the bucket and shows the list stored members
- [x] #2 Opening a live list restores its saved bucket rather than retaining the previously selected bucket
- [x] #3 Automated tests cover manual and live list transitions from a predefined bucket
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add regression tests for opening manual and live lists while a predefined bucket is selected. 2. Clear the ambient bucket when entering any list, then let a live list restore its saved rules. 3. Update the list interaction specification and decision history to state the replacement behavior. 4. Run focused list tests, then the broader build and test suite.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Cleared the ambient bucket when opening a list, with live lists restoring their saved bucket through the existing saved-filter path. Added manual-list and live-list regression tests and updated design-system.md plus docs/decisions.md. Focused ListsViewModelTests pass: 34/34.

Full verification passed: dotnet build completed with 0 warnings and 0 errors; dotnet test passed 4,138 runnable tests across Winnow.Tests, Winnow.Ui.Tests, Winnow.Recommend.Tests, and Winnow.Covers.Tests. Two Linux-only monitor tests skipped on Windows as designed. git diff --check reported no errors.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
User-created lists no longer inherit the predefined bucket that was active before they opened. Manual lists now show their stored membership, while live lists still restore exactly their saved rules. Regression coverage exercises both transitions; the full solution build and all 4,138 runnable tests pass.
<!-- SECTION:FINAL_SUMMARY:END -->
