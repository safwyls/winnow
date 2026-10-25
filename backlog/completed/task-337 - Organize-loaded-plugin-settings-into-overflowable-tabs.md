---
id: TASK-337
title: Organize loaded plugin settings into overflowable tabs
status: Done
assignee:
  - '@codex'
created_date: '2026-09-17 17:54'
updated_date: '2026-09-17 18:05'
labels: []
dependencies: []
ordinal: 379000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Plugin settings currently stack every plugin in one long page. Give each loaded plugin a dedicated settings tab and make overflow reachable with arrows at both ends of the strip.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Each runtime-loaded plugin has a named tab displaying only its settings, with stable selection and retained non-secret drafts across tab switches.
- [x] #2 Left and right arrows appear when the tab strip overflows; scrolling, keyboard selection and resize keep all tabs reachable and the selected tab visible.
- [x] #3 Installation, empty state, unloaded/disabled packages and diagnostics remain reachable; install-to-settings navigation selects the appropriate plugin.
- [x] #4 Desktop and fullscreen implement and verify equivalent plugin navigation, with accessible names, controller/keyboard focus, rendered layout review and updated documentation.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Add shared stable selected-plugin state and a Manage tab for installation/diagnostics; render loaded plugin tabs with bounded horizontal scrolling and overflow arrows on desktop and fullscreen; verify selection, refresh/install handoff, drafts, focus and rendered sizes, then update docs.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented one tab per runtime-loaded plugin plus Manage plugins for installation, unloaded packages and diagnostics. Shared selection keeps existing card/tab instances and plugin identity across refresh; first loaded plugin is initial selection. Desktop uses native TabControl semantics with Left/Right/Home/End. Both surfaces pin the tab strip, expose left/right overflow arrows only when needed, disable arrows at the corresponding edge and reveal selected tabs after resize. Ordinary drafts survive tab changes; outgoing secret drafts and sign-in challenges follow existing cleanup. Fullscreen embeds the generated plugin form and preserves section triggers, controller focus rows and install handoff. Fixed selected fullscreen tab clipping after window shrink based on a failing resize regression. Updated legacy tests to select Manage plugins before asserting its content. Verified rendered desktop at 700x700, 1280x820 and 1600x800, fullscreen 1280x720 and 1920x1080 with 140% text; captures in C:\Temp\winnow-plugin-tabs-captures. Independent source review found no additional issues; its attached-catalog-removal coverage suggestion was added and passed. Final verification: solution build zero warnings/errors; 86 plugin unit/integration tests and 69 focused UI tests passed, including keyboard/controller overflow, resize, empty/diagnostic states, attached catalog removal and install handoff. Additional accessibility-focused run passed 58 checks.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Organized loaded plugin settings into individual tabs on desktop and fullscreen, with overflow arrows and a Manage plugins tab. Preserved draft and selection behavior, installation handoff and existing cleanup. Verified rendered layouts, resize and input regressions, plugin tests and a clean solution build; updated design and plugin documentation.
<!-- SECTION:FINAL_SUMMARY:END -->
