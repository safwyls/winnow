---
id: TASK-164
title: Accelerate and stabilize Windows CI tests
status: Done
assignee:
  - '@codex'
created_date: '2026-09-08 19:45'
updated_date: '2026-09-08 20:04'
labels: []
dependencies: []
modified_files:
  - .github/workflows/ci.yml
  - AGENTS.md
  - scripts/Summarize-TestResults.ps1
  - scripts/Test-TestResultSummary.ps1
  - tests/Winnow.Tests/TempDatabase.cs
  - tests/Winnow.Recommend.Tests/TempDatabase.cs
  - tests/Winnow.Tests/MigrationTests.cs
  - tests/Winnow.Tests/ExplicitContentTests.cs
  - tests/Winnow.Tests/IdentityReadModelTests.cs
  - tests/Winnow.Ui.Tests/StoreLinkAfterInstallTests.cs
priority: high
type: task
ordinal: 196000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Reduce the Windows CI test duration and false failures without weakening regression coverage. Preserve production-faithful migration tests while eliminating accidental repeated database migration work from ordinary tests.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Ordinary database-backed tests clone a verified current-schema template instead of replaying every migration per test case
- [x] #2 Migration and backup behavior tests continue to exercise genuinely fresh or historical databases
- [x] #3 Pure rule tests do not create databases they never use
- [x] #4 Known heavy fixture writes are batched without changing asserted behavior
- [x] #5 The 4K store-link UI scenario settles deterministically and remains covered
- [x] #6 The same branch commit does not run duplicate push and pull-request CI workflows
- [x] #7 CI publishes a readable per-assembly and slow-test timing summary from TRX results
- [x] #8 Release build and the complete test suite pass
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add a process-scoped, checkpointed current-schema SQLite template and clone it for ordinary TempDatabase instances while keeping explicit fresh-database paths. 2. Remove accidental database construction from pure rule cases and batch remaining known high-volume fixture writes. 3. Make the 4K store-link interaction wait for deterministic layout/input settlement. 4. De-duplicate branch CI triggers and add a TRX-to-job-summary timing script with tests. 5. Run focused tests, the full Release suite, migration verification, and compare local timing against the recorded baseline.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Validation: Release build passed with 0 warnings/errors. Full CI-equivalent suite passed 4,091 tests with 2 expected platform skips in 47.73 seconds, down from the recorded 83.6-second local baseline (43%). A four-CPU run passed in 50.42 seconds, down from 71 seconds (29%). Focused database/migration/backup coverage passed 107 tests; the formerly flaky 4K theory passed five repeated runs (20 cases). TRX summary self-test and both migration integrity scripts passed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Replaced per-case migration replay with isolated clones of a once-per-process checkpointed schema, preserved explicit fresh-migration coverage, removed unused database setup from pure rules, and batched a read-performance fixture. Stabilized the 4K input/layout test without weakening its assertions. CI now avoids duplicate feature-branch push runs and publishes tested TRX timing tables. The full Release gate and constrained-runner benchmark pass with materially lower wall time; no tests were removed.
<!-- SECTION:FINAL_SUMMARY:END -->
