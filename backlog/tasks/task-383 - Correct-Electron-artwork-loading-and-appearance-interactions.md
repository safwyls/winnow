---
id: TASK-383
title: Correct Electron artwork loading and appearance interactions
status: Done
assignee:
  - '@codex'
created_date: '2026-10-03 17:14'
updated_date: '2026-10-03 18:41'
labels: []
dependencies: []
priority: high
type: bug
ordinal: 461000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The user reports late review data reshaping hover previews, an un-draggable transparency slider, mismatched titlebar colors, library images appearing only after unrelated interactions, slow Details backgrounds, and generic missing-artwork surfaces. Address the reported behaviors across desktop/fullscreen and the four bundled compositions without hiding loading failures.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Hover previews keep stable geometry as local reviews load and preserve navigation and dismissal.
- [x] #2 Transparency supports continuous pointer dragging and keyboard changes with the final value persisted; titlebar follows the active appearance.
- [x] #3 Visible library artwork loads without requiring scroll, resize or opening Details, and recovers from transient selection/image failures with bounded work.
- [x] #4 Details backgrounds avoid unnecessary sequential loading delays and remain correct under quick navigation and cancellation.
- [x] #5 Unavailable artwork has a deliberate composition-specific placeholder throughout cover and backdrop surfaces, including a starfield for Rift, with titles and accessibility preserved.
- [x] #6 Relevant tests and rendered checks cover desktop/fullscreen; current behavior and validation are documented.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Investigate the shared selection/image pipeline and Details backdrop dependency chain. In parallel, isolate review-popover geometry, appearance dragging/titlebar propagation, and composition-specific missing-artwork rendering. Preserve cached binary image improvements and lifecycle bounds. Reproduce delayed responses and continuous drag in deterministic fixtures, inspect all four compositions on both surfaces, then integrate, document and commit the verified fixes locally.

Reopened after the user reproduced blank covers during sustained scrolling. Trace image-request and decode queues through repeated cancellation, reproduce with a large virtualized library and delayed image work, fix the confirmed stall, then verify continued progress and bounded resources on desktop/fullscreen.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented stable one-line review state in Avalon hover previews; continuous transparency with serialized latest-value saves and protection from stale preference refreshes; theme-painted Windows/Linux caption with native controls and no fullscreen strip. Shared artwork now keeps unchanged in-flight reads, retries transient selection errors, bypasses redundant browser lazy loading after decode, and caches backdrops through bounded leases. Details resolves alternate cover and hero metadata in parallel. Cache invalidations revalidate pending and displayed backdrops without stranding them. Added static per-composition illustrations across shared cover/backdrop/screenshot/artwork-preview surfaces. Native testing also found and fixed artwork-picker focus escaping behind the dialog after save, restoring Escape without stealing nested-dialog focus. Updated design-system, Electron README, theme authoring docs and migration rationale. Final focused component run: 318 tests across 26 files passed. Production TypeScript/build passed with the two existing dependency comment-annotation warnings. Migration audit: 2435 methods, zero pending/partial. Placeholder native checks: 8/8 passed and composition screenshots inspected. Remaining native rerun is in progress.

Native verification complete: all 58 distinct targeted cases now pass with one worker and zero retries. Evidence is split across .tmp/task383-native (17 unchanged image-lifetime/recovery cases), .tmp/task383-placeholders-final (8 composition/mode cases), .tmp/task383-native-final (appearance5, artwork-state5, fullscreen3 initial cases, preview15), .tmp/task383-edge-final (all4 fullscreen transition cases), and .tmp/task383-preview-edge-final (both bottom-edge preview cases). Initial failures exposed fixture readiness, frozen animation timing and a fixed-position cover collapsing to2px; fixtures now preserve real pointer hit-testing, exact geometry and cache reuse assertions. The artwork picker focus failure was a production bug and is fixed, with native desktop/fullscreen and4 component cases. Final TypeScript and formatting pass. Validation uses isolated data directories, not the real library. Windows native behavior was exercised; Linux caption behavior is covered at the platform-policy level only. No numerical first-download speedup is claimed: provider/network latency remains, while request restart, cache reuse and parallel metadata behavior are deterministically verified.

User reports that the previous patch still loads artwork initially but later scrolling leaves all new tiles blank. The screenshot shows loading surfaces rather than the unavailable-art illustrations. Reopened loading and verification criteria; prior passing checks did not cover this sustained failure.

Sustained native scrolling found an unsettled browser conversion after cancellation (one pending decode after 15 seconds while visible images were ready); instrumenting the native stage. A focused regression with six canceled, unsettled Image.decode promises fails before the fix because the seventh visible image cannot acquire a permit. Also fixing independently reproducible backend IGDB shared-wait cancellation gaps. Production background service predates this work and survives window restart; final verification must distinguish window/service restart.

Confirmed and fixed renderer starvation: native Electron fullscreen had a cleared image (empty src, natural dimensions 0, revoked Blob URL) whose native decode promise remained pending for 14259 ms, retaining a decode permit. Six such promises block all conversions in the red component regression. Cancellation now disposes the browser image and retires our decode operation without awaiting that cleared image promise; late completion remains harmless. Also fixed IGDB prewarm/full-batch caller waits: both new backend regressions failed before fixes and now prove six cancelled fallback requests release fetch permits before shared prewarm finishes. Shared batches continue for remaining callers.

Final validation: 109 focused Electron component tests passed; all 196 Winnow.Covers.Tests passed using scratch output; production TypeScript/build and complete migration audit passed (2435 methods, no pending/partial). All 19 distinct native artwork cases pass serially with zero retries: 17 lifetime/recovery cases in .tmp/task383-scroll-fixed and 2 sustained-scroll cases in .tmp/task383-scroll-final. Final stress fixture has1045games, two laps, deterministic late cancelled reads, actual Chromium decoding, selection invalidations, 121s freshness and301s collection clock advances. Desktop2005reads/960keys and fullscreen1504reads/736keys end with all visible images ready, pending work0, LRU33523200bytes. Eight released native promises remain unsettled in fullscreen without blocking progress. Initial fixture-only assertions requiring timed network cancellation were replaced by controlled initial reads. Native baseline evidence retained in .tmp/task383-scroll-instrumented/results.json. User library remains untouched; its running backend was built/started Oct2 and survives window restart. New backend code requires re-running Run.ps1 and Settings > Plugins > Restart library service. Controlled regressions establish these failure mechanisms, not a direct replay of the private library.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Fixed reported appearance/loading issues and the reopened sustained-scroll stall. Browser cancellation now releases decode permits after clearing/revoking images even when Chromium leaves decode() pending. IGDB shared waits stop holding cancelled request fetch permits. Verified with 109 focused component tests,196 backend cover tests,19 native artwork cases, build/typecheck and complete migration audit; earlier appearance/composition evidence remains above. Documented service restart so persistent development backends load the fix.
<!-- SECTION:FINAL_SUMMARY:END -->
