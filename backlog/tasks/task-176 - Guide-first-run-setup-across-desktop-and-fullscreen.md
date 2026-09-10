---
id: TASK-176
title: Guide first-run setup across desktop and fullscreen
status: Done
assignee:
  - '@codex'
created_date: '2026-09-10 17:04'
updated_date: '2026-09-10 17:23'
labels: []
dependencies: []
ordinal: 207000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Give new Winnow users an optional guided setup covering IGDB, Steam, Epic, GOG, theme, app preferences and library preferences. Every step and the whole wizard can be skipped; existing settings and sign-in flows remain the source of behavior.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A new data directory opens the wizard on the first visible launch; completion or skipping all persists, interrupted setup resumes, and existing installations are not interrupted.
- [x] #2 Desktop and fullscreen guide Welcome, IGDB, Steam, Epic, GOG, Theme, Application, Library and completion with accessible navigation and skip options.
- [x] #3 Steps reuse existing protected credentials, provider consent and settings behavior; GOG accurately describes local discovery and secrets are cleared on leaving.
- [x] #4 Setup can be reopened from Application Settings; saved settings survive skips and failures have recovery copy.
- [x] #5 Startup, persistence, navigation and both UI paths are tested; governing docs and README reflect the flow.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Implement a shared persisted wizard state and startup eligibility before ingest. Reuse settings models and existing setup views in separate desktop/fullscreen wizard presentations. Add replay entry points, input routing/focus and secret cleanup. Verify lifecycle, navigation, provider reuse and app boundaries; update documentation and run build/full tests.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented shared setup cursor with pre-ingest new-database eligibility, resume, skip-all completion, replay and failure recovery. Desktop reuses embedded platform/theme/app views with persistent navigation and IGDB Save/Remove/status outside scroll; verified all nine steps at 1200x604 client content (minimum 640px window minus caption). Fullscreen reuses nested provider/settings pages with retained cursor and controller navigation guards; visually checked at 1920x1080. Reviewer findings fixed: input keyboard above overlay, nested consent gamepad scope, visibility focus restoration, saved preference load ordering, and skipping after preference failure. Root tests verify temporary-SQLite lifecycle and real shell keyboard/modal behavior. Full solution test run pending.

Final verification: clean solution build, zero warnings/errors; full solution test run 4,406 passed and 2 Linux-only skips on Windows (3,885 core, 249 UI, 160 recommendation, 112 covers). Includes 11 lifecycle/persistence/recovery tests and 11 UI wizard tests, with actual MainWindow startup tested in desktop and fullscreen. Accessibility enforcement caught a missing Control accessibility view on the replay card; corrected and full suite passes. Desktop screenshots inspected at minimum height, fullscreen at1920x1080. No production data or real credentials used; live provider sign-in and physical controller behavior retain their existing separately tracked validation limits.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Built optional first-run setup for IGDB, Steam, Epic, local GOG discovery, theme, application and library preferences with shared resumable progress and separate desktop/fullscreen presentation. Every configuration step and the entire wizard can be skipped; saved choices remain, secret drafts clear, existing libraries are not interrupted, and Application Settings can replay setup. Existing consent, credential protection and settings commands are reused. Build clean; 4,406 tests pass, with 2 expected Linux-only skips; both surfaces visually checked.
<!-- SECTION:FINAL_SUMMARY:END -->
