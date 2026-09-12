---
id: TASK-252
title: Prevent backdrop downloads from blocking cached artwork
status: Done
assignee:
  - '@codex'
created_date: '2026-09-12 23:25'
updated_date: '2026-09-12 23:29'
labels: []
dependencies: []
ordinal: 284000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Investigate delayed feed backdrops and eliminate avoidable waiting behind unrelated image downloads while preserving artwork preferences, bounded processing and cache safety.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Reproduce cached artwork waiting behind slow downloads and verify it can complete independently after fix
- [x] #2 Decode limits, cancellation and existing artwork behavior remain covered
- [x] #3 Document measured evidence and explain remaining cold-download latency on both desktop and fullscreen
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Reproduce decode-slot blocking with one stalled source and one disk-cached image. Separate network waits from bounded decode/conversion while bounding downloaded bytes and preserving cancellation. Run cover/cache regression suites and desktop/fullscreen backdrop tests; document evidence and remaining cold-network delays.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Reproduced head-of-line blocking: one stalled fake source held the only decode slot; unrelated disk hit failed a one-second deadline before fix. Pipeline now takes decode gate only around disk read/decode/floor generation and conversion; network waits hold no decode slot. Fetch gate remains held through downloaded-byte consumption to bound queued payload memory. Same regression completed disk hit in 11.2ms after fix with source still stalled. 159 Covers tests pass, plus final34 UI tests covering cache lifetime/conversion and desktop/fullscreen/backdrop preferences/feed lifecycle. Stabilized prior card pointer fixture by allowing compositor publication. No live library or CDN timing measured; cold download/retry latency remains. Evidence in docs/spikes/backdrop-load-latency.md.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Removed download-induced blocking of cached backdrop decoding. Controlled regression improved from greater than1s blocked to11.2ms with network still stalled.159 cover tests and34 UI tests pass.
<!-- SECTION:FINAL_SUMMARY:END -->
