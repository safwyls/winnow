---
id: TASK-230
title: Create the user plugins folder during startup
status: Done
assignee:
  - '@codex'
created_date: '2026-09-11 15:20'
updated_date: '2026-09-11 15:21'
labels: []
dependencies: []
type: bug
ordinal: 262000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Startup skips a missing user plugins directory, so Open plugins folder fails on a fresh installation.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Startup discovery creates the user plugins folder and preserves existing contents.
- [x] #2 Directory creation failure is reported without blocking bundled plugins.
- [x] #3 Desktop and fullscreen use the initialized folder; documentation and focused tests cover the behavior.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Create the user directory in shared plugin discovery with a soft failure diagnostic. Add missing-directory and failure coverage, verify both settings paths, and update plugin installation documentation.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Shared startup discovery now creates the selected data-directory plugins folder. Desktop EnrichmentSettingsView and FullscreenSettingsPage both open the UserPluginDirectory supplied by PluginSettingsBackend. Verified shared filesystem behavior through 37 passing Winnow.Plugins.Tests, including new missing-folder and file-collision cases and existing plugin discovery/restart coverage. No manual shell-launch interaction performed on either surface. Plugin documentation updated; git diff --check passed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Create the user plugins directory during startup discovery, preserving installed packages and reporting filesystem failures without blocking bundled plugins. All 37 plugin tests pass.
<!-- SECTION:FINAL_SUMMARY:END -->
