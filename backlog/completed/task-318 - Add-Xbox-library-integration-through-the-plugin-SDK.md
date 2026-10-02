---
id: TASK-318
title: Add Xbox library integration through the plugin SDK
status: Done
assignee:
  - '@codex'
created_date: '2026-09-17 01:33'
updated_date: '2026-09-17 02:21'
labels: []
dependencies: []
priority: high
type: feature
ordinal: 360000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Import Xbox PC games plus an optional console library alongside the existing integrations, with parity in supported import, metadata, account and game actions.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 An installable SDK plugin imports Xbox games with stable identities and truthful play and ownership facts.
- [x] #2 Desktop and fullscreen provide equivalent settings and supported game actions.
- [x] #3 Authentication and local discovery are covered by fixture tests and documented service limitations.
- [x] #4 Packaging, documentation, build and integration verification are complete.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Extend API 1 compatibly with optional account connection, protected secret writes, boolean settings and game-action contracts. Keep UI and persistence in the host.
2. Build a separately packaged Xbox plugin using read-only Windows discovery and an optional Microsoft public-client device sign-in. Import played history only with explicit opt-in and optional console inclusion. Preserve stable IDs, unavailable observations and account isolation.
3. Add cached Microsoft catalog metadata and artwork, available Xbox playtime, validated local launch and store navigation. Share host actions and session attribution across desktop and fullscreen.
4. Verify provider fixtures, real plugin loading, host import/launch and both settings surfaces, then build/package and update current documentation.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
User selected PC games plus optional console library. Researching account inventory evidence and required SDK extensions before implementation.

User explicitly approved scope exception: never-played uninstalled purchases are unavailable from supported Xbox history APIs. History is labelled and not presented as verified ownership. A configurable Microsoft public-client application ID is required for optional sign-in. PC launch/session tracking depends on readable registered Windows packages and process paths.

Added SDK 1.1 optional account and game-action contracts while retaining API/assembly major 1. Host caches validated action observations outside plugin-writable cache scope, checks ownership and active provider at dispatch, and carries history labels to both details surfaces. Initial host plugin sync/action/launch suite passed 20 tests using scratch output.

Provider verification: 97 Xbox tests passed, including 59 local tests. Read-only native scan saw 33 registered packages and one game (Solitaire); localized Notepad resources also resolved correctly without classifying it as a game. Public Microsoft catalog exact-PFN lookup succeeded. No authenticated sign-in or retail-game launch was attempted. Desktop and fullscreen account controls and source labels have headless interaction coverage and inspected captures. Reviewer confirmed SCID statistics correlation, legacy installation retention after disconnect, managed-secret removal and provisional-title promotion fixes. Release build passed with zero warnings/errors; 43 migration hashes verified. Full application tests passed 4805 tests. Final UI and generic plugin verification is ongoing after correcting readiness and too-short test deadlines.

Final scoped verification passed: 98 Xbox provider tests, 89 generic plugin tests, 4805 application tests, 4 focused package/action integration tests, and 37 UI checks covering plugin settings/source labels plus corrected cleanup/input tests. Release build has zero warnings/errors; all 43 migration hashes verify. Package created at artifacts/xbox-plugin/Winnow.Plugin.Xbox-1.0.0.zip. Full UI runs are not clean: unchanged baseline commit 78ed65bd035704aac1b0dd825d0a5b64960393d9 also reproduces fullscreen preparation failures. Additional intermittent journal/updater UI input failures remain documented; no clean full UI gate is claimed. Live authenticated Microsoft service access and retail-game activation/session monitoring remain unverified. Details and reproduction evidence: docs/spikes/xbox-integration-validation.md.

Pristine baseline UI verification completed: 788 passed, 3 failed of 791 at commit 78ed65bd. It reproduced two fullscreen preparation failures and the context-menu TempDatabase disposal lock. Baseline cursor/updater/journal activation tests passed in that run; their intermittent current-run failures are not asserted to be baseline-proven. Final scoped UI run passed all 37 tests. Baseline TRX: C:\Temp\winnow-xbox-baseline-1beb07b823b64e5690c52c610d501b62\test-results\baseline-ui.trx.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Delivered a separately packaged Xbox SDK plugin with installed PC import, optional PC/console played history, available playtime, Microsoft catalog artwork/metadata, protected device sign-in and validated PC launch/store actions. SDK 1.1 and shared host controls support both desktop and fullscreen. Verification: clean Release build, 98 provider tests, 89 host-plugin tests, 4805 application tests and 37 focused UI tests passed; 43 migration hashes verified and ZIP packaging checked. Public catalog and read-only native discovery smoke checks passed. Full UI runs retain independently reproduced baseline fullscreen failures and intermittent unrelated input failures. Live Microsoft sign-in and retail-game launch/session validation remain outstanding. Never-played uninstalled purchases are unavailable under the explicitly approved scope.
<!-- SECTION:FINAL_SUMMARY:END -->
