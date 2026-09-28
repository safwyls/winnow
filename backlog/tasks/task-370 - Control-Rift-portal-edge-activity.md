---
id: TASK-370
title: Control Rift portal edge activity
status: Done
assignee:
  - '@codex'
created_date: '2026-09-28 20:56'
updated_date: '2026-09-28 21:00'
labels: []
dependencies: []
type: spike
ordinal: 406000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The user wants to tune portal motion independently of the existing roundness and waviness controls in the Rift mock.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A labeled activity slider controls portal motion from still to energetic and resets to the current gentle default.
- [x] #2 Desktop and fullscreen honor the setting while reduced motion, keyboard selection and hidden views retain their safeguards.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Add activity to the reusable renderer and appearance controls, then verify zero, maximum, reset and motion overrides on both surfaces.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Added Portal activity from 0 to 100 percent, independent of shape, with the original rate at 40 percent and roughly four times that rate at maximum. The reusable renderer stops its ambient loop at zero and can resume without a phase jump. An animated SVG shape preview runs only while the settings dialog is visible and focused; Still portal and reduced motion disable the control. Browser checks covered desktop Discover and fullscreen Library, minimum/maximum, reset, still-mode disabling and an empty warning/error log. Controlled-clock Node checks passed for zero/default/maximum rates, pause/resume, opening completion at zero and keyboard/fallback loop guards. JavaScript syntax and git whitespace checks passed. Saved capture 07 and updated the study README. Only the Rift mock changed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added a Portal activity slider and animated preview to Rift appearance settings. Verified both surfaces, still/reset behavior and renderer clock/loop safeguards; production frontends remain unchanged.
<!-- SECTION:FINAL_SUMMARY:END -->
