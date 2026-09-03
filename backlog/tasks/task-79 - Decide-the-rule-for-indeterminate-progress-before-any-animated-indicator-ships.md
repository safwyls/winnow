---
id: TASK-79
title: Decide the rule for indeterminate progress before any animated indicator ships
status: To Do
assignee: []
created_date: '2026-09-03 00:58'
labels: []
dependencies: []
priority: low
ordinal: 106000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
design-system.md section 8's reduced-motion guidance names only the hover saturation ramp, and there is no reduced-motion setting to hang a spinner off. Building the Stores panel hit this: a spinner was deliberately not invented, and sign-in shows a Volt-edged status field saying where to look, plus Cancel.

That interim choice is fine and is what ships. This task is the gate: if an animated indicator is ever wanted, the accessibility floor needs the rule first.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 design-system.md section 8 states what an indeterminate indicator may be and how reduced motion affects it
- [ ] #2 The rule covers the case where there is nothing to animate, so the current status-field pattern is either ratified or replaced
<!-- AC:END -->
