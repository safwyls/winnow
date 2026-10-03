---
id: TASK-383
title: Correct Electron artwork loading and appearance interactions
status: Done
assignee:
  - '@codex'
created_date: '2026-10-03 17:14'
updated_date: '2026-10-03 17:47'
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
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented stable one-line review state in Avalon hover previews; continuous transparency with serialized latest-value saves and protection from stale preference refreshes; theme-painted Windows/Linux caption with native controls and no fullscreen strip. Shared artwork now keeps unchanged in-flight reads, retries transient selection errors, bypasses redundant browser lazy loading after decode, and caches backdrops through bounded leases. Details resolves alternate cover and hero metadata in parallel. Cache invalidations revalidate pending and displayed backdrops without stranding them. Added static per-composition illustrations across shared cover/backdrop/screenshot/artwork-preview surfaces. Native testing also found and fixed artwork-picker focus escaping behind the dialog after save, restoring Escape without stealing nested-dialog focus. Updated design-system, Electron README, theme authoring docs and migration rationale. Final focused component run: 318 tests across 26 files passed. Production TypeScript/build passed with the two existing dependency comment-annotation warnings. Migration audit: 2435 methods, zero pending/partial. Placeholder native checks: 8/8 passed and composition screenshots inspected. Remaining native rerun is in progress.

Native verification complete: all 58 distinct targeted cases now pass with one worker and zero retries. Evidence is split across .tmp/task383-native (17 unchanged image-lifetime/recovery cases), .tmp/task383-placeholders-final (8 composition/mode cases), .tmp/task383-native-final (appearance5, artwork-state5, fullscreen3 initial cases, preview15), .tmp/task383-edge-final (all4 fullscreen transition cases), and .tmp/task383-preview-edge-final (both bottom-edge preview cases). Initial failures exposed fixture readiness, frozen animation timing and a fixed-position cover collapsing to2px; fixtures now preserve real pointer hit-testing, exact geometry and cache reuse assertions. The artwork picker focus failure was a production bug and is fixed, with native desktop/fullscreen and4 component cases. Final TypeScript and formatting pass. Validation uses isolated data directories, not the real library. Windows native behavior was exercised; Linux caption behavior is covered at the platform-policy level only. No numerical first-download speedup is claimed: provider/network latency remains, while request restart, cache reuse and parallel metadata behavior are deterministically verified.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Fixed all six reported Electron issues across desktop/fullscreen: stable review previews, responsive persistent transparency, themed caption, reliable visible-artwork recovery, cached and parallel Details backgrounds, and composition-specific missing-art illustrations. Also restored artwork-picker keyboard focus after saving. Verified 318 focused component cases, 58 distinct native cases, production build/typecheck and complete 2435-method migration coverage. Docs updated; first remote downloads remain provider-dependent.
<!-- SECTION:FINAL_SUMMARY:END -->
