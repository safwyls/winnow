---
id: TASK-239
title: Load plugin settings when the lazy pane receives its model
status: Done
assignee:
  - '@codex'
created_date: '2026-09-12 04:49'
updated_date: '2026-09-12 04:51'
labels: []
dependencies: []
type: bug
ordinal: 280000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Plugins settings remains at Reading loaded plugins with no package cards when first opened. Ensure initial lazy-pane binding loads discovered plugins and reopening refreshes runtime state.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Opening the desktop Plugins tab with an uninitialized model loads package cards and replaces the reading placeholder.
- [x] #2 Late model binding and reopening refresh correctly; fullscreen cold loading and existing plugin interactions pass.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Reproduce with an uninitialized settings model through MainWindow, fix view lifecycle loading after model binding, and run focused desktop and fullscreen regressions.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Reproduced the screenshot through MainWindow with an uninitialized model: first opening the lazy Plugins pane yielded zero cards instead of three. The attach event precedes DataContext binding. Added loading on bound-model arrival and visible-tab reopening, guarded by attachment state. The regression now verifies initial cards and runtime-state refresh on reopening; a separate fullscreen test starts without desktop preloading. All 21 PluginSettingsInteractionTests and FullscreenSettingsTests passed using scratch output because the production app is running. git diff --check passed. No production library changes or app restart performed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Fixed desktop plugin loading after lazy-pane model binding and on reopening. Confirmed the original failure before the fix, then passed 21 focused desktop/fullscreen tests.
<!-- SECTION:FINAL_SUMMARY:END -->
