---
id: TASK-247
title: Show fullscreen actions in a sliding edge overlay
status: Done
assignee:
  - '@codex'
created_date: '2026-09-12 18:58'
updated_date: '2026-09-12 19:07'
labels: []
dependencies: []
ordinal: 279000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Replace fullscreen More action navigation with a right-edge overlay that preserves the current page behind it, traps interaction, restores focus and respects reduced motion.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Action menus render as a right edge panel over the retained current page with theme styling and readable scrolling actions
- [x] #2 Controller keyboard pointer dismissal and action selection stay modal and restore focus without leaking input or losing nested navigation
- [x] #3 Slide entrance respects reduced motion; large text render checks and relevant regression tests pass
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Render action pages through the shell overlay while retaining the underlying page and stack semantics. 2. Add edge layout and motion, modal input and focus restoration. 3. Verify action/dismissal/navigation behavior and render normal/large text; update docs and commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Shared fullscreen action pages now render in a right-edge shell overlay over the retained disabled page. Panel supports wrapped scrolling actions, disabled styling, Close/B/Escape/outside dismissal, and restores the opener.180ms entrance is skipped for reduced motion. Content respects configured safe margins; desktop More remains its existing popup. Build clean; initial54 navigation/accessibility checks and final57 focused overlay/tool/navigation tests pass. Overlay suite includes13 cases for actual entering/settled translation, safe margins10%, UI/text scale, nested menus/pages, input trapping and invocation once. Inspected normal/140% and scrolled captures. Broad UI run found action-label representation compatibility failures, fixed by preserving string Content with a wrapping template; all affected tests now pass. The previously known FullscreenPlatformTests Open-versus-arrow failure remains.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Changed fullscreen More and shared action menus to a sliding right-edge overlay. Retains background page state, traps input, restores focus and respects reduced motion/safe margins. Clean build and57 final focused tests pass; normal and140% renders inspected. Existing unrelated FullscreenPlatformTests failure remains.
<!-- SECTION:FINAL_SUMMARY:END -->
