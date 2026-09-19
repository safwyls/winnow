---
id: TASK-296
title: Harden session watcher failures and expose quiet diagnostics
status: Done
assignee:
  - '@codex'
created_date: '2026-09-15 23:50'
updated_date: '2026-09-15 23:59'
labels: []
dependencies: []
type: bug
ordinal: 338000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Executable-index rebuilds fail with InvalidCastException in ReleaseRepository, leaving Steam play facts updated without recorded sessions. Users need quiet failure visibility and useful local evidence for bug reports.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Release summaries load real SQLite value types without breaking executable indexing; regression tests cover the failure.
- [x] #2 Watcher failures remain retryable, preserve usable state, and expose health that recovers only when the failed operation succeeds.
- [x] #3 Desktop and fullscreen show a quiet persistent failure notice with access to local diagnostic logs.
- [x] #4 Diagnostics provide safe actionable failure context and documented bug-report steps; relevant tests pass.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Fix SQLite release-summary materialization with a real database regression. 2. Add shared watcher health with per-operation failure/recovery, bounded retry logging and tests. 3. Surface quiet health and diagnostic-folder access on desktop and fullscreen. 4. Verify integration, document bug-report evidence and commit scoped changes.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Reproduced the exact mixed-row SQLite failure: first edition-year join NULL, later Int64 year caused Dapper to unbox as nullable Int32. Projection now retains declared INTEGER metadata; no migration. Index now reads only external IDs. Added independent watcher health, one-minute index retry, bounded failure/recovery logs, discovery failure propagation and cancellation safeguards. Desktop and fullscreen notices preserve focus, clear on true recovery, and expose selected-data-directory logs; rendered screenshots inspected at 1200x640 and 1920x1080. Added per-event build/run context and bug report template.

Validation: clean serial full-solution build succeeded with 0 warnings/errors. Final focused watcher, data, logging and documentation run passed 100 tests. Full UI suite passed 712 tests; rendered desktop/fullscreen notices inspected and controller log action exercised. Full core suite passed 4733 tests with one README wording lint failure; corrected wording and all 5 documentation tests then passed. Other suites: Covers 189, Recommendations 192, Updates 37, SteamGridDB 42, Plugins 87 pass. Plugins rerun in isolated output after shared scratch-output lock during first parallel solution test. Linux smoke tests (2) skipped on Windows; require Linux CI. Verified all 42 migration hashes and diff whitespace. No production database changes or running app replacement.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Fixed the reproduced nullable edition-year SQLite mapping crash and removed unrelated metadata from executable indexing. Added independent recoverable watcher health, quiet desktop/fullscreen log notices, persistent Diagnostics actions, bounded failure logs with build/run context and bug-report guidance. Verified clean build, 100 focused tests, 712 UI tests and remaining suites as recorded; Linux smoke coverage remains CI-only on this Windows host.
<!-- SECTION:FINAL_SUMMARY:END -->
