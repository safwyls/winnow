---
id: TASK-300
title: Cover desktop startup and fullscreen re-entry with a tracing dragon
status: Done
assignee:
  - '@codex'
created_date: '2026-09-16 01:17'
updated_date: '2026-09-16 01:34'
labels: []
dependencies: []
ordinal: 342000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Returning to fullscreen and desktop startup still expose feed layout flicker. Cover preparation on both surfaces with a brief tracing glow dragon presentation.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Every fullscreen entry covers refresh and layout, including rapid exit and re-entry.
- [x] #2 Desktop startup covers initial library and feed layout with recovery and responsive controls.
- [x] #3 Both surfaces show a theme-aware tracing dragon and respect reduced motion.
- [x] #4 Readiness and lifecycle tests pass and both surfaces are visually checked.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Share a tracing dragon control. 2. Cover fullscreen refresh and desktop initial preparation with cancellable transitions. 3. Test lifecycle and motion, inspect rendering, update specifications.

Cover return-to-desktop refresh with the same readiness presentation so both directions honor the requested short transition.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Focused startup and tracing cases passed. Initial full run with capture environment globally enabled exposed six existing capture-only fixture failures (including a 12-card assertion over a 10-card fixture); use focused capture runs and the normal full-suite configuration separately.

Implemented coverage on desktop startup and both mode-switch directions: wait library, primary feed and layout;350ms minimum plus180ms fade, skipped under reduced motion. Shared vector dragon uses a1.8-second contour glow with theme brushes. Retry, rapid re-entry, hidden/restore timing, close cancellation, first-run setup input and delayed-feed readiness verified. Final normal UI suite754 passed; architecture/documentation15 passed. Focused visual captures inspected on both surfaces and dark/light trace colors. Production app/data untouched.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added a tracing dragon loading presentation to desktop startup and every desktop/fullscreen transition. Primary feed/layout readiness gates reveal; cancellation, recovery, reduced motion and setup input remain intact. Verified754 UI tests,15 architecture/documentation checks and focused visual captures. Evidence: docs/spikes/loading-transitions.md.
<!-- SECTION:FINAL_SUMMARY:END -->
