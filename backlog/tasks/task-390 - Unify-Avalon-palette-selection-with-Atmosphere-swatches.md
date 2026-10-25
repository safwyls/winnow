---
id: TASK-390
title: Unify Avalon palette selection with Atmosphere swatches
status: Done
assignee:
  - '@codex'
created_date: '2026-10-09 18:26'
updated_date: '2026-10-09 18:33'
labels: []
dependencies: []
type: bug
ordinal: 468000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Avalon puts named palettes in a separate details dropdown while the other compositions offer visible swatches in Atmosphere. The inconsistent location and presentation make colors harder to find and compare.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Desktop and fullscreen show Avalon palettes as selectable color swatches in Atmosphere with the same Palette labeling and presentation as other compositions; no separate Avalon palette dropdown remains.
- [x] #2 Bundled and authored palettes, selected-state accessibility, saved selections, composition switching and Studio colors customization continue to work.
- [x] #3 Focused component and native Electron checks pass for both surfaces, rendered swatches are reviewed, and appearance documentation is updated.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Reuse one swatch picker in Theme Studio for shared presets and the live Avalon palette catalog; retain the existing profile settings and hide the duplicate palette field.
2. Preserve Studio colors and its starting presets, migrate existing UI tests to swatch selection, and cover catalog updates and saved selections.
3. Run focused component, build and native desktop/fullscreen checks, inspect screenshots, update docs and commit the milestone.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented one PaletteSwatches control for Atmosphere across all compositions. Avalon reads the live bundled/authored palette catalog, retains its existing settings.avalon.palette persistence and Studio colors edits, and no longer renders the duplicate details dropdown. Shared Studio starting presets remain available only when Studio colors is selected. Updated native palette interactions without weakening the existing persistence, typography, layout and accessibility assertions.

Verification: production build and final typecheck pass. Six focused component suites cover 118 cases; after correcting the new removal assertion to await the existing asynchronous fallback, all pass (117 passed in the initial run, then both affected suites passed all 18 cases). Native appearance/authored/session checks passed 10 cases initially; the new exact-count assertion was corrected to allow exported/authored palettes, and a follow-up run passed all 9 cases covering both composition surfaces, keyboard selection, typography, layout and reduced motion: 19 unique native cases passed, one worker and zero retries. Reviewed desktop and fullscreen Avalon Atmosphere captures plus Afterglow comparison at .tmp/task390-palette-consumers; swatches wrap cleanly with preserved selected state. Captures for all four compositions are retained.

Migration audit uncovered stale case names from TASK-389 in three evidence maps. Updated the references and cache-lifetime descriptions to the verified current backdrop tests; no backdrop implementation was changed here. Complete audit passes all 2435 contracts with zero pending or partial. Backend and full unrelated test suites were not rerun for this UI-only change.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Avalon now uses the same Atmosphere → Palette swatch picker as every other composition on desktop and fullscreen. Original and authored palettes, Studio colors and saved selections remain supported. Build/typecheck, 118 focused component cases, 19 native cases, visual review and complete migration audit pass.
<!-- SECTION:FINAL_SUMMARY:END -->
