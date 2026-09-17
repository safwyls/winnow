---
id: TASK-330
title: Verify SteamGridDB collection enumeration and matching feasibility
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-17 16:11'
updated_date: '2026-09-17 16:34'
labels:
  - blocked
dependencies: []
references:
  - 'https://www.steamgriddb.com/api/v2'
documentation:
  - doc-1
type: spike
ordinal: 372000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Library-wide collection application depends on retrieving all collection assets and reliable game identifiers. Public pages and third-party downloaders demonstrate a use case but do not establish a supported interface; verify this before committing to a collection implementation.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Record dated primary-source evidence and reproducible read-only checks for supported collection enumeration, authentication, pagination, artwork kinds, game identifiers, attribution, terms and rate limits.
- [ ] #2 Demonstrate how collection entries map to exact library identifiers and identify ambiguous, missing and duplicate game/slot cases without changing library identity.
- [x] #3 Conclude supported, conditionally supported or unavailable with explicit limitations; unsupported scraping is not silently accepted as the implementation path.
- [x] #4 Record evidence in docs/spikes and update the dependent collection task with the feasibility outcome; assess desktop and fullscreen input/setup implications without claiming UI coverage.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Inspect the published OpenAPI and bounded public responses; record the supported access surface and identity-matching limitations before deciding whether collection implementation is possible.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Read-only investigation recorded in docs/spikes/steamgriddb-collections.md. Official API v2.10.0 has no collection enumeration. Public website metadata has counts but no assets or game IDs. Live mapping therefore cannot be demonstrated; synthetic matching cases are explicitly distinguished from provider proof.

AC2 cannot be verified live because collection entries/game identifiers are not exposed by a supported interface. Task remains nonterminal with the blocker explicit; TASK332 is gated on a supported enumeration/export path.
<!-- SECTION:NOTES:END -->
