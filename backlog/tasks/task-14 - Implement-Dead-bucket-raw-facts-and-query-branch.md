---
id: TASK-14
title: Build evidence-based Derelict lifecycle feed
status: Done
assignee:
  - '@codex'
created_date: '2026-08-29 21:52'
updated_date: '2026-09-09 02:29'
labels:
  - data
  - recommend
dependencies: []
priority: low
ordinal: 67000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Replace the hidden Won't Run placeholder with Derelict. Preserve dated IGDB and Steam lifecycle signals and derive status, confidence and reasons locally. Explicit cancellation, offline and delisting override conservative activity inference; missing data stays unknown. Present a dedicated library bucket and feed shelf while protecting viable sibling editions. PCGamingWiki and Wikidata remain optional follow-up enrichment.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Dated source evidence is persisted and lifecycle status, confidence and explanation are derived on read.
- [x] #2 IGDB and Steam background enrichment collect supported lifecycle signals with caching and soft failure.
- [x] #3 Derelict appears in the library and feed with evidence reasons, separate from ordinary recommendations.
- [x] #4 Tests cover precedence, unknown and stale evidence, single-player protection, storage and feed integration; docs match behavior.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Add raw lifecycle evidence, conservative classification and derived buckets; extend background IGDB and Steam enrichment; wire Derelict feed and reasons; verify tests and migrations and update docs.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented append-only lifecycle observations, conservative configurable classifier, IGDB status/modes and Steam players/reviews/official news enrichment, daily cached polling, Derelict rail/feed/details and viable-copy preference. Classified statuses are recomputed; compact source evidence is retained; source identity corrections and stale data are handled. Both migration checksum manifests updated.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Implemented Derelict lifecycle library/feed using preserved dated IGDB and Steam signals, recomputed confidence/reasons, conservative dead/abandoned inference and viable sibling-copy preference. Build passes with zero warnings. Full regression run passed recommendation (160), UI (87), covers (108), and 3780 main tests; one installed-copy regression was corrected and all 189 affected tests passed on rerun. Two Linux-only smoke tests skip on Windows. Both migration manifests and diff checks pass. PCGamingWiki/Wikidata are deferred; confidence defaults remain uncalibrated and dead needs at least 14 days of observations.
<!-- SECTION:FINAL_SUMMARY:END -->
