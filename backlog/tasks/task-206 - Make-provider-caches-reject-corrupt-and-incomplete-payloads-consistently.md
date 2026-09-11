---
id: TASK-206
title: Make provider caches reject corrupt and incomplete payloads consistently
status: Done
assignee:
  - '@enrichment-api'
created_date: '2026-09-11 04:59'
updated_date: '2026-09-11 07:59'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.Enrich.GamesDb/GamesDbClient.cs:72'
  - 'src/Winnow.Enrich.GamesDb/GamesDbClient.cs:164'
  - 'src/Winnow.Enrich.Steam/SteamStoreClient.cs:168'
  - 'src/Winnow.Enrich.Igdb/IgdbClient.cs:278'
documentation:
  - docs/architecture-review-2026-09-10.md
priority: medium
type: bug
ordinal: 237000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R18. Evidence: Source verified. GamesDbClient deserializes warm cache data without handling JsonException, allowing a corrupt entry to poison a long TTL. SteamStoreClient checks batch completeness by returned count rather than requested IDs, so unrelated response entries can hide an omitted requested ID and turn it into a confirmed miss. IGDB retains compatible older-version payloads for failed-refetch fallback only while their age remains within the TTL; expired entries are excluded. Cache corruption or imperfect provider responses can become unsupported cached absence or repeated failure. Empty, unavailable, malformed, stale and authoritative missing results need explicit contracts.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Corrupt or incompatible cache payloads are evicted/ignored safely and cannot poison subsequent valid fetches for the cache TTL.
- [x] #2 Batch response completeness is evaluated against requested identifiers; missing or unrelated results cannot create unsupported cached misses.
- [x] #3 Cold/warm/offline tests distinguish empty, malformed, partial and stale results for affected clients, and the documented fallback contract matches the implementation.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Validate GamesDb warm cache projections and treat malformed/incompatible values as refetchable unknowns without replacing them with cached absence. 2. Correlate Steam batch answers by each requested ID; cache negative results only for the observed explicit non-store result, and leave malformed, omitted or conflicting records retryable. 3. Retain compatible stale IGDB game/mapping positives for failed-refetch or missing-credential fallback independently of TTL, while never renewing their timestamp or trusting stale misses. 4. Add warm corrupt-cache repair, partial/unrelated batch, malformed row and expired-compatible offline/refetch fixtures; align narrow provider cache documentation and run focused client tests.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented validated versioned GamesDb warm caches, exact requested-ID Steam batch authority with explicit result-15 negative proof and one-time recheck of legacy null misses, and compatible expired IGDB positive fallback without timestamp renewal. Malformed, duplicate, unrelated, missing and future-version entries remain unknown. Final Windows Release provider cache gate passed 460/460 tests, including cold/warm/offline repair, stale-positive preservation, expired-negative refresh, future/miskeyed rejection and lifecycle/cache timestamp boundaries. Both UI surfaces consume these shared application clients; no surface-specific interaction changed. Build spec and decisions now state the verified contracts.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Provider caches now retain only evidence they can substantiate: corrupt entries refetch, Steam negatives require matching explicit absence, and compatible stale IGDB positives survive offline without renewing freshness. Verified 460/460 focused Windows Release tests with canned responses; no live API traffic.
<!-- SECTION:FINAL_SUMMARY:END -->
