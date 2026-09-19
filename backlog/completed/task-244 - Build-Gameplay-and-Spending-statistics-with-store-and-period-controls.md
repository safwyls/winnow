---
id: TASK-244
title: Build Gameplay and Spending statistics with store and period controls
status: Done
assignee:
  - '@codex'
created_date: '2026-09-12 15:36'
updated_date: '2026-09-12 15:58'
labels: []
dependencies: []
ordinal: 285000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Implement the first scope recommended by TASK-243: shared Gameplay/Spending views on desktop and fullscreen. Gameplay shows recorded hours, top games, session lengths and current library composition with store and 30-day/90-day/custom period controls. Keep supported spending sources and currencies explicit.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Scoped gameplay aggregates correctly handle store identity, linked games, interval clipping, invalid/open/duplicate sessions and current library counts without mixing cumulative playtime into recorded hours.
- [x] #2 Desktop and fullscreen expose Gameplay and Spending with independent view state, store and period controls, accessible charts, accurate coverage and loading/empty/error states.
- [x] #3 Existing Steam spending and currency behavior remains available without implying unsupported Epic/GOG spending.
- [x] #4 Tests exercise aggregates and both presentation paths; render and inspect desktop/fullscreen, update current docs and record a synthetic performance check.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Agree a scoped gameplay aggregate contract and implement clipped completed-session totals, period bins, top games and session-length metrics with data tests. 2. Build shared Gameplay/Spending state and charts with store and 30/90-day/custom controls, current library composition, accurate coverage, and independent desktop/fullscreen presentation. 3. Preserve Steam spending source/currency behavior and integrate refresh/cancellation and controller navigation. 4. Validate data edge cases, interactions, large synthetic history and rendered desktop/fullscreen layouts; update current design/architecture/roadmap docs and commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented bounded session aggregates and separate Gameplay/Spending state on desktop and fullscreen. Data+VM focused run: 33 passed; UI integration run: 69 passed including store/date controls, controller text entry, currency switching, retry/cancel, stale reads and previews. Synthetic 10k ownerships/100k sessions measured roughly 0.8s (30d) and 2s (90d) on worker reads; evidence in docs/spikes/gameplay-statistics-validation.md. Final full-shell visual checks and broader regressions in progress. Full App run found the new identity inventory registration (now added) and the pre-existing PluginSettingsView accessibility failure.

Final verification: solution build clean (0 warnings/errors); 11 repository tests passed, 27 VM/account/identity-inventory tests passed, and 73 focused UI tests passed. Inspected desktop1200/600, isolated fullscreen1920/1280 and real fullscreen shell100%/140% captures, including native selected styling, charts and custom controls. Broad App run:4652 passed with missing new inventory entry subsequently fixed and verified plus existing PluginSettingsView accessibility failure. Broad UI run:540 passed with existing FullscreenPlatformTests Open-versus-arrow expectation failure only. Desktop and fullscreen use independent selection/read state; completed-session history is explicitly partial and Spending remains Steam-only. No migration or new importer.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Built Gameplay and Spending in Stats on desktop and fullscreen. Gameplay adds store and 30/90-day/custom-date controls, clipped recorded hours, top games, session lengths/median and current library composition. Steam spending and currency behavior remain intact. Build clean; repository, VM, identity and 73 focused UI tests pass, with rendered full-shell 100%/140% validation and 100,000-session timing evidence. Two pre-existing unrelated broad-suite failures remain.
<!-- SECTION:FINAL_SUMMARY:END -->
