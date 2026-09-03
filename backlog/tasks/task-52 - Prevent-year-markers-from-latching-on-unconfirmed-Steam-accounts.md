---
id: TASK-52
title: Prevent year markers from latching on unconfirmed Steam accounts
status: To Do
assignee: []
created_date: '2026-08-30 00:17'
labels:
  - enrich
  - data
dependencies: []
priority: medium
ordinal: 58000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
`SteamPlaytimeBackfillService` writes year completion markers for all fetched years before the confirmed gate is evaluated, so an account that was never confirmed (no matching API key) has its 2022-2025 markers written with `imported: 0`. When the confirmed gate rejects the import, the markers survive. If a matching key is later added, only the current year is refetched (it is the only year without a marker), and the account's historical years are permanently unreachable. Observed on the live database's second Steam account. The markers record that the years were asked about, not that they were successfully imported, but the backfill loop treats any marker as done.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Year markers are not written for an account that fails the confirmed gate, or existing markers are cleared when an account first confirms so the historical years are re-eligible
- [ ] #2 A test demonstrates that adding a matching key to a previously unconfirmed account causes all historical years to be refetched and imported
- [ ] #3 Re-running the backfill after the fix on an account that previously latched writes the previously blocked historical data
<!-- AC:END -->
