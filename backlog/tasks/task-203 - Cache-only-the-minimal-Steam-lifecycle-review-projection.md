---
id: TASK-203
title: Cache only the minimal Steam lifecycle review projection
status: To Do
assignee: []
created_date: '2026-09-11 04:59'
updated_date: '2026-09-11 05:06'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.Enrich.Steam/SteamLifecycleClient.cs:78'
  - 'tests/Winnow.Tests/SteamStore/SteamLifecycleClientTests.cs:91'
documentation:
  - docs/architecture-review-2026-09-10.md
priority: medium
type: bug
ordinal: 234000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R15. Evidence: Source verified. SteamLifecycleClient stores the original review response in its cache before stripping authors and review prose from EvidenceJson. The test described as verifying minimal persistence checks only returned RawJson rather than the backing cache. Unneeded third-party review text and account data remain in the local database despite the deliberately minimal evidence contract.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Persisted lifecycle cache entries contain only a versioned minimal projection required by the feature, excluding author identifiers and review prose.
- [ ] #2 Previously stored raw cache entries are expired or migrated without breaking offline behavior.
- [ ] #3 Cold/warm-cache integration tests inspect actual SQLite cache payloads as well as returned evidence.
<!-- AC:END -->
