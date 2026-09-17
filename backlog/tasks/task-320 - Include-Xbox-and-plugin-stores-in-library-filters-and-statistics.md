---
id: TASK-320
title: Publish Xbox imports promptly in library filters and statistics
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-17 02:55'
updated_date: '2026-09-17 03:04'
labels: []
dependencies: []
priority: high
type: bug
ordinal: 362000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
During Xbox end-to-end verification the user cannot find Xbox in platform filters or statistics and needs a verifiable list of imported titles. Library plugins must participate in the same shared store presentation and filtering paths as built-in integrations.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Xbox and other imported plugin stores appear with readable names in applicable library filters and statistics on desktop and fullscreen.
- [ ] #2 Store filtering and counts include plugin ownerships and respect existing grouping and visibility rules; built-in store behavior remains covered.
- [ ] #3 The user receives a title list from the actual imported library, with any difference from the temporary live probe explained.
- [ ] #4 Focused regression checks cover both presentation paths and an updated isolated local client is built for verification.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Diagnose the configured Xbox test profile and trace plugin refresh ordering. 2. Let plugin inventory imports run after discovery without waiting for unrelated remote enrichment; keep resolver writes serialized, publish imported rows before optional metadata/artwork, and ensure new account refreshes are not delayed by a previous enrichment sweep. 3. Verify Xbox appears through existing dynamic desktop/fullscreen filters and Gameplay store controls, preserving session-only gameplay metrics and Steam-only spending scope. 4. Rebuild/relaunch the isolated client with its existing test profile, verify real persisted Xbox titles, and deliver a readable title list.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Current test profile has a protected Xbox credential and both history options enabled, but zero Xbox ownerships/cache rows. Filters, store counts and Gameplay selectors already derive arbitrary store keys from tiles. Plugin refresh currently waits for all startup, which includes OwnershipRefreshCoordinator downstream enrichment across the Steam library, and only publishes after its complete metadata/artwork sweep. Reported absence is an import scheduling/publication defect, not a store allowlist. Settings Platforms native cards and Steam spending are separate existing surfaces.

Implemented PluginRefreshCoordinator with independent inventory and enrichment queues. Imports wait for plugin discovery only, retain LibrarySyncGate for resolver writes and publish before enrichment. A new account refresh can run while an older metadata sweep is blocked. Completing built-in startup queues another enrichment sweep so early discovery does not miss later Steam/Epic/GOG additions. 21 focused scheduling/import/identity/action checks passed. Presentation audit confirmed library PLATFORM and Gameplay choices already support plugin:xbox; unit regressions for those paths passed separately.

The original running build eventually persisted 70 Xbox entries after the long startup queue cleared, confirming the diagnosed delay. Exported the actual persisted title list to ignored artifacts/xbox-shared-e2e/Imported-Xbox-Titles.md and opened it for the user. Entries include 51 PC package identities and 19 Xbox title identities; history also contains demos/betas and companion apps. 39 filter/Gameplay unit tests and 24 desktop/fullscreen UI tests passed, in addition to the 21 scheduling/import checks.
<!-- SECTION:NOTES:END -->
