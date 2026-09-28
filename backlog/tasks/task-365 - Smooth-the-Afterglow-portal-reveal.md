---
id: TASK-365
title: Smooth the Afterglow portal reveal
status: Done
assignee:
  - '@codex'
created_date: '2026-09-28 17:38'
updated_date: '2026-09-28 17:47'
labels: []
dependencies: []
references:
  - docs/spikes/2026-09-27-afterglow-artwork-mock/README.md
type: bug
ordinal: 401000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The fixed-plane portal mock feels slow and jittery after TASK-364. Investigate animation pacing and rendering work while retaining the reveal, shape controls and stationary text.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The opening follows a consistent frame cadence and completes without extending its intended duration when frames are delayed.
- [x] #2 Per-frame work is reduced where measurements justify it, with before/after evidence and honest limits.
- [x] #3 Desktop and fullscreen keep the fixed non-scrolling details, shape controls, reduced-motion behavior and correct cleanup.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Measure existing scheduling and browser update timing, then correct the entrance clock and remove unnecessary mask work. Verify pointer and keyboard behavior on both surfaces, capture the result, document measurements and commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Found a frame gate that discarded fractional time: at simulated 60 Hz it produced 33.3/50 ms intervals and 42 draws in two seconds. Replaced entrance throttling with display-frame updates and a 360 ms wall-clock deadline. Ambient rendering retains a correctly accumulated 30 Hz schedule. Hover intent is 120 ms and prepares the shader. Settled text removes its mask; fallback mask updates only when needed. Cached DOM references eliminate repeated lookups. Shader skips star/cloud work beyond the invisible halo.

Evidence: controlled 60/120 Hz tests produced 16.7/8.3 ms entrance intervals and stable 33.3 ms ambient intervals. A 200 ms stall previously delayed completion to 783.3 ms; revised completion was 366.7 ms, the first available frame after the 360 ms deadline. Over 300 settled draw calls, DOM mask writes and queries each fell from 600 to zero. Opt-in browser probe measured desktop entrance 652.8 to 360.9 ms; CPU/render-submission median 0.3 to 0.2 ms and p95 0.5 to 0.4 ms in local 225/390-sample snapshots. GPU completion/presented FPS not measured. README documents the method and limitations.

Verification passed: desktop and fullscreen live pointer entrances retained 560/620 px fixed planes with zero scrollable descendants; compact 760x560 fullscreen kept the longest title legible at maximum roundness/waviness; defaults and keyboard focus verified. JSDOM covered 120 ms activation, no-GPU entrance completion and loop stop, reduced motion and Escape/ARIA cleanup. JavaScript syntax, Git whitespace and browser warning/error checks passed. Viewport reset, profiling query removed, capture 17 saved. Mock only.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Corrected uneven portal pacing and redundant DOM mask updates. The visible entrance now follows display frames, completes sooner using wall-clock time, and leaves stationary text after opening. Before/after scheduling and browser observations are documented; desktop/fullscreen and fallback/reduced-motion checks pass.
<!-- SECTION:FINAL_SUMMARY:END -->
