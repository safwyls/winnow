---
id: TASK-15
title: Populate account-aware achievement evidence from a supported provider
status: To Do
assignee: []
created_date: '2026-08-29 21:52'
updated_date: '2026-09-11 14:01'
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
- [ ] #1 Ingest achievement schema, user unlocks and global unlock percentages from at least one supported store.
- [ ] #2 Persist user-unlock account provenance; switching accounts cannot reuse another account's completion.
- [ ] #3 Distinguish not fetched or unavailable, confirmed no achievement schema, and a known schema with zero user unlocks.
- [ ] #4 Refresh idempotently and retain valid prior evidence on provider failures without presenting it as fresh.
- [ ] #5 Desktop and fullscreen display ingested per-release data without blending distinct platform percentages.
- [ ] #6 Sanitized fixtures cover private or unavailable data, zero progress, no schema, repeated refresh and account switching.
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: AchievementQueryRepository, GameCoverageViewModel, LibraryViewModel and FullscreenDetailsPage already read and display summaries. IdentityReadModelTests and FullscreenDetailsTests cover those reads. No achievement provider fetch/write implementation was found. Current unlock schema lacks an account key and summary availability needs a richer contract; recommendation value remains a hypothesis.
<!-- SECTION:NOTES:END -->
