---
id: TASK-309
title: Correct Dawn theme control contrast on desktop and fullscreen
status: Done
assignee:
  - '@codex'
created_date: '2026-09-16 05:24'
updated_date: '2026-09-16 05:37'
labels: []
dependencies: []
type: bug
ordinal: 351000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Review of Rose Pine Dawn and SilkCircuit Dawn found dark Fluent controls under light palettes, low-contrast accent labels/focus outlines, faint control borders, and destructive-button hover labels. The user requested reviewing and correcting visibility, with light-theme metadata if needed.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Theme JSON supports an optional light/dark variant that is exported and applied to Fluent controls, with compatible behavior for existing theme files.
- [x] #2 Both Dawn themes provide readable action labels and visible focus/control boundaries in normal, hovered, pressed and selected states on desktop and fullscreen.
- [x] #3 Contrast measurements, theme parsing tests, and rendered desktop/fullscreen control checks verify the fixes and dark-theme behavior.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Add explicit variant metadata with palette-based fallback. Derive contrast-safe accent foregrounds for light surfaces while retaining filled accents; update action/focus consumers and tune Dawn boundaries/destructive states. Extend audit coverage, add regression tests, inspect rendered controls on both surfaces, and document measured limits.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented optional variant metadata with legacy palette inference and export. Light palettes select Fluent Light and derive separate accent foregrounds while retaining authored button fills; both Dawn palettes strengthen boundaries and destructive hover colors. Desktop and fullscreen consumers updated; dark accent mappings preserved. Validation: 227 core theme/architecture/documentation tests passed. Full UI suite: 765 passed, one old Volt resource identity assertion corrected; final affected groups passed 15/15 including six Dawn rendered checks. Captures inspected in C:/Temp/winnow-309-captures. Measured default opaque surfaces: accent labels >=4.5:1, boundaries >=3:1, primary/destructive labels >=4.5:1. Arbitrary artwork/transparency and user-authored palette overrides remain outside those guarantees; evidence in docs/spikes/dawn-control-contrast.md. git diff --check passed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Corrected Dawn control visibility across desktop and fullscreen with light variant metadata, contrast-safe foreground roles, stronger boundaries and destructive states. Verified with 227 core checks, rendered interaction tests for both palettes, and the UI suite plus corrected-assertion rerun. Documented measurements and limitations.
<!-- SECTION:FINAL_SUMMARY:END -->
