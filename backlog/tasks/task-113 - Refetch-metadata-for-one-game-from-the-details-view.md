---
id: TASK-113
title: Refetch metadata for one game from the details view
status: To Do
assignee: []
created_date: '2026-09-05 02:50'
labels:
  - ui
  - enrichment
dependencies: []
priority: medium
type: feature
ordinal: 140000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Enrichment runs on its own schedule. When a game has stale, partial or missing metadata there is no way to ask for it again — the only recourse today is the wrong-game control, which is for a different problem (the resolution is wrong, rather than the data being thin). Add a manual refetch that re-asks the sources for this one game.

Note the interaction with TASK-89: a pinned work must refetch against its pinned IGDB id, not re-resolve. The pin says which game it is; the refetch says fetch it again.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The details modal offers a refetch for one game
- [ ] #2 A pinned work refetches against its pinned IGDB id and the pin survives
- [ ] #3 The control reports what happened — updated, nothing new, or could not reach the source
- [ ] #4 It respects the existing rate limits and cannot be used to hammer a source
- [ ] #5 Progress is stated in words, per the indeterminate-progress rule settled in TASK-79
<!-- AC:END -->
