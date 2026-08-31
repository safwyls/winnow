---
id: TASK-30
title: Include counts in unread-update accessible copy
status: To Do
assignee: []
created_date: '2026-08-29 21:54'
labels:
  - accessibility
  - ui
milestone: m-4
dependencies: []
priority: medium
ordinal: 30000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Flare-marked unread-update counts are currently visual only; screen readers cannot access the count. Finding F48. Source: stabilization-2026-08-28.md Group 3.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Every flare-marked count has an accessible text equivalent (e.g., AutomationProperties.Name)
- [ ] #2 A screen reader announces the count, not just the presence of updates
<!-- AC:END -->
