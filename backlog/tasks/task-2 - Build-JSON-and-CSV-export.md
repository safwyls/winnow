---
id: TASK-2
title: Build JSON and CSV export
status: To Do
assignee: []
created_date: '2026-08-29 21:51'
labels:
  - data
  - infra
milestone: m-1
dependencies: []
priority: high
ordinal: 2000
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
