---
id: TASK-38
title: Surface acquisition facts in the UI or export
status: To Do
assignee: []
created_date: '2026-08-29 21:54'
labels:
  - data
  - ui
milestone: m-1
dependencies:
  - TASK-2
priority: medium
ordinal: 38000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The ownership columns acquired_at, license_type, and price_paid_cents are stored (migration 0014) and populated by M5's account-page importer, but no UI or export reads them. M6 export is the intended first consumer. These columns currently have no visible effect. Source: ROADMAP.md section 6.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 At least one consumer (export or UI) reads and displays acquired_at, license_type, and price_paid_cents
- [ ] #2 The export format includes these columns when populated
<!-- AC:END -->
