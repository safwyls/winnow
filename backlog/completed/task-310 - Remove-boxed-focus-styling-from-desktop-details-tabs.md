---
id: TASK-310
title: Remove boxed focus styling from desktop details tabs
status: Done
assignee:
  - '@codex'
created_date: '2026-09-16 15:30'
updated_date: '2026-09-16 15:31'
labels: []
dependencies: []
ordinal: 352000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The details tab outline crowds the existing active color and underline in the supplied screenshot.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Desktop details tabs have no enclosing outline and retain a visible keyboard focus cue.
- [x] #2 Fullscreen tab treatment is assessed and relevant interaction tests pass.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Replace the desktop focus outline with underline emphasis while preserving spacing; inspect fullscreen styling, update the visual specification and verify existing tab interactions with rendered captures.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Desktop: replaced the enclosing keyboard focus border with a bottom-only 2px cue, keeping tab dimensions stable. Inspected rendered keyboard-focus capture. Fullscreen: existing details tabs already use an underline plus SurfaceRaised focus fill; no equivalent enclosing border change needed. All 14 GameDetailsTabInteractionTests and FullscreenNavigationLayoutTests passed using scratch build output. Updated design-system.md.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Removed the desktop details tab box while retaining active color, underline and keyboard focus emphasis. Verified rendered output and 14 desktop/fullscreen interaction and layout tests.
<!-- SECTION:FINAL_SUMMARY:END -->
