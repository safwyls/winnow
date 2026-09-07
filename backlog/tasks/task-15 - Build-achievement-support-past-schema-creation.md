---
id: TASK-15
title: Build achievement support past schema creation
status: To Do
assignee: []
created_date: '2026-08-29 21:52'
updated_date: '2026-09-06 16:21'
labels:
  - data
  - ingest
  - ui
dependencies: []
priority: medium
ordinal: 68000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Achievement tables exist in the schema but nothing populates or reads them. Finding F24. Source: stabilization-2026-08-28.md Group 2. Trigger: when achievements become scope.

Achievements became scope on 2026-09-06. They are not only a display feature: achievement progress is a stronger and fully retroactive commitment-shape signal for the recommender than playtime minutes, and it bears on the retired hard exclusion, so it affects feed correctness and not just the details modal. Steam global unlock percentages additionally give a cross-game normalised progress reading. TASK-137 is the scoring consumer and depends on this task; it needs the per-release schema, the user unlock state, and the global unlock percentage.

Confirmed 2026-09-06: `achievements` and `achievement_unlocks` exist in migration 0001 and `AchievementQueryRepository` reads them, but there is no write path anywhere in src/ and no Steam fetch (no GetPlayerAchievements, GetSchemaForGame or GetGlobalAchievementPercentagesForApp call exists). The tables are empty.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Achievement data is ingested from at least one store source
- [ ] #2 The achievement data is surfaced in the UI
- [ ] #3 Global achievement unlock percentages are stored alongside the per-release schema, not only the user unlock state
<!-- AC:END -->
