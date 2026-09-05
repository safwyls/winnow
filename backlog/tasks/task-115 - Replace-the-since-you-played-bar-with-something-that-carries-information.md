---
id: TASK-115
title: Replace the since-you-played bar with something that carries information
status: To Do
assignee: []
created_date: '2026-09-05 02:50'
labels:
  - ui
dependencies: []
priority: medium
type: enhancement
ordinal: 142000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The user on the current bar: "Wondering about how to improve the since you played bar, might be cool to have it show a spike graph for player activity over the life of the game. The current bar feels kinda pointless."

The bar occupies prominent space in the details modal and encodes one scalar — elapsed time — that the adjacent text already states. The suggestion is a spike graph of player activity over the game life, which would say something the rest of the modal does not.

Decide honestly what data exists before designing the shape. Winnow stores the user own longitudinal playtime and sessions; whether population-wide activity over a game life is obtainable is an open question that must be settled with evidence, not assumed — check what the Steam and IGDB endpoints already in use actually offer, and record the finding in docs/spikes/ whichever way it goes. If population data is not available, the users own play history over time is still a graph worth drawing and is data Winnow definitely has.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 What data is actually available for a per-game activity graph is established and recorded in docs/spikes/
- [ ] #2 The bar is replaced by something that states a fact the surrounding text does not already state
- [ ] #3 A game with too little history to draw says so rather than rendering a misleading flat line
- [ ] #4 Whatever is drawn is attributed — the user own sessions and population activity are different claims and must not be confused
- [ ] #5 The replacement respects reduced motion and the bounded-scroll rule from TASK-105
<!-- AC:END -->
