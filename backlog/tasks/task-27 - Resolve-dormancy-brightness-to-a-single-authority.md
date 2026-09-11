---
id: TASK-27
title: Resolve dormancy brightness to a single authority
status: To Do
assignee: []
created_date: '2026-08-29 21:53'
updated_date: '2026-09-11 05:07'
labels:
  - ui
dependencies: []
references:
  - docs/architecture-review-2026-09-10.md
priority: low
ordinal: 78000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Two different dormancy brightness values exist (0.60 vs 0.68) and the conflict is unresolved. Finding F50. Source: stabilization-2026-08-28.md Group 2. Trigger: next dormancy or token change.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 A single brightness value is defined in one place
- [ ] #2 All dormancy rendering references that single value
- [ ] #3 The chosen value is documented with its rationale
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Architecture review 2026-09-10: retained as the implementation owner for a single dormancy brightness authority across desktop/fullscreen rendering. The obsolete mock still advertises 0.60; the current ramp uses 0.68 in multiple places. TASK-223 owns broader active-document contradictions and must coordinate with this task rather than create a second brightness implementation.
<!-- SECTION:NOTES:END -->
