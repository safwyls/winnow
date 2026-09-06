---
id: TASK-145
title: Replace grid card flips with hover action icons
status: Done
assignee:
  - '@codex'
created_date: '2026-09-06 19:24'
updated_date: '2026-09-06 19:44'
labels:
  - ui
dependencies: []
references:
  - design-system.md
modified_files:
  - design-system.md
  - docs/decisions.md
  - src/Winnow.App/Themes/Colorimetry.cs
  - src/Winnow.App/Themes/ThemeAudit.cs
  - src/Winnow.App/Themes/WinnowTheme.cs
  - src/Winnow.App/Themes/controls.axaml
  - src/Winnow.App/Themes/tokens.axaml
  - src/Winnow.App/ViewModels/GameTileViewModel.cs
  - src/Winnow.App/ViewModels/LibraryViewModel.cs
  - src/Winnow.App/ViewModels/TileEntry.cs
  - src/Winnow.App/Views/GameTileView.axaml
  - src/Winnow.App/Views/MainWindow.axaml.cs
  - tests/Winnow.Tests/InstallActionRefreshTests.cs
  - tests/Winnow.Tests/ThemeContrastTests.cs
  - tests/Winnow.Tests/TileActionsTests.cs
  - tests/Winnow.Ui.Tests/CardDetailsInteractionTests.cs
priority: high
type: enhancement
ordinal: 172000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The library grid should keep each cover stable instead of flipping to a back face. Hovering a tile should reveal compact primary-action and Details icon buttons over the cover. The primary icon follows the honest existing action for that copy (Play when installed, Install when not installed); Details always opens the modal, and double-clicking the tile remains a Details shortcut.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Grid cards no longer flip, render a back face, or retain flip state in view models and library navigation
- [x] #2 Hovering a grid card reveals compact icon buttons for its available Play or Install action and for Details, with tooltips, accessible names, stable hit targets, and visible keyboard focus
- [x] #3 Double-clicking the non-control area of a grid card still opens Details, while presses on either icon run only that icon action
- [x] #4 Dormancy restoration, unread and store marks, selection, keyboard navigation, context menus, and list view behavior remain intact
- [x] #5 The design system and tests describe and enforce the stable-card interaction
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Collapse GameTileView to one stable cover face and remove flip styles, back-face markup, flip state, and flip lifecycle handlers. 2. Add compact hover/focus action buttons for the existing primary Play/Install action and Details, with tooltips, accessible names, stable hit targets, and visible keyboard focus. 3. Preserve card selection, context menus, keyboard navigation, and non-control double-click opening Details while nested controls invoke only their own commands. 4. Replace flip-specific unit and headless UI coverage with hover-action, hit-target, double-click, and install-refresh coverage. 5. Update the visual specification and decision history, then run focused and full build/test verification plus visual QA.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Removed the grid card's back face and all persisted flip state. The compact primary action remains honest to install state: Play when installed, Install otherwise; Details is always present. The fixed icon dock reveals on pointer hover or keyboard focus. The existing command bar and context routes continue to own Add to list.

Validation: focused CardDetailsInteractionTests passed 14/14; focused TileActionsTests and InstallActionRefreshTests passed 51/51; density-floor headless capture was inspected; full solution build passed with 0 warnings/errors; full solution tests passed 3749/3749.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Replaced the grid card flip with stable 32px Play/Install and Details hover/focus icons, preserved selection and double-click Details behavior, removed flip state and lifecycle code, and aligned tests plus the visual specification. Verified by headless interaction coverage, a 108px visual capture, a warning-free full build, and 3749 passing tests.
<!-- SECTION:FINAL_SUMMARY:END -->
