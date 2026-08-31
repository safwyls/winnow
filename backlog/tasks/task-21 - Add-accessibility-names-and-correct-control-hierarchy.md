---
id: TASK-21
title: Add accessibility names and correct control hierarchy
status: To Do
assignee: []
created_date: '2026-08-29 21:53'
labels:
  - accessibility
  - ui
dependencies: []
priority: medium
ordinal: 46000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Views lack accessibility names and the control hierarchy does not correctly express the semantic structure to screen readers and automation. Finding F35. Source: stabilization-2026-08-28.md Group 2. Trigger: next view-authoring pass.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Every interactive control has an accessibility name
- [ ] #2 The control hierarchy expresses the semantic structure (headings, groups, lists)
- [ ] #3 A screen reader can navigate the primary views meaningfully
<!-- AC:END -->
