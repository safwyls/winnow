---
id: TASK-235
title: Handle refreshed facets during legacy merge replay
status: Done
assignee:
  - codex
created_date: '2026-09-11 16:15'
updated_date: '2026-09-11 16:20'
labels: []
dependencies: []
ordinal: 267000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Recover legacy upgrades after facet refresh without weakening identity guards
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Refreshed facet rows permit safe upgrade
- [x] #2 Identity and durable user row conflicts refuse atomically
- [x] #3 Regression tests and migration integrity pass for shared startup
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Inspect a copied database and journal semantics, narrowly handle facet replay, test upgrades and document behavior

Handle the same proven facet-refresh case for release facets; restore missing assignment with unknown rank because repoint journals did not record ranking.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Read-only SQLite backup of the affected library reproduced application 40 failure with the production initializer. Snapshot has 40 standing merges, passes quick_check and has zero foreign-key violations. Original library was not modified. Missing facet 193 still exists in vocabulary; only its survivor assignment was removed.

After work-facet recovery, the copied library reaches application 12 and encounters missing release_facets repoints. The journal identifies both releases and the facet but contains no rank; recovery must retain rank as NULL instead of inventing an order.

Production DatabaseInitializer successfully upgraded the copied library through all 38 migrations and initialized a second time. SQLite integrity_check is ok, foreign_key_check returns zero rows; 40 live identity links and all 28 missing facet assignments restored. No existing work, release, ownership, session or globally keyed external ID lost. Counts: works 1025->1065, releases 1031->1065, ownerships 1047->1062, external IDs unchanged at 1065, sessions unchanged at 11. Original database remains untouched.

All 4,462 Winnow.Tests Release tests pass, including 18 retirement cases. Independent review found no actionable safety issues. All 38 migration hashes and whitespace checks pass. Shared production initialization was exercised on two private snapshot copies; desktop/fullscreen both use this startup path, with no presentation changes or production host launch.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Fixed legacy upgrades after metadata refresh removed journalled facet assignments. Exact journal evidence restores work/release memberships, unknown release rank stays NULL, and durable-row refusal/rollback remains intact. Copied affected library upgrades twice successfully with 40 links and 28 restored assignments, integrity and foreign keys clean. 4,462 Release tests and 38 migration hashes pass. Original library untouched.
<!-- SECTION:FINAL_SUMMARY:END -->
