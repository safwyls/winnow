---
id: TASK-379
title: Analyze the native Rift fullscreen graphics trace
status: Done
assignee:
  - '@codex'
created_date: '2026-09-29 02:34'
updated_date: '2026-09-29 02:59'
labels: []
dependencies: []
type: spike
ordinal: 415000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Analyze the user-provided TASK-378 startup trace to distinguish app rendering cost and presentation pacing from physical monitor flicker. Preserve the raw trace locally and avoid speculative graphics defaults.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Record capture completeness, frame timing, expensive work and relevant display-path evidence using reproducible sanitized analysis.
- [x] #2 Identify an evidence-supported next step, with desktop and fullscreen coverage and clear limits on what the trace proves.
- [x] #3 If an app defect is established, implement and verify the targeted fix; otherwise document why no production change is justified.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Inspect capture loss and aggregate presentation, main-thread and compositor events. 2. Correlate timing outliers with app effects and verify Chromium event semantics against the matching source. 3. Apply a targeted correction only if the evidence supports it; record findings, checks and the remaining physical display validation.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Capture contains 20.016 seconds; the 200 MiB trace buffer filled and discarded 7669 chunks. DirectComposition present events are recorded. Begin-frame interval is 6061 us after startup (165 Hz). At 9.8-11 s the ambient portal produces 30 updates/s with low main-thread cost; at 13.24-14 s Chromium records 111 render surfaces, 110 for rounded corners, and 6185 render-pass draws across 64 displayed submissions. Source forces all resting effect cards into 3D transforms and runs two loading animations per cover. Apply a narrow layer/loading correction and verify the shared interaction and both Rift presentation modes; this does not establish a physical-flicker fix.

Implemented resting transform:none in the shared artwork surface and still loading indicators only inside Rift Library. Preserved active lift/tilt, foil, Discover shuffle and portal timing. Reproducible sanitized analysis includes 6206 render-pass events in the busy interval (6185 duration events plus 21 instant events). 49 focused tests and production build passed. Desktop and fullscreen browser fixtures showed resting transforms none and a focused card with a 3D transform plus material canvas; screenshot saved. The separate rift-trace-fix package matches all 52 output files. Native before/after speedup and physical flicker are not verified.

User hardware comparison: flicker remains in rift-trace-fix with normal DirectComposition. The layer/loading optimizations do not resolve physical monitor flicker.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Analyzed the truncated native capture, documented stable reported 165 Hz begin-frame cadence, intentional 30 Hz portal updates, and excessive gallery render surfaces/loading animations. Removed permanent resting-card 3D transforms and gallery loading motion. Reproduced sanitized results, passed 49 focused tests/build, verified desktop/fullscreen interaction structure, and packaged a separate hardware-comparison build. Physical flicker remains unresolved pending the user comparison.
<!-- SECTION:FINAL_SUMMARY:END -->
