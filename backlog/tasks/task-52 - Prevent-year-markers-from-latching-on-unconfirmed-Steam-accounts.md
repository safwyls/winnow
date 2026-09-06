---
id: TASK-52
title: Prevent year markers from latching on unconfirmed Steam accounts
status: Done
assignee:
  - '@backfill_recovery'
created_date: '2026-08-30 00:17'
updated_date: '2026-09-06 21:39'
labels:
  - enrich
  - data
milestone: m-4
dependencies: []
priority: high
ordinal: 1000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
`SteamPlaytimeBackfillService` writes year completion markers for all fetched years before the confirmed gate is evaluated, so an account that was never confirmed (no matching API key) has its 2022-2025 markers written with `imported: 0`. When the confirmed gate rejects the import, the markers survive. If a matching key is later added, only the current year is refetched (it is the only year without a marker), and the account's historical years are permanently unreachable. Observed on the live database's second Steam account. The markers record that the years were asked about, not that they were successfully imported, but the backfill loop treats any marker as done.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Year markers are not written for an account that fails the confirmed gate, or existing markers are cleared when an account first confirms so the historical years are re-eligible
- [x] #2 A test demonstrates that adding a matching key to a previously unconfirmed account causes all historical years to be refetched and imported
- [x] #3 Re-running the backfill after the fix on an account that previously latched writes the previously blocked historical data
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Ignore historical completion markers until an account has confirmed; stop writing completion on rejected imports; test later matching credentials and legacy latched markers on temporary SQLite databases.

Invalidate legacy unconfirmed markers to an empty pending value before confirmation persists, so an anchor failure or interruption cannot re-latch them.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Verified 29 Release backfill/account-identity tests using C:\Temp\winnow-task52 output. Regression seeds legacy zero-import markers into a temporary migrated SQLite database, rejects an undisclosed pass, simulates matching credential responses with an anchor failure, then retries and verifies previously blocked 2024/2025 snapshots are written. New unconfirmed passes leave no completion markers. Real user database and live Steam were not accessed; criterion 3 is evidenced by the reproduced persisted state and canned responses, not a live account rerun.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Unconfirmed responses no longer complete years. Legacy markers become pending before confirmation, so matching credentials recover history even after an anchor failure. Confirmed imports retain historical skipping and idempotence. All 29 focused tests pass.
<!-- SECTION:FINAL_SUMMARY:END -->
