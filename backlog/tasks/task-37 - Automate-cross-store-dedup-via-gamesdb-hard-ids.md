---
id: TASK-37
title: Automate cross-store dedup via gamesdb hard ids
status: To Do
assignee: []
created_date: '2026-08-29 21:54'
labels:
  - resolve
  - enrich
dependencies:
  - TASK-5
priority: medium
ordinal: 37000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
`Winnow.Enrich.GamesDb` routes Epic titles to a Steam appid for enrichment (62 of 67 resolved), but deliberately writes no `external_ids` and no merge candidates because `external_ids` is keyed `(provider, provider_id)` globally and would collide with the Steam release that already owns the id. A different keying or a dedicated cross-reference would collapse most of the merge queue automatically via hard ids rather than fuzzy title, which is what the design doc section 5.3 wants. Source: ROADMAP.md section 6.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Cross-store identity links from gamesdb produce confirmed merge candidates without colliding with existing external_ids
- [ ] #2 The merge queue shrinks by the number of titles that resolve via hard id
<!-- AC:END -->
