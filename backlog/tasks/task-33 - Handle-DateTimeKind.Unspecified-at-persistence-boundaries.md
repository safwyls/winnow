---
id: TASK-33
title: Handle DateTimeKind.Unspecified at persistence boundaries
status: Done
assignee:
  - '@timestamp_boundaries'
created_date: '2026-08-29 21:54'
updated_date: '2026-09-06 21:54'
labels:
  - data
milestone: m-4
dependencies: []
priority: medium
ordinal: 1200
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
`DateTimeKind.Unspecified` values can cross persistence boundaries without being rejected or normalized. This can cause silent misinterpretation of timestamps. Finding F45. Source: stabilization-2026-08-28.md Group 3.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Persistence boundaries reject `DateTimeKind.Unspecified` or normalize to UTC
- [x] #2 The chosen strategy (reject vs. standardize on `DateTimeOffset`) is documented
- [ ] #3 A test demonstrates that an Unspecified DateTime does not persist silently
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Audit timestamp binding and retain UTC text storage. 2. Reject Unspecified timestamps in the central Dapper handler while converting Local values to UTC. 3. Add SQLite regressions for required and nullable timestamps. 4. Verify and document the policy.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Removed Dapper default DateTime and nullable DateTime parameter mappings so the registered UTC handler governs writes. The handler rejects Unspecified values with ArgumentException before SQL execution and converts Local to UTC. Six new real SQLite cases cover required/nullable rejection without rows written and UTC/local values with nullable ends. All 27 RepositoryRoundTripTests pass; build has zero warnings/errors. Documentation addition sent to coordinator for the shared data-model section; integrated full suite remains with coordinator.

Timestamp strategy documented in game-library-design.md section 6. Focused repository tests pass; integrated suite follows this batch.

Integration caught subsecond truncation when the previously bypassed handler became active. Updated its format to yyyy-MM-dd HH:mm:ss.FFFFFFF, preserving existing SQLite precision; UTC/local nullable round-trip cases now include fractional ticks. Documentation updated.

Final integration audit removed pre-normalizing fetchedAt.ToUniversalTime calls from all six SQLite caches (Steam Web, GamesDB, IGDB, updates, Steam store, Epic catalog), so Unspecified cannot bypass the central guard. Six parameterized cache regressions pass: rejected insertion leaves zero rows, Local converts to the same UTC instant with fractional precision, and rejected upsert preserves the original payload. Twitch token expiry and resolve-state timestamps use DateTimeOffset and need no change. Focused cache verification completed successfully.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Persistence rejects Unspecified DateTime centrally, including all six SQLite metadata caches, while converting Local to UTC and retaining subsecond precision. Verified with 27 repository round-trip tests, six additional cache boundary regressions, and coordinator integrated suite (3,847 passing before the final six cache cases). No rows are inserted or overwritten for rejected timestamps.
<!-- SECTION:FINAL_SUMMARY:END -->
