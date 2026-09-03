---
id: TASK-23
title: Enforce single-instance execution
status: To Do
assignee: []
created_date: '2026-08-29 21:53'
labels:
  - infra
dependencies: []
priority: high
ordinal: 23000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
No single-instance guard exists. Running two copies of Winnow simultaneously duplicates session recording, scheduler work, and can corrupt the database. Finding F39. Source: stabilization-2026-08-28.md Group 2. Trigger: next startup composition change. Lands with F36 in the same startup pass.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 A second launch detects the running instance and activates it instead of starting a new one
- [ ] #2 Session and scheduler work is never duplicated
- [ ] #3 A test or manual procedure demonstrates the guard
<!-- AC:END -->
