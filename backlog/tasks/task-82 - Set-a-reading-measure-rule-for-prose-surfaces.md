---
id: TASK-82
title: Apply the established prose measure to remaining desktop surfaces
status: To Do
assignee: []
created_date: '2026-09-03 00:58'
updated_date: '2026-09-11 14:04'
labels: []
dependencies: []
documentation:
  - design-system.md
priority: low
ordinal: 109000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The visual specification already defines desktop ProseMeasure at 410px and distinguishes prose from card/grid width. Complete the remaining layout application: the .para style and affected platform/settings/account/consent surfaces can still span their enclosing cards. Preserve multi-column geometry and fullscreen's separate TV typography.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 design-system.md states a maximum measure for prose, with the size and leading it applies to
- [ ] #2 Desktop prose follows the established measure and alignment in affected surfaces; assess platform cards, settings, account/consent text and merge explanatory copy without treating the 720px card cap as paragraph width.
- [x] #3 The rule says explicitly that it does not govern the merge card, so section 6's 840px stays undisturbed
- [ ] #4 Verify readable wrapping and accessibility on desktop; assess fullscreen separately without applying the desktop pixel measure to TV layouts.
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: design-system.md section 3 defines ProseMeasure=410; completed specification criteria remain checked. controls.axaml's .para style has no MaxWidth or left alignment, and StoresView contains wider prose hosts. Only the application/layout verification remains open.
<!-- SECTION:NOTES:END -->
