---
id: TASK-376
title: Reduce Rift shuffle rendering churn
status: Done
assignee:
  - '@codex'
created_date: '2026-09-29 01:38'
updated_date: '2026-09-29 01:44'
labels: []
dependencies: []
type: bug
ordinal: 412000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
On an AW3423DWF with RX 9070 XT, HDR and VRR off, disabling DirectComposition stops reported fullscreen flicker but shuffling can hitch. Discover currently recreates the portal renderer per selection and includes stacking order in its animation keyframes. Reduce avoidable work without changing GPU defaults or claiming the monitor symptom is solved.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Discover reuses its portal renderer when cycling games, while artwork, text and details activation follow the latest selection on desktop and fullscreen.
- [x] #2 Deck animation contains only transform and opacity changes; stacking order is restored after completion, interruption and reduced motion.
- [x] #3 Record focused tests, build and browser checks separately from the user-reported native flicker results.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Preserve the Discover portal during selection changes; keep deck stacking static during each animation; test lifecycle and interruptions, verify both views, document the diagnostic limits and commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Regression test reproduced the previous portal remount on selection. Discover now keeps it alive, with a separate portal test proving one renderer creation and no destruction while artwork/text update. Deck keyframes now contain only transform/opacity, with temporary stacking restored on completion, cancellation and reduced motion. All 20 app/deck/portal tests passed, and the production build passed with existing Zod annotation notices. Browser checks covered desktop/fullscreen selection, rapid reversals, focus and immediate details; shared mock checks covered both modes and stacking cleanup. Packaged a separate Windows build at src/Winnow.Electron/release/rift-smooth/win-unpacked using the already staged backend and notices; all 52 frontend output files matched app.asar byte-for-byte. Physical monitor flicker and native frame pacing require user retest. No GPU default or driver switch was added.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Reduced avoidable shuffle work by retaining the Discover portal renderer and keeping stacking out of animation keyframes. Verified 20 focused tests, build, desktop/fullscreen browser behavior and package contents. A separate portable build is ready for the same DirectComposition-disabled hardware comparison; native flicker is not claimed fixed.
<!-- SECTION:FINAL_SUMMARY:END -->
