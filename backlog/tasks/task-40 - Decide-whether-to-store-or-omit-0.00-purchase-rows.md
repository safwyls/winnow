---
id: TASK-40
title: Decide whether to store or omit $0.00 purchase rows
status: To Do
assignee: []
created_date: '2026-08-29 21:54'
updated_date: '2026-08-29 21:55'
labels:
  - data
  - ingest
dependencies: []
priority: low
ordinal: 67000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The account-page importer drops $0.00 purchase rows. Whether to store a zero-dollar transaction or omit it entirely is a user-facing decision that has not been made. Neither path is tested. Source: ROADMAP.md section 6.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 A decision is recorded (store as zero, or omit with documented rationale)
- [ ] #2 The chosen behavior is tested
<!-- AC:END -->
