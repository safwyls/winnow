---
id: TASK-360
title: Move Afterglow mock card captions into descriptive flyouts
status: Done
assignee:
  - '@codex'
created_date: '2026-09-27 18:32'
updated_date: '2026-09-27 18:41'
labels: []
dependencies: []
references:
  - docs/spikes/2026-09-27-afterglow-artwork-mock/README.md
type: spike
ordinal: 396000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The user finds hover captions obscure the new foil finish and wants the same game information plus a description beside each artwork card.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Grid artwork remains unobstructed while a side flyout shows the full title, metadata, revisit reason and game description on hover or keyboard focus.
- [x] #2 The flyout flips or adapts to available space, stays readable while the pointer moves into it, dismisses with Escape and cleans up during scrolling, navigation, dialogs and rerenders.
- [x] #3 Desktop and fullscreen layouts, long titles, narrow windows, keyboard input and existing artwork effects are verified with documented visual evidence.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Add concise static game synopses to the mock fixtures; move grid captions to one body-level preview with viewport-aware placement and hover/focus lifecycle; preserve compact record captions; verify both layouts and capture evidence.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Replaced grid caption overlays with one fixed flyout outside the artwork and scrolling surfaces. It contains title, store, playtime, installed status, revisit reason and an original short game synopsis. Added descriptions to all 12 mock fixtures while preserving hero copy. The flyout uses a 180ms hover delay, remains hoverable, opens immediately on keyboard focus, flips at window edges, docks on very narrow windows, and supports Escape and Page Up/Down. Compact record text preferences now apply only to list rows. Verified desktop/fullscreen at 2052x1272, fullscreen at 760x560 and desktop at 390x700. Checked right/left placement, complete long title, pointer entry/exit, Escape without navigation, Enter opening correct game, dialog/navigation/rerender cleanup, constrained scrolling, missing artwork without shader, inline record captions, and independent list preferences. Existing foil and tilt remain visible with no grid caption area. JavaScript syntax/whitespace checks passed and browser reported no warnings/errors. Screenshot 13-side-preview.png and mock README record evidence. Physical touch, OS reduced-motion switching and screen-reader output were not exercised. Production frontends are unchanged.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Moved Afterglow mock game information into a responsive side flyout with full descriptions, leaving foil artwork clear. Verified pointer and keyboard behavior, long titles, both surfaces, narrow fallbacks and cleanup; saved review evidence.
<!-- SECTION:FINAL_SUMMARY:END -->
