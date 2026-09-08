---
id: TASK-161
title: 'Refine feed actions, list workflows and Steam connection layout'
status: Done
assignee:
  - '@codex'
created_date: '2026-09-08 16:02'
updated_date: '2026-09-08 16:14'
labels: []
dependencies: []
ordinal: 193000
---

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Feed actions do not wrap
- [x] #2 Steam connection layout matches requested hierarchy
- [x] #3 Collapsible rail sections and static or live creation in footer
- [x] #4 Reusable list modal available from library feed and details
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Implement layouts and shared list modal; verify behavior and update visual specification.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Shared scrollable modal replaces both inline pickers. Details membership refreshes after adding. Feed controls use distinct theme inks and tooltips. Core 3747, UI 72, recommendation 155 and cover 108 tests passed; Linux-only 2 tests skipped on Windows. Build passed with zero warnings. Additional rail interaction tests in progress.

Final verification: zero-warning build; 3747 core, 73 UI, 155 recommendation and 108 cover tests passed (4083 total). Two Linux process checks skipped on Windows. Inspected rendered 40-list modal at 800x600. Window-level overlay blocks rail navigation and creation cancellation restores New list focus.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added compact feed feedback icons, aligned Steam session controls, collapsible list sections with footer creation, and a reusable scrollable modal from feed/library/details. Details membership refreshes immediately. Verified with 4083 passing tests and rendered modal inspection.
<!-- SECTION:FINAL_SUMMARY:END -->
