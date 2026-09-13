---
id: TASK-249
title: Use right-click back in fullscreen action panels
status: Done
assignee:
  - '@codex'
created_date: '2026-09-12 19:19'
updated_date: '2026-09-12 19:20'
labels: []
dependencies: []
ordinal: 290000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Remove the upper-right Close button from fullscreen action panels and support right-click and Escape through fullscreen back navigation.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Action panel has no header Close button
- [x] #2 Right-click and Escape navigate back once and respect active page handling
- [x] #3 Focused fullscreen navigation tests pass; desktop controls stay unchanged
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Remove the action header Close control. Route fullscreen right-click through controller Back handling; verify existing Escape path and add routed input coverage. Update layout assertions and fullscreen documentation.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Removed action-panel header Close and its focus stop. Fullscreen shell intercepts right-button press in the tunnel and routes through Handle(Back), respecting keyboards and page-specific cancellation. Existing Escape route already uses the same handler. Desktop input path unchanged. 43 focused UI tests pass, including right-click once-only dismissal/focus restoration and handled/unhandled page Back cases. Inspected 140% action-panel capture; header and safe margins correct.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Removed upper-right Close; right-click and Escape use fullscreen Back handling. Verified 43 focused passing UI tests and enlarged-text render.
<!-- SECTION:FINAL_SUMMARY:END -->
