---
id: TASK-210
title: Expire missing artwork state and retry transient lease failures
status: To Do
assignee: []
created_date: '2026-09-11 04:59'
updated_date: '2026-09-11 05:06'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.Covers/CoverPipeline.cs:72'
  - 'src/Winnow.Covers/CoverLease.cs:125'
  - 'src/Winnow.Covers/CoverLease.cs:142'
documentation:
  - docs/architecture-review-2026-09-10.md
priority: medium
type: bug
ordinal: 241000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R22. Evidence: Reproduced. CoverPipeline remembers missing keys by source-set identity without an expiry. After the disk negative marker expires, the same running pipeline still reports the key missing. CoverLeasePool also retains a completed null load while a lease remains held: two requests after a transient null call the underlying cache only once. Artwork can remain a placeholder for the process or lease lifetime after the configured negative TTL or a recoverable failure. Disk, pipeline and lease caches disagree about retry eligibility.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Negative cache lifetime is consistent across disk and memory, respects provider identity changes and expires without requiring app restart.
- [ ] #2 A transient null/cancelled load can be retried while a consumer retains its lease, without duplicate concurrent fetches or broken bitmap ownership.
- [ ] #3 Tests cover time advancement, warm caches, provider changes and a null-then-success cache; both desktop and fullscreen artwork consumers recover.
<!-- AC:END -->
