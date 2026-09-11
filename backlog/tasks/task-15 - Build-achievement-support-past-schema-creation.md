---
id: TASK-15
title: Populate account-aware achievement evidence from a supported provider
status: Done
assignee:
  - '@recommendation-engine'
created_date: '2026-08-29 21:52'
updated_date: '2026-09-11 19:11'
labels:
  - data
  - ingest
  - ui
dependencies: []
documentation:
  - game-library-design.md
  - docs/recommendation-engine.md
priority: medium
ordinal: 68000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Populate the existing per-release achievement query and detail-display pipeline from a supported store provider. Reads and desktop/fullscreen summaries already exist, but no production fetch/write path supplies them. Add account provenance, freshness and availability semantics so summaries and TASK-137 can consume trustworthy evidence.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Ingest achievement schema, user unlocks and global unlock percentages from at least one supported store.
- [x] #2 Persist user-unlock account provenance; switching accounts cannot reuse another account's completion.
- [x] #3 Distinguish not fetched or unavailable, confirmed no achievement schema, and a known schema with zero user unlocks.
- [x] #4 Refresh idempotently and retain valid prior evidence on provider failures without presenting it as fresh.
- [x] #5 Desktop and fullscreen display ingested per-release data without blending distinct platform percentages.
- [x] #6 Sanitized fixtures cover private or unavailable data, zero progress, no schema, repeated refresh and account switching.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add Core achievement availability/account snapshot contracts and append migration 0040 for scoped unlocks and attempt/success timestamps. 2. Implement typed Steam schema/player/global client using existing bounded redacted transport and API keys, with conservative parser fixtures. 3. Persist successful facts idempotently and preserve stale prior evidence on failure; read current selected-account summaries per release. 4. Wire bounded periodic background sync and coordinate desktop/fullscreen unknown/no-schema/stale presentation. 5. Verify HTTP/account-switch/failure/refresh boundaries and both surfaces, document supported provider and commit separately.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: AchievementQueryRepository, GameCoverageViewModel, LibraryViewModel and FullscreenDetailsPage already read and display summaries. IdentityReadModelTests and FullscreenDetailsTests cover those reads. No achievement provider fetch/write implementation was found. Current unlock schema lacks an account key and summary availability needs a richer contract; recommendation value remains a hypothesis.

Implemented official Steam schema/player/global achievement client using API-key-only requests through existing bounded retry/rate-limited transport, with request logging disabled. Production sync requires matching confirmed account/key fingerprint, positive ownership membership and exact Steam IDs; startup/manual pipeline plus15-minute timer process20 due games, with24-hour attempt/freshness window. Migration0040 retains legacy unknown-account rows and adds account-keyed unlocks plus separate attempt/schema/progress/global timestamps. Invalid/private/incomplete/wrong-account replies remain unanswered; known zero requires a complete schema-aligned response. Repeated writes are idempotent, failed refreshes preserve previous valid data without restamping timestamps, and schema changes cannot combine new totals with stale unlocks. Desktop and fullscreen show explicit availability and last-known progress, including standalone desktop copy rows. Validation:38/38 provider/account/identity/inventory tests;34/34 achievement presentation/fullscreen detail/tab tests;41 migration hashes verified. Fixtures contain fake accounts; no live provider call, user database migration or private capture was performed. Documentation updated in build spec6.2, visual spec10 and recommendation model; achievements add no scoring or retirement change.

Final review added per-release schema/global observation fences: an older response for another account cannot replace newer shared metadata or percentages. Matching-schema progress remains independently writable for that account; incompatible old schemas remain unanswered. New regression passes; final backend/inventory count is39/39 (superseding38 above), with34/34 UI tests unchanged.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added account-aware Steam achievement ingestion and per-release availability/freshness displays on desktop and fullscreen. Verified39 backend/inventory tests,34 UI tests and migration integrity. Requires a confirmed Steam API key; session-only sign-in and other stores remain unsupported. No live service coverage or recommendation improvement claimed.
<!-- SECTION:FINAL_SUMMARY:END -->
