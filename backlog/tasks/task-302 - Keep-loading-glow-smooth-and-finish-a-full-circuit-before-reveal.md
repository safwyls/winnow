---
id: TASK-302
title: Keep loading glow smooth and finish a full circuit before reveal
status: Done
assignee:
  - '@codex'
created_date: '2026-09-16 01:52'
updated_date: '2026-09-16 02:01'
labels: []
dependencies: []
ordinal: 344000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Extended loading previews reveal a pause in the UI-thread-driven dragon animation during startup. Reveal should wait for data/layout readiness and at least one visible complete glow circuit, without stopping or restarting when load finishes.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Loading glow continues independently of UI-thread startup work, with evidence covering an induced UI stall.
- [x] #2 Desktop and fullscreen reveal only after readiness and a complete animation circuit, except reduced motion or hidden/cancelled presentation.
- [x] #3 Glow remains continuous through reveal and extended previews; lifecycle, motion, theme and contour coverage stay correct.
- [x] #4 Relevant tests and visual checks pass, and documented timing and rendering behavior match implementation.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Trace UI-thread animation scheduling and move the glow to compositor-owned rendering. 2. Gate both reveal paths on actual circuit completion and retain animation through fade. 3. Verify stalls, fast/slow loads, reduced motion and cancellation; update specs and commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Native isolated Windows probe reproduced the UI scheduling stall:0 frames during1200ms UI blockage; compositor path drew195 frames on a distinct render thread and advanced phase0.0873→0.7624. Both completed circuit flags verified. Focused29 tests passed before final mutable-brush/capture assertion refinement. New readiness gate replaces temporary5000ms desktop override as requested.

Completed compositor rendering and full-circuit gates on both surfaces. Trace keeps moving through reveal; theme/size changes preserve phase. Final full UI suite759 passed, architecture/documentation15 passed, final shared-mark capture/mutable-theme tests7 passed. Native UI-stall comparison and visual inspection recorded in docs/spikes/loading-animation-render-thread.md. Review found no blocking lifecycle issues; production app/data untouched.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Moved glow animation off the UI thread and replaced timed splash minimums with a completed rendered circuit plus data/layout readiness. Native probe measured195 frames during a1200ms UI stall versus0 with the prior scheduling path. Verified759 UI tests,15 architecture/documentation checks, and final visual captures.
<!-- SECTION:FINAL_SUMMARY:END -->
