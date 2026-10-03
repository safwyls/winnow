---
id: TASK-387
title: Preload visible library backgrounds after browsing settles
status: In Progress
assignee:
  - '@codex'
created_date: '2026-10-03 22:03'
updated_date: '2026-10-03 22:30'
labels: []
dependencies: []
type: enhancement
ordinal: 465000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The first Details background can wait on image discovery and download, while later visits are immediate. Warm the backgrounds of the games currently visible in the library after a pause in browsing so likely selections are ready earlier.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Desktop and fullscreen libraries in all bundled compositions preload backgrounds for actually visible titles after scrolling, filtering and layout changes settle, excluding overscan and hidden panes.
- [ ] #2 Preloading is bounded, deduplicated, cancellable and reuses the normal artwork cache without exhausting foreground request or pixel budgets; new browsing cancels obsolete work.
- [ ] #3 Opening a warmed title reuses its background data and preserves selection, fallback, invalidation and ownership behavior; focused component and native checks pass and behavior is documented.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Add a shared settled-viewport observer and a small cancellable background queue; reuse each composition details artwork selection and cache path, integrate the three library implementations, and verify visibility, cancellation, cache reuse and native desktop/fullscreen behavior.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Shared 400 ms settled viewport observer is integrated in Avalon, Afterglow/Catalogue and Rift libraries for desktop/fullscreen. It observes clipped card targets, excludes inert/hidden rows and retained panes, and restarts on scroll/filter/layout changes. A two-job queue retains canceled jobs in its concurrency count until retirement and waits behind requested artwork. Initial 77 focused observer, scheduler and existing library tests pass. Independent read-only review found no defects in this integration; native clipping/cache reuse verification remains in progress.

Focused verification now covers 77 observer, queue and library checks plus 90 existing artwork regressions. Two sustained native scroll checks passed with 1,045 games. The first native preload matrix passed seven of eight composition/mode cases and exposed a Rift desktop cache-recency bug: warmed visible cache hits were not promoted, so subsequent neighbors could evict them. Correcting the retention order and rerunning the native matrix before final acceptance.

Final validation: 77 observer/scheduler/library tests, 90 existing artwork regressions and 17 new preload-loader tests pass (184 distinct focused checks). Electron production build/typecheck and migration coverage audit pass; formatting and git diff checks pass. Native preloading tests passed all eight Avalon/Afterglow/Rift/Catalogue desktop/fullscreen combinations with one worker and zero retries, verifying viewport clipping, idle debounce, cancellation, hidden panes and Details cache reuse. Both 1,045-title sustained-scroll tests passed.
<!-- SECTION:NOTES:END -->
