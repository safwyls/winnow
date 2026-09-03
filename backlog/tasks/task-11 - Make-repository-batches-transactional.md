---
id: TASK-11
title: Make repository batches transactional
status: To Do
assignee: []
created_date: '2026-08-29 21:52'
labels:
  - data
dependencies: []
priority: medium
ordinal: 38000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Logical batches of repository writes do not commit atomically. A failure mid-batch can leave the database in an inconsistent state. Finding F18. Source: stabilization-2026-08-28.md Group 2. Trigger: next repository write path added or reworked.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 A logical batch of writes commits or rolls back as a unit
- [ ] #2 A simulated failure mid-batch leaves no partial state in the database
<!-- AC:END -->
