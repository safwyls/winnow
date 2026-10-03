---
id: TASK-386
title: Show only appearance controls supported by the selected composition
status: Done
assignee:
  - '@codex'
created_date: '2026-10-03 20:02'
updated_date: '2026-10-03 20:19'
labels: []
dependencies: []
type: bug
ordinal: 464000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Appearance currently exposes settings that some bundled compositions ignore, making controls seem broken and cluttering the menu. Hide irrelevant controls while keeping saved composition preferences intact and useful shared settings available.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Theme Studio shows only controls consumed by the selected bundled composition, and switching compositions updates visible sections without discarding their saved settings.
- [x] #2 Desktop, fullscreen Settings and setup hide composition-specific controls that do not apply, including empty section headings.
- [x] #3 Focused component and native rendered checks cover all four compositions on both surfaces; relevant documentation is updated.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Audit actual profile and theme-setting consumers, define shared composition applicability, condition Theme Studio and fullscreen/setup appearance controls, preserve preference storage, and verify switching and rendered behavior before committing.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audited real CSS/component consumers for Avalon, Afterglow, Rift and Catalogue. Shared applicability rules now hide unused Studio color/font/density/shade/material/layout controls, plus Avalon-only fullscreen role typography and ultrawide fit in Settings/setup. Avalon shared color controls appear only for Studio colors. Rift retains its working details arrangement. Hidden values survive composition switches and fullscreen resets. Installed themes retain the shared API controls. Desktop and fullscreen were verified separately: 23 Studio/runtime component tests and 53 Settings/setup component tests passed; all 7 native appearance cases passed, followed by 2 extended native cases exercising all four compositions through real Settings navigation. Reviewed rendered Atmosphere and Settings captures in .tmp/task386-native-appearance and .tmp/task386-native-settings. Build and final TypeScript check passed; migration audit reports 2,435 contracts, zero pending/partial. Updated design-system.md and docs/electron-themes.md. Build emits existing third-party Zod annotation warnings.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Appearance menus now show controls relevant to the selected bundled composition across Theme Studio, desktop/fullscreen Settings and setup, preserving hidden saved preferences. Verified with 76 focused component tests, 7 native appearance cases plus extended Settings rechecks, build/type checks and complete migration audit.
<!-- SECTION:FINAL_SUMMARY:END -->
