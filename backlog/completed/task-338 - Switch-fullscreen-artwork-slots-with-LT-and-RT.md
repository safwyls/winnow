---
id: TASK-338
title: Switch fullscreen artwork slots with LT and RT
status: Done
assignee:
  - '@codex'
created_date: '2026-09-17 18:06'
updated_date: '2026-09-17 18:08'
labels: []
dependencies: []
ordinal: 380000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Make fullscreen artwork slot navigation available through the controller triggers, consistent with other fullscreen sections.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 LT and RT cycle backward and forward through Hero, Cover and Icon with wraparound and visible selected-slot focus.
- [x] #2 Trigger navigation preserves slot browsing state, does not switch during artwork writes and is covered by fullscreen interaction tests; desktop controls remain unchanged.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Handle PagePrevious/PageNext in FullscreenArtworkPage, show trigger hints around slot tabs, verify cycling and write guard through fullscreen input, and update visual specification.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
FullscreenArtworkPage consumes PagePrevious/PageNext (existing LT/RT and PageUp/PageDown mapping), wraps the slot sequence and focuses the selected slot after rebuilding. Reuses existing ChooseSlot command/state and ignores trigger changes while artwork writes are busy. Added trigger glyphs beside the slot row and footer hint. Desktop interaction remains unchanged and is covered by the same 23-test artwork suite. New fullscreen-host test verifies both directions, wraparound, focus, retained hero preview/scroll offset, no writes from navigation and busy guard. Reviewed rendered 1920x1080 capture at C:\Temp\winnow-artwork-triggers\artwork-fullscreen-trigger-slots.png. Solution build: zero warnings/errors. Updated visual specification.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added LT/RT artwork slot cycling with wraparound, focus and visible hints in fullscreen. Verified preserved slot state and busy-write guard with 23 passing artwork tests and a clean build.
<!-- SECTION:FINAL_SUMMARY:END -->
