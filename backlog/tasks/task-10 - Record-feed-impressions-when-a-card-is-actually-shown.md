---
id: TASK-10
title: Record feed impressions when a card is actually shown
status: To Do
assignee: []
created_date: '2026-08-29 21:52'
labels:
  - recommend
  - ui
dependencies: []
priority: medium
ordinal: 10000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Impressions are currently recorded at generation time, not when the card is visible to the user. This overstates impression counts and distorts the feedback signal. Finding F16. Source: stabilization-2026-08-28.md Group 2. Trigger: next feed presentation change.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 An impression is recorded only when the card enters the visible viewport
- [ ] #2 A card generated but never scrolled into view records no impression
<!-- AC:END -->
