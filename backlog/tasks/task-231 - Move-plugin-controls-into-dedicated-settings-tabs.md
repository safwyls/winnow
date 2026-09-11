---
id: TASK-231
title: Move plugin controls into dedicated settings tabs
status: Done
assignee:
  - '@codex'
created_date: '2026-09-11 15:22'
updated_date: '2026-09-11 15:29'
labels: []
dependencies: []
type: enhancement
ordinal: 263000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Give plugin installation and provider configuration their own Plugins tab on desktop and fullscreen, separate from Metadata and artwork.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Desktop Plugins tab contains installation, folder access, provider enablement, fields, secrets and refresh controls; Metadata and artwork retains IGDB and artwork ordering.
- [x] #2 Fullscreen Plugins section offers the same provider controls with working controller navigation.
- [x] #3 Navigation clears unsaved secrets, remembers the selected desktop tab, and existing plugin interactions pass focused UI tests.
- [x] #4 Settings and plugin documentation describe the new navigation.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Extract the desktop plugin view and add shell tab state and navigation. Delegate the fullscreen section move. Update interaction tests and documentation, run focused headless UI checks and inspect rendered layout.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Desktop: extracted PluginSettingsView into its own lazy settings tab, preserving provider controls and clearing drafts when navigating away. Keyboard activation, tab memory, separation from IGDB, and tab bounds at 1200px verified by headless tests; rendered desktop shell inspected. Fullscreen: dedicated Plugins section, controller trigger navigation, and horizontal tab scrolling with focused-tab reveal verified, including 1000px overflow coverage. Shared provider forms retain saving, secret removal and restart state. Fullscreen accessibility tests include Plugins. Full UI suite: 462 passed; four controller-guide assertions rejected the new horizontal scroll strip. Updated those assertions to retain the no-vertical-scroll requirement; rerun of all FullscreenSettingsTests and PluginSettingsInteractionTests passed 17/17. Earlier focused interaction/accessibility run passed 21/21. Desktop and fullscreen captures inspected. Documentation updated and git diff --check passed. No production host or real library used.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Moved plugin installation and configuration into dedicated desktop and fullscreen Plugins tabs. Preserved shared settings, secret cleanup, and controller navigation; added overflow handling for fullscreen tabs. Verified through headless interaction/accessibility tests and rendered captures.
<!-- SECTION:FINAL_SUMMARY:END -->
