---
id: TASK-328
title: Add persistent artwork slots and compatible browser provider capabilities
status: Done
assignee:
  - '@codex'
created_date: '2026-09-17 16:10'
updated_date: '2026-09-17 16:50'
labels: []
dependencies: []
documentation:
  - doc-1
type: feature
ordinal: 370000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Artwork selection currently supports user cover/background imports and an unpaged plugin artwork list. A shared browser needs persistent hero, cover and icon choices with provenance, offline retention and compatible provider capabilities. Product proposal: doc-1.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Hero, Cover and Icon choices retain asset provenance and downloaded images through restart, refresh, offline use, credential removal and provider disablement.
- [x] #2 Manual choices take priority over collection assignments and automatic sources; resetting one slot restores its automatic behavior without changing other slots.
- [x] #3 Providers declare supported slots and expose browsable candidates with paging and attribution where available; existing SDK artwork plugins remain compatible.
- [x] #4 Confirmed linked copies share displayed choices with documented and tested unlink behavior; artwork matching never changes library identity.
- [x] #5 Windows jump-list artwork uses a selected icon with existing cover fallback; desktop and fullscreen can read and save the same icon choice.
- [x] #6 Focused persistence, plugin-compatibility and refresh tests pass; relevant architecture and plugin documentation describes shipped behavior.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add persistent artwork choices and precedence with an append-only migration and repository. 2. Add optional paged provider SDK contracts without changing legacy provider methods. 3. Wire app projections, offline image imports and jump-list icon selection. 4. Verify persistence, linked-game behavior and plugin compatibility, and update documentation.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Core/data and app foundations implemented. Persistent choice tests pass 8/8, all44 migration hashes verify, validated local image tests pass10/10 and App build passes with no warnings. Both library instances use shared saved-choice projections; full icon selection interaction verification follows in dependent TASK-329. SDK compatibility and app integration tests are being completed.

SDK1.2 compatibility/catalog suite passed92 tests; app persistence/browser/source tests passed34; validated image cache tests passed10. Read-only group sharing, legacy reset and source-isolated Steam artwork are integrated. Review identified metadata import validation bypass; fixed to use full static image validation before replacing a choice, with regression tests pending.

Packaged-provider integration now passes 2/2: SteamGridDB setup retry, all slots, paging, attribution, bounded host filtering, and retained original images after credential removal and plugin disablement. Full main suite first run passed4957 with one identity inventory failure; added explicit policies and inventory now passes5/5. Review fixes synchronize editor saved values after browser commits while retaining text drafts.

Final solution build passed with zero warnings/errors. Main suite passed4960; provider catalog92; image pipeline191. Persistence covers restart/refresh/offline local retention, priority/reset/link/unlink behavior; packaged provider tests cover credential removal and disablement. Shared icon slots are exercised in browser tests and used by the Windows jump-list projection, retaining the cover fallback. All44 migration hashes verify. Full UI suite still running before terminal status.

Final full solution suite passed6631 tests with0 failures and2 expected Linux-only skips on Windows; all832 UI tests passed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added revisioned per-slot artwork persistence, legacy-import migration, confirmed-group sharing/reset, durable validated local images, selected jump-list icons and compatible SDK1.2 paged browser contracts. Verified main4960, plugin catalog92, cover pipeline191 and full832 UI tests;44 migration hashes and zero-warning solution build pass.
<!-- SECTION:FINAL_SUMMARY:END -->
