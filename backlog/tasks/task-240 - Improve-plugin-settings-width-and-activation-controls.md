---
id: TASK-240
title: Improve plugin settings width and activation controls
status: Done
assignee:
  - '@codex'
created_date: '2026-09-12 04:55'
updated_date: '2026-09-12 04:59'
labels: []
dependencies: []
type: enhancement
ordinal: 272000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Use the settings pane width, place plugin versions below titles, and replace activation actions with visible toggles as requested in the annotated screenshot.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Desktop installation and provider cards use the available pane width and show versions below titles.
- [x] #2 Desktop and fullscreen activation controls show toggle state, preserve restart semantics, and support keyboard or controller interaction.
- [x] #3 Focused UI tests and rendered layout checks pass.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Stretch desktop content, stack title and version, add state-bound activation toggles on both surfaces, verify input and failed-save state, and update the visual specification.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Desktop cards and installation text now span the available pane width; versions sit beneath titles and activation switches occupy the upper right. Fullscreen uses an activation switch and retains its existing version placement below the title. Separate editable switch state restores persisted enablement after a failed save. Added headless bounds and title/version placement assertions, keyboard activation with save-failure rollback, and controller activation coverage. All 23 PluginSettingsInteractionTests and FullscreenSettingsTests passed using scratch output. Inspected desktop shell and fullscreen provider renders; git diff --check passed. No production library or running app was changed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Expanded plugin settings cards, moved versions below titles, and added activation switches on desktop and fullscreen. Verified 23 focused tests and rendered layouts.
<!-- SECTION:FINAL_SUMMARY:END -->
