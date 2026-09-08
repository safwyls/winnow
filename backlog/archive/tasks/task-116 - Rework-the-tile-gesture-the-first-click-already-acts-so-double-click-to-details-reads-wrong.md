---
id: TASK-116
title: >-
  Rework the tile gesture: the first click already acts, so double-click to
  details reads wrong
status: To Do
assignee: []
created_date: '2026-09-05 02:50'
labels:
  - ui
dependencies: []
priority: medium
type: enhancement
ordinal: 143000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The user: "Library cover art cards, corner fold to flip otherwise open details or corner fold to open details directly. Something feels odd about the double click to details when the first click already does an action."

The complaint is real and structural: the first click flips the card, so a double-click is not "the same gesture, harder" but two different actions in sequence, and the second is unrelated to the first. Two shapes were suggested — corner fold flips and the body opens details, or corner fold opens details directly.

Pick one and make the gesture consistent across the grid and the list view. The card back exists and carries content (TASK-98 put dimmed key art behind it), so whichever shape is chosen must keep the back reachable.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Opening details is a single deliberate gesture, not a second click that happens to follow a different action
- [ ] #2 The card back stays reachable
- [ ] #3 The gesture is discoverable — the affordance is visible rather than learned by accident
- [ ] #4 Keyboard and screen-reader users have an equivalent path to both the back and the details
- [ ] #5 The grid and the list view agree
- [ ] #6 design-system.md is updated, and the superseded interaction is recorded in docs/decisions.md
<!-- AC:END -->
