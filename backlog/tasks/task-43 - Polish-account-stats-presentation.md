---
id: TASK-43
title: Polish account stats presentation
status: To Do
assignee: []
created_date: '2026-08-29 21:55'
labels:
  - ui
  - data
dependencies:
  - TASK-38
priority: low
ordinal: 70000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The account stats screen is functional and its figures are correct, but it is a first pass. Presentation cleanup is shelved until core functionality (M5, M6) is complete. Concrete candidates: layout hierarchy and grouping, per-transaction averages, per-year averages, percentage breakdowns across spend-by-kind slices, cost per hour played, and spend on games never launched. Source: ROADMAP.md section 6 (deferred 2026-08-29).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Visual hierarchy distinguishes primary figures from breakdowns
- [ ] #2 At least two derived figures (from the candidates listed) are implemented
- [ ] #3 The screen remains correct under an empty import (no account-page data)
<!-- AC:END -->
