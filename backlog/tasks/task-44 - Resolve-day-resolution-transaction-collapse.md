---
id: TASK-44
title: Resolve day-resolution transaction collapse
status: To Do
assignee: []
created_date: '2026-08-29 21:55'
labels:
  - data
dependencies: []
priority: low
ordinal: 44000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The account transaction fact tables cannot distinguish two identical same-day transactions, so an exact repeat purchase on one day is undercounted by one. This is a data fidelity limitation inherent in the current schema's day-resolution keying. Source: ROADMAP.md section 6 (within the account stats item).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Either the schema supports distinguishing same-day identical transactions, or the limitation is documented and tested as accepted behavior
<!-- AC:END -->
