---
id: TASK-115
title: Replace the since-you-played bar with something that carries information
status: To Do
assignee: []
created_date: '2026-09-05 02:50'
updated_date: '2026-09-06 00:18'
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

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
DESIGN DECISIONS, 2026-09-05, from the combined details-modal design pass (mock at mock-details.html). Approved by the user:
- Band 4 order becomes: corrections, updates, ABOUT+screenshots, ALSO COVERS, EXTENDS, EXPANSIONS, LISTS. This reverses the recorded reason at GameDetailsView.axaml:899 that ALSO COVERS leads because it is a fact about identity; the superseded sentence goes to docs/decisions.md.
- Ratings become a reception line in Band 1 under year and publisher, NOT a new section. Three figures, each attributed with its count: IGDB users, IGDB aggregated critics, Steam.
- Steam shows its own label ("Very Positive") with the percentage and count on hover.
- Acquisition facts: acquired_at and license_type in the left column under ON DISK. Price paid NEVER appears in this modal — section 7 never be smug; "$59.99 / never opened" is the sentence the product must not write. Price goes to export and account stats.
- Screenshots go inside ABOUT as a thumbnail strip that expands one shot to a hero above it, inline in the modal tree, no popup.
- Refetch is a More menu row with its status on a Band 3 TextBlock outside the scroll region.
- The update list is renamed so it stops colliding with the Band 2 rail; the rail keeps SINCE YOU PLAYED. Mock placeholder is "What landed" and a better name is welcome.
- TASK-115 ships BOTH halves in one pass: the release-to-today axis AND the backfilled monthly bars.
- The rule that governs future additions: a label section heading in Band 4 is earned by a list of rows the user can act on, ABOUT being the single prose exception. A fact about the game goes in Band 1; a fact about this copy goes in the left column; a picture goes inside ABOUT; an act goes in the More menu with its status on the strip.
<!-- SECTION:NOTES:END -->
