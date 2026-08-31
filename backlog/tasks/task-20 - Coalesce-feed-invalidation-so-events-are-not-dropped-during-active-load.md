---
id: TASK-20
title: Coalesce feed invalidation so events are not dropped during active load
status: To Do
assignee: []
created_date: '2026-08-29 21:53'
labels:
  - recommend
  - ui
dependencies: []
priority: medium
ordinal: 20000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Feed invalidation events that arrive while a load is already in progress can be dropped, leaving the feed stale until the next manual or timed refresh. Finding F34. Source: stabilization-2026-08-28.md Group 2. Trigger: next feed refresh change.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 An invalidation event received during an active load is queued and replayed after the load completes
- [ ] #2 A test demonstrates that rapid invalidation during load does not lose the final state
<!-- AC:END -->
