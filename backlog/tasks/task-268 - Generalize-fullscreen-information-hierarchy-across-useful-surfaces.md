---
id: TASK-268
title: Generalize fullscreen information hierarchy across useful surfaces
status: Done
assignee:
  - '@codex'
created_date: '2026-09-13 21:21'
updated_date: '2026-09-13 21:29'
labels: []
dependencies: []
ordinal: 310000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Codify Overview typography and grouping as reusable fullscreen guidance, audit fullscreen surfaces, and apply it where mixed information needs clearer hierarchy. Preserve task-specific grids, compact controls, shared operations and controller behavior.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Design system defines reusable type roles, spacing, dividers, actions, accessibility and appropriate exceptions.
- [x] #2 Audited fullscreen information surfaces adopt shared hierarchy where beneficial without changing domain behavior.
- [x] #3 Representative rendered layouts and interaction tests pass, with desktop assessment and audit decisions recorded.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Extract reusable fullscreen presentation helpers from details; audit settings, activity, reading/editor/tool screens; apply bounded improvements by surface; verify renders and existing UI interaction tests, then update guidance and record audit evidence.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit and implementation: shared FullscreenInformation provides BodyFont roles (18 section / 32 bold item / 24 body-link / 22 metadata), bounded columns, wrapping links and group rules; Details adopts shared helpers. Settings/IGDB/plugin/artwork pages separate titles, explanations, controls and semantic groups. Activity, response history and play history separate titles/date facts and events; preview and reading pages gain clear groups and bounded prose. Journal editors group session context, note, rating and actions. Library tools/identity proposals distinguish subjects and evidence; purchase import separates sign-in, saved pages, hints and results. Updated numeric typography guidance to distinguish fullscreen prose from desktop aligned data. Desktop source and shared domain behavior unchanged.

Audit exceptions: cover shelves/library/search grids retain artwork-led layout; tab strips, action menus, keyboard and file picker retain compact navigation; charts and aligned numeric controls retain data typography. Setup, metadata/match/manual-game forms and platform connection tools keep existing field/task layouts; adding reading-row dividers everywhere would add noise. Controller diagrams and brand previews retain their specific scale. Existing editable, checked, progress, error, accessibility and focus behavior preserved. Rendered fixtures inspected for enlarged Library settings, IGDB and journal layout. UI full-suite verification ongoing.

Final verification: Release build and all 649 Winnow.Ui.Tests passed. Three targeted Activity/reading capture tests also passed; inspected resulting Activity and 1280/1920 reading renders, with bounded wrapping, separate metadata and persistent Back. Six settings cases cover Library/credentials/artwork at 100% and 140% text; two journal cases cover 1280/2560 widths. Full UI suite includes desktop interactions, parity, accessibility, controller navigation and shared-state checks. Updated test lookups to preserved automation names after structured content changes, and backdrop theme expectation to its four existing gradients. Initial capture-enabled whole-suite attempt exposed an unrelated optional setup screenshot null frame; final whole-suite run omits global capture and targeted renders pass. Page-only Activity/journal/reading captures prove geometry but do not include shell styles; settings captures include the real fullscreen shell. No physical-controller or seating-distance measurement performed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Codified reusable fullscreen information hierarchy and shared presentation helpers, then applied them to settings, Activity/history, reading, journals, Library tools and purchase import. Preserved specialized browsing/control/chart layouts and shared operations. Verified all 649 Release UI tests and representative normal/enlarged rendered layouts; audit and desktop assessment recorded.
<!-- SECTION:FINAL_SUMMARY:END -->
