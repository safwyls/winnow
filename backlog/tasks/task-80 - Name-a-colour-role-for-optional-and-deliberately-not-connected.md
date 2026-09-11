---
id: TASK-80
title: Standardize the optional and unconnected provider state treatment
status: To Do
assignee: []
created_date: '2026-09-03 00:58'
updated_date: '2026-09-11 14:04'
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
- [ ] #1 The visual specification names optional/unconnected semantics and the existing or chosen tokens/component treatment.
- [ ] #2 Desktop provider status components use that contract instead of per-screen inventions.
- [ ] #3 Fullscreen communicates the same state meaning in its own layout, with accessible names and no false error or degraded-service implication.
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: StoresView.axaml contains a local neutral status-pill style, while StoresViewModel and FullscreenPlatformTools expose connection state. The named shared semantic/component contract remains unspecified.
<!-- SECTION:NOTES:END -->
