---
id: TASK-251
title: Group feed actions beside Play over faded hero art
status: Done
assignee:
  - '@codex'
created_date: '2026-09-12 23:13'
updated_date: '2026-09-12 23:20'
labels: []
dependencies: []
ordinal: 283000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Implement the selected desktop feed design: horizontal feedback actions beside Play with divider, plus dimmed hero backdrop fading across each card.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Actions sit beside Play with divider and retain independent commands and receipts
- [x] #2 Hero art is dimmed and gradient faded, with plain surface fallback and safe cache lifecycle
- [x] #3 Desktop layout and interactions verified; fullscreen keeps existing hero and controller actions
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Move feedback controls into a wrapping bottom action row with a conditional divider. Add clipped low-opacity horizontal hero art using leased backdrop selection and view-size requests. Update focused card layout checks, render examples and document desktop/fullscreen behavior.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Desktop actions now share a wrapping bottom row, with secondary buttons kept together after a conditional divider. Shared leased landscape art uses source preferences, metadata and Steam hero fallbacks, dimmed to 22% with a left-to-right opacity mask. Background brush does not contribute natural image size. Detach cancels metadata and leases; stale completions ignored, reattach reacquires. Fullscreen retains its existing hero and controller actions; SupplementalFeedTests pass. Clean build; 19 focused UI tests pass. Inspected recent/recommendation captures with synthetic landscape artwork and checked 420/560-width geometry, hit testing and unchanged card dimensions. No live library data changed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Grouped actions beside Play with divider and added gradient-faded hero art. Verified 19 focused tests and rendered recent/recommendation cards.
<!-- SECTION:FINAL_SUMMARY:END -->
