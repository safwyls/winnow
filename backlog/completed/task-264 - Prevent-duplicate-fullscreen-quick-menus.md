---
id: TASK-264
title: Prevent duplicate fullscreen quick menus
status: Done
assignee:
  - '@codex'
created_date: '2026-09-13 19:36'
updated_date: '2026-09-13 19:38'
labels: []
dependencies: []
ordinal: 306000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Repeated controller Start presses currently stack Quick menu pages, requiring multiple Back presses. Keep one quick menu open and preserve its focus and return destination. Assess desktop Start routing separately.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Repeated Start presses leave one quick menu; one Back or Resume returns to the original page and focus.
- [x] #2 The quick menu can reopen after dismissal, including over an existing action menu; relevant UI tests pass.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Track the quick-menu page by identity and ignore repeat opens while it is on the stack. Add headless regression tests for repeated Start, Back, Resume and reopening; verify existing fullscreen/controller tests.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
QuickMenu now tracks its page identity and ignores repeated opens while that page remains on the stack. Back clears the reference. Regression coverage exercises five repeated Start presses, preserving menu selection, Back and Resume, reopening, and restoration over both root content and an existing action menu. Desktop Start still enters fullscreen through MainWindow routing; existing controller navigation tests passed. Release UI build and all 20 targeted QuickMenu, FullscreenInteraction and GamepadNavigation tests passed. Updated visual specification. git diff --check passed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Prevented duplicate fullscreen quick menus while preserving selection and return focus. Verified 20 headless controller/menu interaction tests.
<!-- SECTION:FINAL_SUMMARY:END -->
