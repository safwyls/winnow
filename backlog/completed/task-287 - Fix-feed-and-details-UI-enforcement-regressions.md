---
id: TASK-287
title: Fix feed and details UI enforcement regressions
status: Done
assignee:
  - '@codex'
created_date: '2026-09-14 04:14'
updated_date: '2026-09-14 04:18'
labels: []
dependencies: []
ordinal: 329000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Full suite reports discarded ratings accessibility name, noncanonical badge margin and obsolete feed reduced-motion selector after the desktop redesign.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Ratings accessible name reaches the automation control view without changing visible text.
- [x] #2 Badge alignment and both feed reduced-motion surfaces pass enforcement; full Windows suite passes.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Expose ratings through a named accessible wrapper, normalize badge margin, update motion inventory to current art and countdown selectors, and run enforcement plus full suite.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Moved desktop header ratings accessible name to an explicitly exposed wrapper; hidden the visual child from the automation control view to avoid duplicate announcements. Added a runtime peer assertion in details refresh coverage. Fullscreen ratings presentation is unchanged and fullscreen parity tests passed. Normalized feed badge to canonical 8,8,0,0 without visual change. Updated reduced-motion inventory to check both existing Art and Countdown snapping selectors. All test assemblies pass: 5971 passed, 2 Linux-only skipped. Initial combined scratch-output run hit a shared xunit DLL copy lock; remaining Winnow.Tests project rebuilt and passed separately (4725); UI 699 passed. No design-system behavior change.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Fixed the three reported enforcement failures; all 5971 Windows tests pass and two Linux-only cases skip.
<!-- SECTION:FINAL_SUMMARY:END -->
