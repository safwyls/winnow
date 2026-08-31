---
id: TASK-6
title: Replace UI-thread N+1 queries with bulk read models
status: To Do
assignee: []
created_date: '2026-08-29 21:52'
labels:
  - ui
  - data
dependencies: []
priority: medium
ordinal: 6000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The library and startup view models issue per-item queries on the UI thread. Replace these with bulk read models that load in a single round trip. Finding F13. Source: stabilization-2026-08-28.md Group 2. Trigger: next library or startup view-model change.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Library view populates from a single bulk query, not per-item fetches
- [ ] #2 No repository call executes on the UI thread
- [ ] #3 Startup time does not regress
<!-- AC:END -->
