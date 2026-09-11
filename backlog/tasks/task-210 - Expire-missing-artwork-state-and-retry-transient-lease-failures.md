---
id: TASK-210
title: Expire missing artwork state and retry transient lease failures
status: Done
assignee:
  - '@codex'
created_date: '2026-09-11 04:59'
updated_date: '2026-09-11 08:27'
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
- [x] #1 Negative cache lifetime is consistent across disk and memory, respects provider identity changes and expires without requiring app restart.
- [x] #2 A transient null/cancelled load can be retried while a consumer retains its lease, without duplicate concurrent fetches or broken bitmap ownership.
- [x] #3 Tests cover time advancement, warm caches, provider changes and a null-then-success cache; both desktop and fullscreen artwork consumers recover.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Unify negative-cache expiry with a shared clock and exact marker expiration, clear failed lease loads for retry, and verify recovery on desktop and fullscreen consumers.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Memory negatives retain the exact original disk deadline and source identity through a shared clock. Completed null/cancelled/faulted lease loads clear for retry without splitting concurrent requests. Detail covers reset failed width requests. Real desktop and fullscreen cover controls recover after a transient failure.

Independent final review reproduced and fixed an eviction interleaving: a late waiter received disposed art A after another waiter retained replacement B. The lease pool now returns the exact retained instance. CoverLeaseInterleavingTests failed before the correction and passes afterward. All 159 cover tests and 24 headless artwork/selection/dormancy cases pass, including desktop and fullscreen retry paths. Reproduction details are in docs/spikes/architecture-fixes-2026-09-10.md, Final artwork lifetime review.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Verified all 157 cover tests, 149 cover/library/merge model tests and 19 headless artwork cases. New cases cover warm disk/memory expiry, null and cancellation retries under retained leases, provider changes, both visual consumers and same-width detail recovery. No app restart or cache deletion is required.
<!-- SECTION:FINAL_SUMMARY:END -->
