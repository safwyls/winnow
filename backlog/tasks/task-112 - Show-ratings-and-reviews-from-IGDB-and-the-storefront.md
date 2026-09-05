---
id: TASK-112
title: Show ratings and reviews from IGDB and the storefront
status: To Do
assignee: []
created_date: '2026-09-05 02:50'
labels:
  - ui
  - enrichment
dependencies: []
priority: medium
type: feature
ordinal: 139000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The details modal states no critical or player reception. IGDB carries aggregate ratings (its own user rating and an aggregated critic rating with counts); Steam carries review summaries. Surface what is available and be explicit about which source each number comes from — an unattributed score is worse than none.

Check what each source actually permits and provides before designing the display; do not assume a Steam review summary is available through the endpoints Winnow already uses.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Available ratings are shown in the details modal, each attributed to its source
- [ ] #2 The number of ratings behind a score is shown, so a 9 from four people does not read like a 9 from four thousand
- [ ] #3 A game with no rating data shows nothing rather than a zero or an empty scale
- [ ] #4 Fetching is rate-limited, cached and soft-failing
- [ ] #5 What each source provides, and what it does not, is recorded in docs/facet-provenance.md
<!-- AC:END -->
