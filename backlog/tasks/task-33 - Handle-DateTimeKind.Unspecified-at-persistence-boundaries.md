---
id: TASK-33
title: Handle DateTimeKind.Unspecified at persistence boundaries
status: To Do
assignee: []
created_date: '2026-08-29 21:54'
labels:
  - data
milestone: m-4
dependencies: []
priority: medium
ordinal: 52000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
`DateTimeKind.Unspecified` values can cross persistence boundaries without being rejected or normalized. This can cause silent misinterpretation of timestamps. Finding F45. Source: stabilization-2026-08-28.md Group 3.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Persistence boundaries reject `DateTimeKind.Unspecified` or normalize to UTC
- [ ] #2 The chosen strategy (reject vs. standardize on `DateTimeOffset`) is documented
- [ ] #3 A test demonstrates that an Unspecified DateTime does not persist silently
<!-- AC:END -->
