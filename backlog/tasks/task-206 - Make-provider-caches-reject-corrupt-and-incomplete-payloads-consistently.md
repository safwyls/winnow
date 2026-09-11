---
id: TASK-206
title: Make provider caches reject corrupt and incomplete payloads consistently
status: To Do
assignee: []
created_date: '2026-09-11 04:59'
updated_date: '2026-09-11 05:06'
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
- [ ] #1 Corrupt or incompatible cache payloads are evicted/ignored safely and cannot poison subsequent valid fetches for the cache TTL.
- [ ] #2 Batch response completeness is evaluated against requested identifiers; missing or unrelated results cannot create unsupported cached misses.
- [ ] #3 Cold/warm/offline tests distinguish empty, malformed, partial and stale results for affected clients, and the documented fallback contract matches the implementation.
<!-- AC:END -->
