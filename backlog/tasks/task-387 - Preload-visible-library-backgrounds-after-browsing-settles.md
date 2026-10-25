---
id: TASK-387
title: Preload visible library backgrounds after browsing settles
status: Done
assignee:
  - '@codex'
created_date: '2026-10-03 22:03'
updated_date: '2026-10-04 01:20'
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
- [x] #1 Desktop and fullscreen libraries in all bundled compositions preload backgrounds for actually visible titles after scrolling, filtering and layout changes settle, excluding overscan and hidden panes.
- [x] #2 Preloading is bounded, deduplicated, cancellable and reuses the normal artwork cache without exhausting foreground request or pixel budgets; new browsing cancels obsolete work.
- [x] #3 Opening a warmed title reuses its background data and preserves selection, fallback, invalidation and ownership behavior; focused component and native checks pass and behavior is documented.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Investigate why new scrolled batches do not warm in the production library, including ongoing visual effects, viewport rebinding and foreground-load scheduling. Reproduce the failure under realistic motion and loading before changing the shared observer or scheduler. Verify desktop and fullscreen with a regression that opens a newly scrolled title without relying on an earlier Details visit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Shared 400 ms settled viewport observer is integrated in Avalon, Afterglow/Catalogue and Rift libraries for desktop/fullscreen. It observes clipped card targets, excludes inert/hidden rows and retained panes, and restarts on scroll/filter/layout changes. A two-job queue retains canceled jobs in its concurrency count until retirement and waits behind requested artwork. Initial 77 focused observer, scheduler and existing library tests pass. Independent read-only review found no defects in this integration; native clipping/cache reuse verification remains in progress.

Focused verification now covers 77 observer, queue and library checks plus 90 existing artwork regressions. Two sustained native scroll checks passed with 1,045 games. The first native preload matrix passed seven of eight composition/mode cases and exposed a Rift desktop cache-recency bug: warmed visible cache hits were not promoted, so subsequent neighbors could evict them. Correcting the retention order and rerunning the native matrix before final acceptance.

Final validation: 77 observer/scheduler/library tests, 90 existing artwork regressions and 17 new preload-loader tests pass (184 distinct focused checks). Electron production build/typecheck and migration coverage audit pass; formatting and git diff checks pass. Native preloading tests passed all eight Avalon/Afterglow/Rift/Catalogue desktop/fullscreen combinations with one worker and zero retries, verifying viewport clipping, idle debounce, cancellation, hidden panes and Details cache reuse. Both 1,045-title sustained-scroll tests passed.

User follow-up: initial preloading may work, but scrolling to new titles leaves backgrounds cold until Details is opened. Previous native fixtures forced reduced motion and fast successful cover reads; investigate production behavior rather than relying on that evidence.

Follow-up diagnosis: the global artwork pending count included offscreen and canceled-but-unacknowledged cover loads, and the speculative queue waited for that count to reach zero without a time bound. A regression with busy permanently true failed before the fix. The queue now allows covers one additional second after the 400 ms browse pause, then starts its existing maximum of two background jobs. New browsing still cancels obsolete work and restarts the allowance. All 27 focused queue, observer and loader checks pass; the real Avalon desktop native regression also passes with one offscreen cover held pending. Fullscreen native setup is being adjusted to scroll past the initially visible two rows.

Follow-up verified: all 10 native preloading cases pass with one worker and zero retries, including real Avalon desktop and fullscreen shells with normal motion, a newly scrolled batch and one held offscreen cover. Visible covers finish, backgrounds warm while artworkPending remains 1, and opening a newly warmed title reuses its background bytes. Native evidence: .tmp/task387-scroll-preload-final. All 27 focused queue/observer/loader checks pass; the production build/typecheck, formatting and git diff checks pass. The original synthetic tests used only fast successful covers and therefore missed this scheduling stall. No live user-library interaction was performed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Visible library backgrounds preload after 400 ms of settled browsing across all bundled desktop and fullscreen compositions. Fixed a follow-up stall caused by waiting indefinitely for every cover request: covers now get at most one additional second before the bounded two-job background queue proceeds. Revalidated with 27 focused tests and all 10 native cases, including slow offscreen covers in real Avalon shells; production build passes. Documentation includes the bounded wait and cache limits.
<!-- SECTION:FINAL_SUMMARY:END -->
