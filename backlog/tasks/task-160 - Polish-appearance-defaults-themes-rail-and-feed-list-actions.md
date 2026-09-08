---
id: TASK-160
title: 'Polish appearance defaults, themes, rail and feed list actions'
status: Done
assignee:
  - '@codex'
created_date: '2026-09-08 05:09'
updated_date: '2026-09-08 05:22'
labels: []
dependencies: []
ordinal: 192000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Apply the eight requested desktop polish changes while preserving saved appearance preferences.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Rail hides unrunnable bucket and labels account statistics Steam Stats
- [x] #2 Floating first and default; Windows 30% Acrylic everything but covers first and default
- [x] #3 Bundle requested local themes and variants
- [x] #4 One collapsed theme warning
- [x] #5 Inactive appearance scrolling preserves position on click
- [x] #6 Feed cards add games to lists
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Trace settings and lists; delegate appearance, themes and feed changes; update rail; reconcile documentation; build and test.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Verified serial full rebuild with zero warnings/errors; full dotnet test passed 4074 tests (3745 unit, 66 UI, 155 recommendation, 108 covers), with 2 Linux-only tests skipped on Windows. Headless tests exercise restored focus, keyboard scroll, feed picker focus/Escape and action nonoverlap. SQLite tests verify feed existing/new-list persistence. Bundled JSON matched local originals by SHA-256. Existing preferences and local theme overrides preserved. Native inactive-window activation is covered through its focus-restoration path in headless tests, not a separate native automation run.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Implemented all eight polish requests: rail visibility/name, appearance defaults/order, five authored themes, collapsed warnings, scroll restoration, and feed list actions. Updated governing documentation. Full build and 4074 tests pass.
<!-- SECTION:FINAL_SUMMARY:END -->
