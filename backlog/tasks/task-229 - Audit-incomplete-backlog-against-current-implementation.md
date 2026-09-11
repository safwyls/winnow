---
id: TASK-229
title: Audit incomplete backlog against current implementation
status: Done
assignee:
  - codex
created_date: '2026-09-11 13:53'
updated_date: '2026-09-11 14:09'
labels: []
dependencies: []
type: docs
ordinal: 261000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Ensure every incomplete task describes useful remaining work with accurate scope, acceptance criteria and dependencies; archive invalidated or redundant items.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Every initially incomplete task has a source-backed disposition
- [x] #2 Retained tasks describe current remaining work and archives explain why work is unnecessary
- [x] #3 Task dependencies and roadmap references remain consistent after the audit
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Inventory incomplete tasks; inspect source and related completed work by domain; update or archive through the CLI; verify full coverage and relationships; commit the audit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Coverage: reviewed all 25 initially incomplete tasks plus the sole draft DRAFT-1 against current source, tests, specifications and related completed work. Updated and retained TASK-2,4,13,15,37,40,41,42,43,46,49,80,81,82,108,109,110,114,136,137,138,159. Each task records its evidence and current remaining scope. Completed TASK-44 through its documented-and-tested accepted transaction-deduplication alternative; completed TASK-170 after verifying successful Pages PR/main jobs and HTTP 200 responses for all three public routes. Archived TASK-65 as a duplicate of the migration-baseline rewind delivered with TASK-70.7. DRAFT-1 remains valid deferred collection-import work and was left unchanged. Preserved priorities, ordinals, existing completed criteria and the TASK-4 hardware limitation. Removed obsolete dependencies, connected research to existing replay tooling and corrected roadmap/rail claims exposed by the audit. Independent review checked scope preservation and prompted removal of unnecessary new follow-up approval language. Verification: 40 account/provenance/backup tests and 100 repository enforcement tests passed; CLI readback matched all planned task criteria and statuses; every remaining task has resolvable dependencies/references and open criteria; archive record retained; git diff --check passes. No feature implementation, new provider probe, support request or hardware validation occurred. Existing UI edits and unrelated untracked files remain separate.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Reconciled 25 incomplete tasks: 22 updated, two completed with verification and one redundant request archived. Reviewed the sole draft and retained it. Current roadmap and navigation documentation agree with task scope. Verified 140 focused tests, Pages CI/live routes, task readback, dependencies, references and diff hygiene.
<!-- SECTION:FINAL_SUMMARY:END -->
