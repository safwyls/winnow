---
id: TASK-312
title: Keep segmented selector fills inside rounded borders throughout the app
status: Done
assignee:
  - '@codex'
created_date: '2026-09-16 15:39'
updated_date: '2026-09-16 15:53'
labels: []
dependencies: []
ordinal: 354000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Selected square backgrounds in the grid/list switch and other tab selector bars overpaint their enclosing border and rounded corners. Audit equivalent controls throughout desktop and fullscreen and correct containment across interaction states.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 All desktop segmented and tab selector groups are inventoried and affected selected, hover and pressed fills stay inside their border and radius.
- [x] #2 Fullscreen equivalents are audited and corrected where applicable, preserving visible keyboard/controller focus.
- [x] #3 Representative rendered checks and focused interaction tests cover endpoint segments, themes and selector state changes; the shared visual rule is documented.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Inventory desktop and fullscreen selector implementations; fix shared containment and exceptional controls; verify rendered first/last selected and hover states, focus and geometry with focused UI tests; document audit coverage and results.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit inventory: affected desktop groups are MainWindow grid/list, MainWindow Settings sections, StoresView Steam/Epic/GOG, and MergeQueueView generated kind filter. Each now wraps the segment panel in shared Border.segment-clip (3px inner radius inside 1px rule and 4px outer radius). Safe desktop exclusions: ActivityTracker range radios, Stats section buttons, GameDetails underline tabs, Appearance option cards, journal rating dots, sidebar rows and code-created controls; these have no enclosing segmented border or paint their own fill and border together. Fullscreen audit: root, Library collections, Activity sections and summary/statistics choices, Details and Settings tabs use underline navigation without rounded enclosing borders. Shelf dots own their circular border/fill and toggle thumbs are inset inside rounded tracks. No fullscreen implementation change required. App build passed with zero warnings/errors.

Final rendering verification: shared inner rounded Border clipping passes exact exterior-corner pixel comparisons for first/last selected, hovered and pressed segments in Winnow and Rose Pine Dawn; disabling inner clipping reproduces the square-fill failure. Focus is drawn by an inset border in the shared segment template because an external focus adorner is clipped by the group. Keyboard focus remains visibly rendered without changing dimensions. Inspected selected and focused PNG captures under C:\Temp\winnow-selector-captures. Final suite passed all 54 cases across SegmentedSelectorContainmentTests, FullscreenNavigationLayoutTests, DesignTimePreviewTests, StoresAccountContextTests and MergeRowActionsTests. Build has no warnings/errors. No real library or launcher files used.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Audited desktop and fullscreen selector families. Fixed all four affected rounded desktop groups with shared inner clipping and an inset keyboard-focus outline. Fullscreen underline selectors require no changes. Verified dark/light rendered corners with a negative control, stable geometry, four production sites and 54 passing focused tests.
<!-- SECTION:FINAL_SUMMARY:END -->
