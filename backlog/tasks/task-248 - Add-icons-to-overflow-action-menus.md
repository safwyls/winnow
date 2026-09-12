---
id: TASK-248
title: Add icons to overflow action menus
status: Done
assignee:
  - '@codex'
created_date: '2026-09-12 19:10'
updated_date: '2026-09-12 19:13'
labels: []
dependencies: []
ordinal: 280000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Give overflow action options recognizable vector icons while preserving labels and interaction on fullscreen and desktop.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Fullscreen overflow actions display icons beside wrapped labels
- [x] #2 Desktop overflow options use matching icons where applicable
- [x] #3 Build and relevant menu checks pass
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Add shared vector icons beside fullscreen labels and desktop More items. Build, check existing menu tests and inspect renders.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Added shared theme-colored outline vector icons to fullscreen action panels and desktop details More options, including dynamic external links. Labels and accessible names remain unchanged. Fullscreen icons scale with text and disabled rows dim together. Build clean; 33 fullscreen/menu checks and 21 desktop details/link checks pass. Inspected normal and enlarged-text headless captures; no live library data touched.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added overflow option icons on fullscreen and desktop. Verified clean build, 54 focused passing UI checks and fullscreen renders.
<!-- SECTION:FINAL_SUMMARY:END -->
