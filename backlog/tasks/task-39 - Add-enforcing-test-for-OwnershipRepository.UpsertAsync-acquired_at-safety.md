---
id: TASK-39
title: Add enforcing test for OwnershipRepository.UpsertAsync acquired_at safety
status: Done
assignee:
  - '@codex'
created_date: '2026-08-29 21:54'
updated_date: '2026-09-06 22:40'
labels:
  - data
milestone: m-4
dependencies: []
priority: medium
ordinal: 2200
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
`OwnershipRepository.UpsertAsync` could overwrite an imported `acquired_at` if a Steam candidate source ever starts supplying `AcquiredAt`. Today both sources hard-code null, so the safety is incidental. An enforcing test should lock this invariant. Source: ROADMAP.md section 6.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A test proves that upserting a candidate with a non-null `AcquiredAt` does not overwrite an existing imported `acquired_at`
- [x] #2 The test fails if the guard is removed
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Inspect OwnershipRepository's conflict update and existing round-trip coverage. 2. Add an enforcing SQLite test where a later non-null candidate acquisition date cannot replace an imported date. 3. Record evidence and finalize after the focused test passes.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Added an enforcing round-trip test that seeds an imported acquisition date, upserts a later candidate with a different non-null date, and asserts the imported value remains. Verification awaits the parent's serialized test slot.

Focused verification exposed the original guard was reversed: COALESCE(excluded.acquired_at, ownerships.acquired_at) preferred the incoming candidate. Corrected it to COALESCE(ownerships.acquired_at, excluded.acquired_at); this preserves imported dates and still fills null dates. Parent rerun pending.

Source SQL correction is complete; no migration is needed because this changes upsert behavior only. The new test is intentionally a mutation guard: reversing COALESCE operand order causes the assertion to fail.

Fixture now models the actual importer: FillAcquisitionFactsAsync seeds the imported date on the existing ownership, then UpsertAsync receives a conflicting non-null candidate date. Stored-first COALESCE is therefore exercised against the intended source boundary.

Mutation evidence from first focused run: before the SQL correction, the new test failed with expected 2016-10-28 and actual 2026-09-06, proving the original excluded-first guard overwrote imported data. After changing to ownerships-first, rerun the normal filter and record pass counts.

The enforcing importer-shaped test exposed the original excluded-first COALESCE bug: expected imported2016-10-28, received incoming2026-09-06. Corrected Upsert to stored-first COALESCE. RepositoryRoundTrip tests now pass in Release; the observed pre-fix failure supplies the guard-removal negative control.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Preserve imported acquisition dates during candidate upsert; only fill absent dates. The regression failed against the original SQL and passes with the guard.
<!-- SECTION:FINAL_SUMMARY:END -->
