---
id: TASK-123
title: The details action band carries seven controls and clips
status: To Do
assignee: []
created_date: '2026-09-05 16:46'
labels:
  - ui
dependencies: []
priority: high
type: bug
ordinal: 150000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The details modal action band (Band 3 of GameDetailsView.axaml) now carries the primary action, Store page, All patch notes, Open folder, Wrong game?, Edit details and Hide — seven controls in a horizontal StackPanel with Spacing=10 that does not wrap.

Estimated by the TASK-119 agent from Button.link chrome (24px), Button.launch chrome (40px) and 12px Jakarta: the installed-game set is roughly 652-690px and the not-installed set roughly 574-605px, against a right column running 422px (card MinWidth 700) to 582px (MaxWidth 860). That overruns at every card width, and it was already near the edge before Edit details was added.

This is an ESTIMATE, not a measurement — the app was not run. Confirm it before designing, since the remedy depends on how much it actually overruns.

The remedy is a design decision and was deliberately not taken: wrapping, a second row, moving a control elsewhere, or folding the rarer actions behind a disclosure. Note the band has already absorbed several additions today (Hide in TASK-87, Wrong game in TASK-102, Edit details in TASK-119) and will attract more, so the answer should accommodate growth rather than buy back one control width.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The actual overrun is measured in a running app before a remedy is chosen
- [ ] #2 Every action in the band is reachable at every card width from MinWidth 700 to MaxWidth 860
- [ ] #3 Nothing clips, and no control is silently unreachable
- [ ] #4 The chosen arrangement accommodates a further control without another redesign
- [ ] #5 Keyboard traversal still reaches every action in a sensible order
- [ ] #6 design-system.md records the arrangement and the rule for what may join the band
<!-- AC:END -->
