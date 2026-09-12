---
id: TASK-250
title: Compare feed card action layouts
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-12 23:09'
updated_date: '2026-09-12 23:11'
labels: []
dependencies: []
ordinal: 282000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Preview a sliding edge reveal and grouped actions beside Play using current feed card styling, to help choose a direction.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Interactive comparison shows both proposals with recent and recommendation card actions
- [ ] #2 Keyboard focus and reduced motion are considered; fullscreen implications are stated
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Build a standalone interactive comparison using the supplied card art and Winnow colors. Show recent versus recommendation actions, hover/focus reveal and reduced motion. Inspect in browser and recommend a direction.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Created standalone feed-card-actions.html in the task visualization directory, comparing hover/focus edge reveal with grouped actions beside Play. Includes recommendation toggle, held-open state and reduced-motion toggle. Fullscreen recommendation retains controller action panel. Browser URL security policy blocked local file inspection; visual verification remains pending. No production app changes made.
<!-- SECTION:NOTES:END -->
