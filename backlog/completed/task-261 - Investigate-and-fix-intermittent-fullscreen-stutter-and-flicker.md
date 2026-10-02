---
id: TASK-261
title: Investigate and fix intermittent fullscreen stutter and flicker
status: Done
assignee:
  - '@codex'
created_date: '2026-09-13 18:55'
updated_date: '2026-09-13 19:07'
labels: []
dependencies: []
ordinal: 303000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
User supplied Winnow_wEa8xUcVxN.mp4 showing intermittent stutter/flicker after vertical row transitions. Inspect recording, correlate visible failures with rendering/layout/cover lifetimes, reproduce concrete causes and fix verified issues. Assess fullscreen and shared desktop impact.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Recording observations and reproducible causes are distinguished from unverified hypotheses.
- [x] #2 Verified rendering/navigation causes are fixed with targeted regression evidence and reduced-motion coverage.
- [x] #3 Document measurements and limitations, validate relevant desktop/fullscreen behavior, and report findings.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Extract timestamped frames from supplied clip; inspect row movement, queued layout/rebuilds and cover loading in parallel. Reproduce observed issues in isolated tests with timing/render evidence. Fix confirmed causes, verify targeted tests and renders, record findings and commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Inspected user recording frame strips: one-frame placeholder grid at33.533s (next frame art restored), repeated card positions around55.2s. Kept raw video and extracts outside repo. Reproduced pre-layout row-coordinate discontinuity, animation clock consuming preparation, duplicate hero/footer tree work, interrupted A-B-C backdrop promotion, synchronous metadata on dispatcher, and premature240px then480px cover requests. Fixed with arranged-origin transforms/render-frame scheduling, presentation reuse, queued latest ready backdrop, cancellable off-dispatcher metadata, actual-bounds cover requests and one paint per Art settlement. Tests reproduced backdrop and cover-request failures before fixes. Reviewer findings on ready-art failed upgrades, resized candidate invalidation and partial metadata success fixed with regression coverage. No desktop production changes; desktop/fullscreen cover selection and lifetime parity included. Final Release solution build:0 warnings/errors. Combined relevant UI run:273 passed,0 failed/skipped,31s; includes real scheduled-frame completion. Timing fixture data and limits in docs/spikes/fullscreen-stutter-2026-09-13.md. No claim of stable FPS improvement or elimination of every cold-cache placeholder flash; real library/GPU behavior not remeasured.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Investigated clip and fixed reproducible fullscreen animation, backdrop-loading and redundant-work faults. Recording observations and attribution limits documented. Release build clean and273 relevant UI tests pass; physical real-library recheck remains a validation limitation.
<!-- SECTION:FINAL_SUMMARY:END -->
