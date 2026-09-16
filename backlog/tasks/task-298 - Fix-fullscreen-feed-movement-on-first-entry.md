---
id: TASK-298
title: Fix fullscreen feed movement on first entry
status: Done
assignee:
  - '@codex'
created_date: '2026-09-16 00:41'
updated_date: '2026-09-16 00:50'
labels: []
dependencies: []
type: bug
ordinal: 340000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
On first entering fullscreen, the feed row repeatedly jerks vertically and settles after navigation. User supplied explorer_0oPDEL6ciG.mp4. Identify the startup layout or animation cause and fix it without masking intentional shelf transitions.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 First fullscreen entry keeps feed geometry stable once the viewport is established, including asynchronous artwork and initial focus.
- [x] #2 Shelf navigation, fullscreen re-entry, scaling and reduced motion retain expected behavior; desktop is assessed independently.
- [x] #3 Regression demonstrates the cause and fix; relevant UI tests and visual checks pass, with findings documented.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Inspect recording and trace initial fullscreen layout, focus and artwork updates. 2. Reproduce the responsible feedback or animation path in a UI regression and fix its owner. 3. Verify startup, navigation, reduced motion and desktop isolation; document and commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
The recording shows about 168 native pixels of upward cover movement at 11.200 seconds, restored by 11.267, with stable hero/footer and cover size. Home rebuilt during feed updates with unconstrained height and stretch alignment, then corrected geometry in a background dispatcher callback. Home now resolves geometry during measurement and reconfigures capacity without replacing its layout. True tail appends preserve current covers and focus; per-generation snapshots remain safe across hidden-page updates. Original-code regression failed all three refresh cases (viewport Y633.045 to432.510, height398 to634); the fixed version preserves every sampled rectangle. Added insertion, delayed append and hidden shrink/re-entry coverage. Review found no remaining blocker. Validation: 47 focused Home/Browse/Stutter/Scale tests passed; full UI suite passed 725 tests with no skips or failures. Normal and 140% text captures inspected. Desktop behavior is covered by the full UI suite and no desktop layout source was changed. Updated design-system.md and measurement evidence in docs/spikes/fullscreen-feed-first-entry.md. Whitespace checks passed. Production app/data untouched; unrelated feed-card edits excluded.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Fixed fullscreen feed jumps caused by rebuilding a stretched shelf before a background sizing correction. Resolve Home geometry during measurement and retain the current row when later shelves append. Demonstrated failing intermediate layouts on original code and stable bounds after the fix. All 725 UI tests passed; rendered normal/large-text checks and independent review completed.
<!-- SECTION:FINAL_SUMMARY:END -->
