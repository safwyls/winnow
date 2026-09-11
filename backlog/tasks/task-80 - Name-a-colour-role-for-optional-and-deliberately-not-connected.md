---
id: TASK-80
title: Standardize the optional and unconnected provider state treatment
status: Done
assignee:
  - '@codex-ui'
created_date: '2026-09-03 00:58'
updated_date: '2026-09-11 19:24'
labels: []
dependencies: []
documentation:
  - design-system.md
priority: low
ordinal: 107000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Document a shared semantic treatment for optional providers that are deliberately not connected. Desktop currently uses a neutral Line/TextDim pill, distinct from Volt success and Amber attention. Define the component/state contract and use it consistently; the task does not require a new hue or identical pill geometry on fullscreen.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The visual specification names optional/unconnected semantics and the existing or chosen tokens/component treatment.
- [x] #2 Desktop provider status components use that contract instead of per-screen inventions.
- [x] #3 Fullscreen communicates the same state meaning in its own layout, with accessible names and no false error or degraded-service implication.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Name the optional/unconnected state in the visual spec using neutral Line/TextDim tokens. 2. Move desktop provider status styles into the shared component stylesheet and expose provider-qualified accessible state labels on desktop/fullscreen. 3. Verify all three providers on both surfaces with headless rendering and commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: StoresView.axaml contains a local neutral status-pill style, while StoresViewModel and FullscreenPlatformTools expose connection state. The named shared semantic/component contract remains unspecified.

Named optional/unconnected as neutral Line/TextDim and moved provider-status component states into controls.axaml. Desktop uses the shared component; fullscreen shows accessible status text including local-only GOG. Six headless provider/surface cases passed, checking visible accessible labels and neutral versus live desktop states.

Accessibility follow-up: provider status TextBlock peers expose their Text and ignore AutomationProperties.Name. Removed ineffective Name bindings from desktop status text and fullscreen platform prose. The six provider/surface cases now assert the real automation peer name instead of merely reading the attached property.

Follow-up validation: all thirty Release StoresAccountContextTests and JournalNotificationTests passed together, including real peer-name assertions for all three provider states on desktop and fullscreen. The coordinator also verified the source accessibility reachability rule with these changes.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Standardized provider-state semantics and shared desktop styling, with accessible status lines on fullscreen. Six headless cases verify Steam, Epic and GOG on both surfaces.
<!-- SECTION:FINAL_SUMMARY:END -->
