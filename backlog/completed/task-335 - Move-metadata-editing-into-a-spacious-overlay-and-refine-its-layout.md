---
id: TASK-335
title: Move metadata editing into a spacious overlay and refine its layout
status: Done
assignee:
  - '@codex'
created_date: '2026-09-17 17:23'
updated_date: '2026-09-17 17:33'
labels: []
dependencies: []
ordinal: 377000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The artwork browser overlay gives editing tasks useful room. Edit details still occupies a cramped body inside the details modal; move it to the same overlay pattern and review field hierarchy, artwork controls and responsive layout.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Edit details opens a larger overlay above the details card with a clear game title, reachable Back action and comfortable text and artwork editing areas.
- [x] #2 Per-field save/reset, validation and source labels remain intact; opening and closing the nested artwork overlay preserves metadata drafts and returns to the correct control.
- [x] #3 Keyboard focus stays in the active overlay and underlying layers cannot receive input; Escape closes one layer and returns focus predictably.
- [x] #4 Rendered desktop layouts at short, standard and large windows are reviewed, including long values/errors; fullscreen editing is assessed and verified separately; focused tests, build and current visual docs pass.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Rehost metadata editing in a sibling overlay using the artwork overlay pattern, preserving nested artwork ordering. 2. Organize text fields and artwork in responsive columns with consistent sizing/actions and persistent navigation/status. 3. Exercise saves/reset/errors, nested focus/drafts, desktop window sizes and fullscreen regression behavior; inspect rendered captures and review the final layout.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented a sibling metadata overlay capped at 1440x1000 with 24px inset/padding, pinned title/Back/messages, text and artwork columns and stacking below 820px. Shared row objects preserve per-field saves, resets, provenance and drafts; fullscreen retains its established field order and controller pages. Nested artwork disables underlying surfaces and restores the originating Browse button. Rendered headless captures reviewed at 1200x640, 1280x820, 1920x1080, 800x700 and 940x820 (120% text with user-owned reset controls), including long About text and invalid-year feedback. Fullscreen metadata list and field validation/back navigation rendered and exercised separately. Captures: C:\Temp\winnow-metadata-overlay-captures. Independent source review found no actionable issues. Verification: solution build zero warnings/errors; 365 UI tests passed in broader rerun, five final desktop layout cases passed, and 84 metadata/details unit/integration tests passed. Initial broader capture-enabled run had two transient FullscreenToolsHierarchyTests failures; both passed in isolated runs with and without capture and in the complete rerun. TRX: C:\Temp\winnow-metadata-overlay-results\metadata-overlay-ui.trx. No production host or real library data used.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Moved Edit details into a spacious overlay and grouped text/artwork fields responsively. Preserved per-field editing, nested artwork drafts/focus and fullscreen behavior. Verified rendered desktop/fullscreen layouts, keyboard/controller navigation, focused tests and a warning-free solution build; updated the visual specification.
<!-- SECTION:FINAL_SUMMARY:END -->
