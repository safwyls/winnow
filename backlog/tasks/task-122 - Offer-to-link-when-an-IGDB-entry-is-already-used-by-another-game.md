---
id: TASK-122
title: Offer to link when an IGDB entry is already used by another game
status: To Do
assignee: []
created_date: '2026-09-05 04:58'
labels:
  - ui
  - data
dependencies:
  - TASK-89
priority: medium
type: feature
ordinal: 149000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
User request: "if someone tries to set the metadata for something to a game that is already using that metadata elsewhere in the library then we should prompt them to merge instead".

The refusal already exists and is already distinguished. WorkIgdbPinRepository returns WorkIgdbPinOutcome.IgdbIdClaimedByAnotherWork when another work holds the id, IgdbManualAssignment maps it to IgdbAssignmentStatus.IgdbIdClaimedByAnotherWork, and GameIgdbMatchCopy gives it its own sentence. A comment in IgdbMatchViewModelTests already calls it out as "the one the user can act on" against three they cannot — but the UI gives them nothing to act with, so it dead-ends.

The meaning of the collision is the point: works.igdb_id is UNIQUE, so two works claiming one IGDB entry ARE the same game. That is exactly what the identity-link system models — kind same_game, built across the TASK-70 series and surfaced in the Merges queue. So the honest response to the refusal is not an error but an offer: these two are the same game, link them.

Design the offer rather than bolting a button on. The user should be told which game already holds the entry, be able to see it, and confirm the link — links are reviewed deliberately in the Merges queue and a hard external-id join is the one case AGENTS.md says may auto-merge, so decide and record whether this path confirms in place or routes to the queue.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Assigning an IGDB entry another work already holds offers to link the two as the same game instead of only refusing
- [ ] #2 The offer names and shows the game that already holds the entry, so the user can tell whether it is really the same game
- [ ] #3 Accepting produces the same same_game link the Merges queue produces, through the existing identity-link path and not a second mechanism
- [ ] #4 Declining leaves both works exactly as they were, with nothing pinned
- [ ] #5 Whether this confirms in place or routes to the Merges queue is decided and recorded
- [ ] #6 Tests cover the offer, the accept producing a link, and the decline changing nothing
<!-- AC:END -->
