---
id: TASK-178
title: Apply a preferred platform to pending merge headers
status: Done
assignee:
  - '@codex'
created_date: '2026-09-10 19:01'
updated_date: '2026-09-10 19:09'
labels:
  - ui
dependencies: []
ordinal: 209000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Add a queue-wide preferred-platform option that switches pending merge headers to a matching platform when available. TASK-109 separately covers changing existing linked groups and their persistent per-group preference.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Choosing a platform updates all eligible pending headers, including filtered-out cards; unavailable platforms leave headers unchanged.
- [x] #2 The preference is visible, saved for later queue loads, can be cleared, and allows subsequent per-card overrides.
- [x] #3 Expansion bases and completed links are preserved; confirmed merges use the selected header.
- [x] #4 Desktop and fullscreen expose the same behavior and relevant tests pass.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add shared preference selection using existing card promotion and settings storage. 2. Add desktop and fullscreen controls following their existing menus. 3. Verify bulk choice, fallback, reload, overrides and persisted link parent; update visual documentation.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented shared saved merges.preferred_platform selection (None, Steam, Epic, GOG), applying to all eligible pending cards on selection and load. Matching current headers are retained when multiple rows offer the platform; absent matches stay unchanged. Existing Promote path preserves inclusion and link direction. Desktop cut-bar flyout and fullscreen controller action sheet use the same command; a returning queue reads changes from the other surface. None preserves current pending choices. TASK-109 remains separate for existing linked groups. Full build passed with zero warnings/errors. Main suite passed 3898, recommendation 160, covers 112; two Linux tests skipped on Windows. Initial UI run caught flyout Hide clearing the clicked DataContext; fixed by capturing option first. Targeted merge, preview and accessibility suite then passed 169 tests. Desktop interaction test passed; full updated UI suite and fullscreen interaction verification pending.

Final UI verification: 251/251 passed, including desktop flyout selection, dismissal, keyboard focus, accessible state and minimum-width layout, plus fullscreen controller action-sheet selection with real temporary repository DI and saved preference on reopening. Targeted 169 tests include six new shared behavior cases: hidden cards and accepted parent, persistence/override/clear, expansion and completed-link protection, cross-surface revisit and reinclusion, multiple matching rows, and missing/unknown platform fallback. git diff --check passed. No production host or real library writes were used.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added remembered None/Steam/Epic/GOG preferred-platform controls to desktop Merges and fullscreen identity tools. Selection promotes eligible pending headers across all kinds while retaining missing-platform headers and allowing manual overrides. Confirmed merges use the selected parent; existing links and expansion bases remain intact. Build passed with zero warnings/errors; main 3898, recommendation 160, covers 112, targeted merge/preview/accessibility 169 and final UI 251 tests passed. Two Linux-only tests skipped on Windows.
<!-- SECTION:FINAL_SUMMARY:END -->
