---
id: TASK-388
title: Keep Details stable while screenshots load
status: Done
assignee:
  - '@codex'
created_date: '2026-10-04 19:30'
updated_date: '2026-10-04 19:37'
labels: []
dependencies: []
type: bug
ordinal: 466000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The Details tab visibly flashes as screenshots arrive. Isolate screenshot loading and updates so existing text, controls, focus and scroll remain stable on desktop and fullscreen.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Loading or refreshing screenshot metadata and pixels does not replace or reset unrelated Details tab content.
- [x] #2 Desktop and fullscreen preserve screenshot previews, gallery navigation, unavailable states and correct game changes.
- [x] #3 Focused component and native delayed-loading checks pass and current behavior is documented.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Confirmed the flash comes from absolutely positioned screenshot placeholders escaping unpositioned buttons, rather than tab remounts. Wrap each thumbnail in a positioned frame with reserved desktop or fullscreen dimensions. Retain image-local state and contain loading, decoded and unavailable content within that frame. Reproduce with held native responses; verify sibling DOM identity, no sibling mutations, stable image geometry, focus and scroll, then run existing gallery and Details checks and document.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Reproduced before the fix in production Electron: all three loading desktop buttons were only 2px tall and their placeholders extended outside the buttons over the reading region. Added stable screenshot-thumbnail frames; no broad memoization or query changes are needed. Four native delayed-loading cases now pass for desktop/fullscreen and successful/unavailable images, asserting unchanged sibling DOM, no text mutations, retained focus and scroll, and identical preview bounds. Inspected loading and unavailable screenshots. Build/typecheck and 158 focused component tests pass; the full affected native suites are running.

Final validation: npm run build passed with the existing third-party Zod annotation warnings; 158 tests passed across parity-details, parity-details-remaining-contracts, artwork and artwork-placeholders. All 24 native cases in details-layout.spec.ts and gallery.spec.ts passed with one worker and zero retries using an explicit temporary data directory and current prebuilt companion. This includes four new held-image cases, compact/large windows, fullscreen scaling, wheel/keyboard navigation, focus return and thumbnail-to-full-image upgrade. Inspected desktop/fullscreen loading and unavailable images; formatting and diff checks pass.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Contained screenshot loading and unavailable placeholders in stable thumbnail frames shared by all compositions, with separate desktop and fullscreen sizing. The surrounding Details DOM, focus and scroll stay intact as images settle. Confirmed the original bug with a failing native reproduction, then passed 158 component checks, all 24 affected native checks and the production build.
<!-- SECTION:FINAL_SUMMARY:END -->
