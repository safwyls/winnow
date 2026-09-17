---
id: TASK-332
title: Preview apply and undo library artwork collections
status: To Do
assignee: []
created_date: '2026-09-17 16:12'
updated_date: '2026-09-17 16:47'
labels:
  - blocked
dependencies:
  - TASK-330
  - TASK-331
documentation:
  - doc-1
type: feature
ordinal: 374000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Users want a coordinated artwork style across their library without selecting every image separately. Apply a chosen provider collection to matching library games through a reviewable operation. Implementation is conditional on the SteamGridDB feasibility task establishing an acceptable access method.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Desktop and fullscreen offer Apply artwork collection for a capable provider and accept a collection URL or ID with actionable validation/setup errors.
- [ ] #2 Before applying, show matched games and before/after artwork by slot, unmatched entries and uncovered games; exact identifiers or explicit user-confirmed artwork matches are required.
- [ ] #3 Preserve manual choices by default; users can explicitly include individual customized slots and resolve duplicate candidates before committing.
- [ ] #4 Apply only reviewed successfully downloaded assets, retain current art on failures, and report applied/skipped/failed counts; cancellation leaves an accurate record of completed assignments.
- [ ] #5 Undo restores previous choices only for slots still holding that operation value, preserving later edits; restart and offline behavior retain selected assets and operation history.
- [ ] #6 Initial application is one-time, with no silent collection subscription or styling of later imports; collection assignments take precedence over automatic art and remain below manual choices.
- [ ] #7 Desktop keyboard and fullscreen controller flows, focus return and scaling are verified separately; tests cover ambiguity, partial failure, cancellation and conflict-aware undo; shipped behavior is documented.
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Collection implementation is gated: TASK-330 found no supported collection enumeration API/export. Website-only internal access is undocumented and terms restrict scraping. Await provider-supported access; no bulk-apply UI is shipped. Per-game browser work can proceed independently.

Per-game browsing is implemented independently. Collection application remains unimplemented on both surfaces because supported enumeration is unavailable; the saved-choice model reserves collection precedence and revision-safe restoration, but no bulk operation or collection subscription is exposed.
<!-- SECTION:NOTES:END -->
