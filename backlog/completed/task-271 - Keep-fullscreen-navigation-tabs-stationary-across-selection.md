---
id: TASK-271
title: Keep fullscreen navigation tabs stationary across selection
status: Done
assignee:
  - '@codex'
created_date: '2026-09-13 21:57'
updated_date: '2026-09-13 22:03'
labels: []
dependencies: []
ordinal: 313000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Reserve stable tab geometry across selection and focus.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Tab and trigger positions remain stable at supported text sizes.
- [x] #2 Selection emphasis is retained and desktop scope documented.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Reserve regular and bold dimensions in navigation labels and verify layout regression tests.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Added a shared tab factory and label that reserves normal/bold dimensions at the current font and text size. Applied to root navigation, Library collections, Activity, Settings, Details, and summary sections. Details update badges keep fixed dot weight and preserve composed content. Desktop uses separate templates and is unchanged. Inspected rendered navigation at 140 percent text and populated Details Updates at 100 percent; six details capture checks pass.

Release build and all 659 UI tests pass, including five new geometry checks across selection/focus, text scales 70/100/140 percent, and the real Details unread badge. Exact tab rectangles and full strip bounds remain equal through selection changes. Existing desktop interaction tests also pass.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Fullscreen tab labels reserve regular and bold dimensions, preserving selected emphasis without moving neighboring tabs or trigger markers. Shared navigation strips and Details badge are covered; all 659 UI tests pass.
<!-- SECTION:FINAL_SUMMARY:END -->
