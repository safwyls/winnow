---
id: TASK-2
title: Build JSON and CSV export
status: To Do
assignee: []
created_date: '2026-08-29 21:51'
updated_date: '2026-09-01 01:33'
labels:
  - data
  - infra
milestone: m-1
dependencies: []
priority: low
ordinal: 2500
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Implement the M6 export deliverable: full database export as JSON and CSV that round-trips through the importer without loss. This is the intended first consumer of the acquisition-fact columns (acquired_at, license_type, price_paid_cents) from M5's account-page import. Source: ROADMAP.md section 4, M6 row.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 JSON export produces a complete representation of works, releases, ownerships, sessions, playtime snapshots, and account transactions
- [ ] #2 CSV export covers the same scope
- [ ] #3 Re-importing a JSON export into an empty database produces identical query results
- [ ] #4 Acquisition-fact columns appear in both export formats
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
M6 deferred 2026-08-31, not cancelled.

Two reasons. First, the exit criterion is stale. "Round-trips through the importer without loss" referred to the GDPR export importer, which the M5 redefinition of 2026-08-28 replaced with API backfill and a Steam account-pages parser. No general Winnow-format importer exists. Meeting the criterion as written means building one, roughly doubling the scope, and no prior estimate accounted for it.

Second, the only purpose the documents record for export is that it is the intended first consumer of the acquisition columns (section 6, carried-over debt). That is circular: a feature cannot be justified by the fact that another feature names it as a consumer.

Two genuine cases survive scrutiny. Data ownership is consistent with the local-first, no-server premise: users should be able to extract their data in a portable format. External analysis the app does not perform, such as a spreadsheet pivot, is the other. Machine migration is weaker because copying the database file already does it losslessly.

If resumed, the recommended scope is export only. The exit criterion should be restated honestly: JSON is complete and re-readable by Winnow; CSV is a set of views, human-readable but not round-trippable. Building a general importer is out of scope unless explicitly brought in.
<!-- SECTION:NOTES:END -->
