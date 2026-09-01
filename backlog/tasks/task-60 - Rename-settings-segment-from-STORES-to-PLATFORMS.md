---
id: TASK-60
title: Rename settings segment from STORES to PLATFORMS
status: In Progress
assignee:
  - '@claude'
created_date: '2026-09-01 02:50'
updated_date: '2026-09-01 03:15'
labels:
  - ui
  - docs
dependencies: []
priority: low
ordinal: 77000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The settings rail segment labelled STORES should read PLATFORMS. An earlier rename was reverted by a documentation correction that treated the code as the source of truth. The user intent is PLATFORMS. The segment label, any screen or rail copy that echoes it, and every documentation reference should be renamed in one pass so they cannot drift apart again.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The rail segment in MainWindow.axaml reads PLATFORMS
- [ ] #2 No remaining occurrence of STORES in the settings rail, related views, or documentation refers to this segment
- [ ] #3 Build succeeds and no test regresses
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Make the segment label a view-model fact so a test can see it: add StoresViewModel.SegmentLabel => "PLATFORMS" beside Title ("Platforms", already correct).
2. MainWindow.axaml: the settings segmented control's first button binds Text to Stores.SegmentLabel instead of the literal STORES. Tooltip copy refreshed by docs-writer (the segment now also holds purchase import, per TASK-59).
3. Documentation, same pass: README.md line 54 'SETTINGS > STORES' -> 'SETTINGS > PLATFORMS'; design-system.md section 13 gap 3 'SOURCES/STORES' -> 'SOURCES/PLATFORMS'. Both delegated to docs-writer.
4. Out of scope deliberately: the StoresView/StoresViewModel type names and design-system's prose about 'the Stores panel' (the panel's identity in code and in the design record, not the segment label the user reads).
5. Test: StoresViewModelTests asserts SegmentLabel == "PLATFORMS" and that no user-facing label property on the panel reads STORES.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Done, not finalized. The segment label is now a view-model property, StoresViewModel.SegmentLabel => "PLATFORMS", bound by MainWindow.axaml along with a new SegmentTooltip. It lived only as a literal in a XAML attribute before, which is why the rename could drift back without a test noticing.

Changed: src/Winnow.App/ViewModels/StoresViewModel.cs (SegmentLabel, SegmentTooltip), src/Winnow.App/ViewModels/SteamConnectionCopy.cs (SegmentTooltip constant), src/Winnow.App/Views/MainWindow.axaml (the segment binds the two properties). Documentation, authored by the docs-writer subagent: README.md line 52 heading 'Connecting stores' -> 'Connecting platforms', line 54 'SETTINGS > STORES' -> 'SETTINGS > PLATFORMS', plus one sentence pointing at the Steam entry for purchase import; design-system.md line 1011 'SOURCES/STORES' -> 'SOURCES/PLATFORMS'.

Deliberately unchanged: the StoresView/StoresViewModel type names, design-system.md's section 13 heading and its other 'Stores panel' prose, ROADMAP.md's 'Stores-panel toggle'. Those name the panel in code and in the design record, not the segment label the user reads.

Test: StoresViewModelTests.The_settings_segment_reads_platforms asserts SegmentLabel == "PLATFORMS", Title == "Platforms", and that neither the label nor the tooltip contains STORES.

Verified: full suite 2398 + 98 + 70 passed, 0 failed. Not committed.
<!-- SECTION:NOTES:END -->
