---
id: TASK-389
title: Share background artwork selection across compositions
status: Done
assignee:
  - '@codex'
created_date: '2026-10-04 19:55'
updated_date: '2026-10-04 20:15'
labels: []
dependencies: []
type: bug
ordinal: 467000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Avalon renders available hero art while Afterglow, Rift and Catalogue show placeholders for the same titles. The alternative compositions and their preload path read only the current Hero candidate. If that candidate fails, they bypass the remaining automatic backgrounds and cover fallback available through the backdrop selector.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Avalon, Afterglow, Rift and Catalogue use the same background selection, image cache, fallback and live refresh behavior on desktop and fullscreen.
- [x] #2 Saved backgrounds, automatic landscapes and preferred header cover fallback render correctly; genuinely unavailable art keeps composition-specific placeholders and existing visual treatments.
- [x] #3 Visible-game preloading follows the same shared selection and image cache as foreground backgrounds for every composition.
- [x] #4 Focused component and native Electron checks cover both surfaces, including artwork changes and failed candidates; build passes and current behavior is documented.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Extract the existing Avalon backdrop controller, transport and lifecycle into shared artwork components; retain Avalon-specific geometry and veils in its presentation.
2. Route generic hero artwork and visible-game preloading through the shared selection, fallback and update path. Pass canonical/header identities and surface mode from consumers.
3. Verify selection, updates, cache reuse and placeholders in component tests and native composition checks on desktop and fullscreen. Update documentation and commit the fix.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Extracted the backdrop lifecycle, controller, selection and cached image transport into shared components. Generic Artwork hero callers now use it; compositions retain their own visual treatment. Shared Details and feed/preview callers pass header identity and surface mode. All preload paths now use the same candidate/fallback order and asset keys. Native composition checks pass for all four compositions on desktop/fullscreen; focused checks and the broader suite are being verified.

Verification: npm run build passed, including TypeScript (existing dependency annotation warnings only). Full Electron component suite with --maxWorkers=4: 4,581 passed, 16 skipped across three gated suites. Focused artwork/backdrop/lifetime/enforcement checks: 270 passed, three skipped. Native Electron: composition-backdrops 8/8, background-preload 10/10, backdrop 5/5, one worker and zero retries. Rendered captures inspected for the composition-specific crop and veil treatments. All runs used disposable data and the prebuilt test companion.

The shared Hero path also reaches Journal previews; the asynchronous journal refresh assertion now waits for its visible result instead of incidental artwork query scheduling. Native Avalon tests assert detached DOM source cleanup and request cancellation while permitting the bounded cache to retain reusable pixels; cancellation cases use uncached candidates. The shared controller and cache tests continue to check lease counts, URL disposal and invalidation. No backend behavior or real library data was changed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
All four compositions now use one backdrop selector, candidate fallback order, image cache and live-refresh lifecycle. Failed high-resolution art advances to available landscapes/screenshots and the preferred cover before showing a composition placeholder. Visible-game preloading follows the same path. Existing composition layouts, Avalon geometry and reading veils are retained. Build, 4,581 component tests and 23 native checks pass; 16 environment-gated component cases were skipped.
<!-- SECTION:FINAL_SUMMARY:END -->
