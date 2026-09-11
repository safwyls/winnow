---
id: TASK-175
title: Configure IGDB credentials inside Settings
status: Done
assignee:
  - '@codex'
created_date: '2026-09-10 15:55'
updated_date: '2026-09-10 16:07'
labels: []
dependencies: []
ordinal: 206000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Provide desktop and fullscreen setup for user-supplied IGDB credentials using the existing protected local storage, instead of requiring environment variables.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Desktop and fullscreen offer client ID, masked secret, save/remove actions, setup link and clear status.
- [x] #2 Secrets use the existing protector and are never saved as plaintext; persistence failures are actionable.
- [x] #3 Saved credentials are consumed by the existing provider with clear activation behavior and configuration fallback handling.
- [x] #4 Relevant tests pass and setup documentation reflects both surfaces.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Use an App-owned IGDB settings service for protected transactional persistence and legacy migration, with a shared view model for editor state and commands. Expose desktop Application card and dedicated fullscreen page. Clear secret drafts on navigation and presentation changes. Explain restart activation and external configuration fallback. Verify persistence, controller entry, architectural enforcement and full solution tests, then update setup/design documentation.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Desktop: IGDB card in Application settings with masked fields and command/status bindings. Fullscreen: dedicated page reached from Application with shared model, explicit focus rows and controller keyboard routing. UI tests pass for desktop binding/validation and fullscreen masked editor routing. Persistence changes run in an ambient SQLite transaction and use the existing protector; no network validation or automatic restart is performed.

Review found retained secret drafts in persistent/cached views; fixed desktop hide, fullscreen detach and both presentation switches with regression tests. Initial full suite: all 238 UI tests passed; architecture enforcement found direct enrichment dependencies in the new view model, now being moved to an App service. README descriptive wording corrected to satisfy documentation enforcement.

Final validation: clean solution build (zero warnings/errors); 4,384 tests passed, 2 Linux-only tests skipped on Windows. Includes 13 credential persistence/migration/rollback cases and 3 UI tests covering desktop fields/status, fullscreen controller keyboard routing, and secret clearing on hide/detach/presentation changes. Architecture and documentation enforcement pass after moving persistence behind IIgdbSettingsService. Reviewer retention findings resolved. Verification used fake credentials and temporary SQLite data; no live Twitch authentication was attempted.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added IGDB metadata setup in desktop and fullscreen Application Settings. Shared commands save/remove protected credentials through an App service and atomic SQLite transaction, preserve legacy migration and external configuration fallback, and explain restart activation without claiming validation. Secret fields stay masked and drafts clear when leaving. Documentation updated. Build clean; all 4,384 tests pass with 2 expected Linux-only skips.
<!-- SECTION:FINAL_SUMMARY:END -->
