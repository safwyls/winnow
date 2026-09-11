---
id: TASK-37
title: Evaluate GamesDB references for reversible cross-store identity links
status: Done
assignee: []
created_date: '2026-08-29 21:54'
updated_date: '2026-09-11 19:49'
labels:
  - resolve
  - enrich
dependencies: []
documentation:
  - game-library-design.md
priority: medium
ordinal: 87000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Acquire and validate independent native-store edition evidence for reversible cross-store identity automation. Preserve source releases, globally unique external IDs, explicit user decisions and ambiguous or unavailable edition mappings.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Record validated cross-store reference evidence without assigning another release's globally unique external ID.
- [x] #2 Define and test the evidence sufficient for automatic linking; ambiguous or conflicting edition mappings cannot auto-link.
- [x] #3 Qualified links use existing reversible identity operations and admission rules.
- [x] #4 Repeated observations are idempotent and preserve explicit user decisions.
- [x] #5 Verify library, queue, details and undo behavior on desktop and fullscreen.
- [x] #6 Report eligible, conflicting and unresolved coverage; no arbitrary queue-size reduction is required.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Acquire independent edition evidence from each release's native store identifiers. Extend the anonymous Epic CMS client to accept offer/page IDs only where namespace, catalog item ID and artifact AppName all exactly match the current launch triple. Add a strict separately versioned IGDB external lookup that accepts one explicit edition game ID with version parent/title and rejects multiple game mappings, ordinary game rows and incompatible cache payloads. Persist validated source references and hashes in migration0042 without using the legacy game_versions grouping ID or assigning external IDs to other releases. Revalidate source hashes, release identities and current user decisions inside reversible link transactions. Exercise missing, conflicting, stale and eligible fixtures, repeat/undo/user protection, and shared desktop/fullscreen projections; report fixture coverage without claiming live-library coverage.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: EnrichmentLookupPlanner routes Epic metadata through Steam/GOG references without writing external IDs. IdentityLinkRepository supplies reversible links. Completed TASK-70, TASK-83 and TASK-189 are the integration context; retired destructive TASK-5 is not a prerequisite. GamesDB game-level references do not by themselves prove edition equivalence.

PR-12 rebase review against merged PR-13 added a conservative release-version gate, transactional release/root evidence checks and pending-pair cleanup, plus integration with the shared startup/scheduled/account refresh pipeline. Broad GamesDB-only linking is deliberately not claimed complete: game-level references alone do not prove edition equivalence, and production edition-evidence acquisition remains outstanding.

Implemented independent native edition acquisition: exact Epic CMS catalog/namespace/artifact matches yield offer/page IDs; strict IGDB external lookup independently resolves each native ID to an explicit edition games.id and refuses legacy, expired, ambiguous and ordinary-game evidence. Migration0042 records source-generated cache references/hashes and expiry. Record/link transactions revalidate sources, release/work/store identity and existing user-decision safeguards. Legacy IgdbVersionId alone cannot auto-link. Focused validation passed:80 data/identity/CMS tests;311 IGDB tests including62 new edition cases;2 headless desktop/fullscreen tests run production acquisition and GamesDB sync, inspect library/Merges/details, invoke Separate again with real keyboard input and confirm no automatic relink. Public unauthenticated Fez CMS fields verified; no live IGDB credentials or private library coverage measured. Architecture and docs/spikes/native-edition-evidence.md updated. Awaiting coordinating agent's full Release suite before finalization and commit.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Implemented independent native store evidence for reversible GamesDB links. Exact Epic CMS catalog, namespace and artifact matches supply offer/page IDs; strict current IGDB external mappings must independently identify the same explicit edition game for both releases. Ambiguous, ordinary-game, missing or stale evidence remains reviewable. Migration 0042 records source hashes and expiry, and write transactions revalidate identity and source inputs without copying external IDs or overriding pins, rejections or separation history.

Final verification on Windows: Release build passed with 0 warnings and 0 errors. The complete suite passed 5,619 tests, including 520 UI tests; 2 Linux-only tests were skipped on Windows. All 42 migration hashes verified against baseline dbe9d9b. Focused evidence checks passed 83 data/identity/CMS, 311 IGDB, and 4 desktop/fullscreen composition and separation tests. Coverage fixtures distinguish eligible, conflicting, and unresolved pairs; private live-library coverage remains unmeasured.
<!-- SECTION:FINAL_SUMMARY:END -->
