---
id: TASK-111
title: Show IGDB screenshots in the details view
status: To Do
assignee: []
created_date: '2026-09-05 02:50'
labels:
  - ui
  - enrichment
dependencies: []
priority: medium
type: feature
ordinal: 138000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The details modal carries no imagery beyond the cover. IGDB supplies screenshots per game; pull them and show them, so the modal says what the game looks like rather than only what its box looks like. Reuse the existing cover cache and disk-cache discipline rather than adding a second image path, and respect the same soft-failing, rate-limited rules the other IGDB calls follow.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 A game with IGDB screenshots shows them in the details modal
- [ ] #2 Images use the existing cover cache and disk cache, not a second image path
- [ ] #3 A game with no screenshots shows nothing rather than an empty frame
- [ ] #4 Fetching is rate-limited, cached and soft-failing like the other IGDB clients
- [ ] #5 The modal does not grow past the window, per the bounded-scroll rule set in TASK-105
<!-- AC:END -->
