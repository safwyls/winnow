---
id: TASK-234
title: Rebase and review PR 12 against merged architecture fixes
status: Done
assignee:
  - '@codex'
created_date: '2026-09-11 15:59'
updated_date: '2026-09-11 16:12'
labels: []
dependencies: []
type: task
ordinal: 266000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Update the GamesDB identity PR onto main after PR 13, preserve its architecture and correctness safeguards, resolve conflicts, and verify the combined behavior.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 PR 12 is based on current main without restoring obsolete startup flow, identity admission rules, documentation history or Backlog ordering.
- [x] #2 GamesDB identity linking preserves transactional user decisions, edition ambiguity rules and the shared refresh pipeline on desktop and fullscreen.
- [x] #3 Independent review findings are addressed and relevant tests plus migration integrity pass.
- [x] #4 The updated branch and clear PR summary are pushed without losing remote work.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Preserve main at conflicting hunks during rebase, discard the obsolete Backlog-only commit, then explicitly integrate hard-ID linking with current admission and refresh contracts. Delegate bounded data and service review work, add integration coverage, update current documentation, run Release validation and push with an explicit lease.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Rebased on PR-13 merge fd807f8, preserving main conflict hunks and dropping the obsolete Backlog ordering commit. Integrated GamesDB as a shared refresh pipeline step; preserved plugin installation/settings changes, source identity rows, manual decisions and main architecture. Independent data/service and preservation reviews completed. Added matching positive release-version gate plus transactional root, release and external-ID rechecks and pending-candidate cleanup. Desktop and fullscreen production-composition tests pass; 469 UI tests pass. Migration check verifies all 38 hashes. Updated TASK-37 to retain the outstanding production edition-evidence acquisition scope.

Release validation: 5,391 tests pass across all six Windows test assemblies; 2 Linux-only cases skip on Windows. The first full run found an outdated identity-reader inventory method name; corrected it and reran all 4,449 general tests successfully. All 469 UI tests, including both new production refresh cases, pass; 38 migration hashes and git diff --check pass. No production host or live library used.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Rebased PR 12 onto merged PR 13 and pushed with an exact remote-head lease. Preserved plugin changes and current architecture, corrected unsafe edition admission and stale-evidence races, integrated shared refresh on both surfaces, and updated the PR summary. Validation: 5,391 Windows Release tests passed, 2 Linux-only skips, 38 migration hashes verified. Broader edition evidence remains tracked in TASK-37.
<!-- SECTION:FINAL_SUMMARY:END -->
