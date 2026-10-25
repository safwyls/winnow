---
id: TASK-290
title: Unify desktop feed and library tile interactions
status: Done
assignee: []
created_date: '2026-09-14 05:35'
updated_date: '2026-09-14 05:44'
labels: []
dependencies: []
ordinal: 332000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Library details currently need a double click and feed covers omit the library launch overlay and folded details corner. Share the desktop tile presentation so interaction and resizing remain consistent.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Single click on desktop feed or library tiles opens details; launch and feedback actions execute independently.
- [x] #2 Shared responsive cover component provides hover Play/Install and details dogear in both layouts while retaining previews, artwork, accessibility and recycling behavior.
- [x] #3 Document and test desktop behavior and verify fullscreen remains on its existing controller presentation.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Reuse GameTileView as the shared responsive cover face; retain host-owned feed preview/caption activation and feedback strip. Open library details on release while preserving selection on press and independent action buttons. Verify pointer/keyboard actions, cancellation, resizing, recycling and fullscreen regression coverage; update design system.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Desktop feed now embeds GameTileView and shares artwork leases, Fit/Fill, dormancy, badges, launch overlay and details dogear. Feed reserves 48px for independent feedback controls and delegates details activation to the shared cover. Library selects on press and opens on release, rejecting cancelled/recycled presses. Pointer focus no longer pins feed hover controls. Fullscreen remains on its existing separate presentation and was covered by the full UI suite. Release build: zero warnings/errors. Full UI suite: 702 passed; after final hover/cancellation edits, 55 focused tests passed (including two new cancellation cases). Enforcement suite: 100 passed. Inspected rendered 180px feed capture and tested 180/240px plus live resizing; no action overlap. Design system updated.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Unified desktop feed/library cover rendering and actions through GameTileView. Single-click details, independent Play/Install and feedback controls, responsive sizing and keyboard/hover behavior verified with full UI, focused interaction and enforcement suites; fullscreen presentation preserved.
<!-- SECTION:FINAL_SUMMARY:END -->
