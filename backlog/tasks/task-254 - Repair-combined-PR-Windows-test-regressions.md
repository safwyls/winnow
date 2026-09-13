---
id: TASK-254
title: Repair combined PR Windows test regressions
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-13 06:12'
updated_date: '2026-09-13 06:16'
labels: []
dependencies: []
type: bug
ordinal: 296000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
PR #14 Windows CI fails cover admission, fullscreen platform navigation, and plugin accessibility tests.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Cover admission and cancellation tests use the independent fetch concurrency limit.
- [x] #2 Fullscreen navigation and plugin accessibility checks match reachable controls and current rendering.
- [ ] #3 Windows Release test suites pass after the fixes.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Inspect the three CI failures against production behavior, correct stale assumptions or inaccessible properties, run Release tests, and push fixes to PR #14.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Confirmed CI run 34741481699 failures. Cover lifetime fixture now explicitly limits independent fetch concurrency to two; cancellation and admission assertions remain. Fullscreen platform test checks the rendered Path chevron. Desktop and fullscreen plugin summaries use AutomationId instead of an ignored TextBlock Name; interaction tests verify accessible text and polite live announcements on both surfaces. Full Release suites are running.

Final Release UI suite passed all 584 tests; accessibility enforcement passed all four tests. Remaining local core suite is still running. No visual layout or interaction behavior changed.
<!-- SECTION:NOTES:END -->
