---
id: TASK-250
title: Compare feed card action layouts
status: Done
assignee:
  - '@codex'
created_date: '2026-09-12 23:09'
updated_date: '2026-09-12 23:20'
labels: []
dependencies: []
ordinal: 291000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Preview a sliding edge reveal and grouped actions beside Play using current feed card styling, to help choose a direction.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Interactive comparison shows both proposals with recent and recommendation card actions
- [x] #2 Keyboard focus and reduced motion are considered; fullscreen implications are stated
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Build a standalone interactive comparison using the supplied card art and Winnow colors. Show recent versus recommendation actions, hover/focus reveal and reduced motion. Inspect in browser and recommend a direction.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Created standalone feed-card-actions.html in the task visualization directory, comparing hover/focus edge reveal with grouped actions beside Play. Includes recommendation toggle, held-open state and reduced-motion toggle. Fullscreen recommendation retains controller action panel. Browser URL security policy blocked local file inspection; visual verification remains pending. No production app changes made.

User selected actions beside Play after viewing comparison. Implemented and rendered in TASK-251; exploratory preview work concluded.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Compared edge reveal and inline actions; user selected inline actions with divider. Production implementation and visual checks completed in TASK-251.
<!-- SECTION:FINAL_SUMMARY:END -->
