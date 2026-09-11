---
id: TASK-27
title: Resolve dormancy brightness to a single authority
status: Done
assignee:
  - '@codex'
created_date: '2026-08-29 21:53'
updated_date: '2026-09-11 08:10'
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
- [x] #1 A single brightness value is defined in one place
- [x] #2 All dormancy rendering references that single value
- [x] #3 The chosen value is documented with its rationale
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Define the shared artwork transform endpoint in Covers and have Avalonia tokens and procedural art reference it. Preserve the current 0.68 floor justified by real cover art, remove misleading 0.60 comments, and retire the obsolete mock under TASK223. Verify resource values and real/procedural rendering parity without changing the chosen appearance.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Architecture review 2026-09-10: retained as the implementation owner for a single dormancy brightness authority across desktop/fullscreen rendering. The obsolete mock still advertises 0.60; the current ramp uses 0.68 in multiple places. TASK-223 owns broader active-document contradictions and must coordinate with this task rather than create a second brightness implementation.

Implemented shared Covers.DormancyStyle endpoint and Avalonia resource adapter; disk transform, application ramp and placeholders reference it. Retained0.68 brightness and documented real-capsule rationale.16 rendering checks plus1 actual resource-dictionary check pass; existing desktop/fullscreen dormancy controls remain covered. TASK223 retires the historical mock.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Dormancy brightness and the coupled saturation/hue endpoint now have one authority shared by cached pixels, procedural art and both presentations. Existing appearance is preserved.
<!-- SECTION:FINAL_SUMMARY:END -->
