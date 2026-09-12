---
id: TASK-238
title: Show loaded plugins in Plugins settings
status: Done
assignee:
  - '@codex'
created_date: '2026-09-12 04:43'
updated_date: '2026-09-12 04:45'
labels: []
dependencies: []
type: enhancement
ordinal: 270000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Make plugins loaded in the current session visible at a glance in desktop and fullscreen Plugins settings.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Both surfaces list loaded plugin names and versions and show an empty state when none are loaded.
- [x] #2 The list follows runtime loaded state, including pending enable or disable changes, and existing configuration remains available.
- [x] #3 Focused UI tests pass and plugin documentation describes the list.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Expose a shared loaded-plugin summary from catalog snapshots, bind it above configuration on desktop and fullscreen, verify mixed runtime states and empty states through headless UI tests, and update plugin documentation.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Added a shared runtime-loaded summary with names, versions, empty state and read-failure state. Desktop and fullscreen show it above existing configuration. Nine PluginSettingsInteractionTests passed using scratch build output, including mixed runtime states, pending disable, pending enable, failed startup, and refresh to an empty list on both surfaces. Inspected desktop and fullscreen rendered captures; loaded list is readable and existing controls remain accessible. Documentation updated; git diff --check passed. No production host or real library used.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added loaded plugin names and versions to desktop and fullscreen Plugins settings, with an explicit empty state. Verified nine focused headless interaction tests and rendered captures.
<!-- SECTION:FINAL_SUMMARY:END -->
