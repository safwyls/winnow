---
id: TASK-356
title: Prototype Afterglow cursor portals and expanded returning shelf
status: Done
assignee:
  - '@codex'
created_date: '2026-09-27 16:57'
updated_date: '2026-09-27 17:06'
labels: []
dependencies: []
references:
  - docs/spikes/2026-09-27-afterglow-artwork-mock/README.md
type: spike
ordinal: 392000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Iterate on the review mock with eight returning games, filled portrait frames and a PixiJS cursor portal revealing secondary artwork.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Discover shows eight returning titles and all cover images fill their stable 2:3 frames.
- [x] #2 A PixiJS effect follows the cursor on poster artwork, revealing alternate art through an amorphous portal while preserving captions and selection.
- [x] #3 Desktop and fullscreen mock layouts, keyboard access, effect disable/reduced-motion handling and renderer cleanup are verified and documented.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Update mock layout and fixtures; vendor PixiJS and local alternate artwork; implement one shared portal renderer with idle cleanup; inspect both surfaces in browser and document evidence.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Mock only. Added eight returning fixtures, filled portrait frames and PixiJS 8.21.0 portal with seven local secondary-art sources and procedural fallback. Browser verified desktop/fullscreen at 2052x1272 and 760x560: moving reveal, keyboard still portal, Still/Off controls, Enter preview, loading state and canvas cleanup on navigation/dialogs/records; no horizontal overflow, stable frames, pinned footer. One shared private renderer, capped ticker and no loop in Still mode. System reduced-motion preference uses verified Still branch; OS preference and physical touch were not exercised. Final shader run has no new warnings/errors. Node syntax and git whitespace checks pass. Evidence and screenshot in mock README.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Expanded the review mock to eight returning games, filled 2:3 covers and a customizable PixiJS cursor portal revealing alternate artwork or a star field. Desktop/fullscreen browser checks, keyboard/selection checks, static/off settings, cleanup checks and JS syntax validation passed; review evidence saved in 09-discover-portal.png.
<!-- SECTION:FINAL_SUMMARY:END -->
