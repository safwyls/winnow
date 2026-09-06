---
id: TASK-7
title: >-
  Fix update-poll fairness under persistent failure and poll build history
  independently
status: Done
assignee:
  - '@backfill_recovery'
created_date: '2026-08-29 21:52'
updated_date: '2026-09-06 22:18'
labels:
  - enrich
milestone: m-4
dependencies: []
priority: medium
ordinal: 1400
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Under persistent failure, update polling can starve some titles indefinitely. Raw build history must be polled independently of announcement fetches so a failure in one does not block the other. Findings F11 and F12. Source: stabilization-2026-08-28.md Group 2. Trigger: next update-signal work.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A persistently failing title does not prevent other titles from being polled
- [x] #2 Build-history polling proceeds independently of announcement polling
- [x] #3 A test demonstrates fair round-robin under simulated persistent failure
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Persist every completed poll attempt so failing titles yield the capped batch to older candidates; retry failed sources on following days. Poll and record build signals for every due eligible title independently of news outcome, retaining rate limits, caches, and correlation watch rules. Test persistent failures, exceptions, source independence, and restart fairness with canned responses.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented independent news/build operations with per-source exception isolation, persisted retry_pending state, and oldest-attempt-first capped scheduling. Raw build history no longer depends on patch-note age, novelty, availability, or existence. Existing typed-client rate/retry policies and caches are unchanged. Added restart-persistent failure rotation and both source failure directions using canned transport; focused verification awaits serialized shared build slot.

All 59 Updates namespace tests pass in Release (C:\Temp\winnow-task7). New fixtures prove three persistently failing titles rotate through a cap of one over two days with a recreated service provider between turns, for both soft and thrown failures. Both failure directions preserve the other source, and empty/no-feed/ancient news still records raw builds. Existing correlation, retry, cache, and HTTP contract tests remain green. No live HTTP calls. Diff check clean.

Batch integration: Release solution build passed with zero warnings/errors. Full suite passed 3899 tests; two stale identity-inventory assertions were corrected, then all five inventory tests passed (3902 total current tests verified). Changes to the inventory scanner retain enforcement for SQL constants and bulk snapshot callers.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Failed polls now consume their persisted daily turn and retry the following day, ordered behind older attempts. Every due title polls build history independently of announcements; successful signals survive failures in the other source. Verified with 59 canned-response tests, including restart-persistent round-robin failures and independent source failure cases.
<!-- SECTION:FINAL_SUMMARY:END -->
