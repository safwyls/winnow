---
id: TASK-246
title: Place fullscreen trigger hints beside section headers
status: Done
assignee:
  - '@codex'
created_date: '2026-09-12 18:53'
updated_date: '2026-09-12 18:56'
labels: []
dependencies: []
ordinal: 287000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Move Library, Activity and Settings LT/RT cues from the footer to either side of the section choices, using the same controller glyphs as the main navigation.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 LT and RT bracket the corresponding section choices in Library Activity and Settings without changing input behavior
- [x] #2 Duplicate footer hints are removed and rendered keyboard/controller regression checks pass
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add existing trigger glyphs to section rows and remove relocated footer hints. 2. Verify layouts and trigger navigation with focused fullscreen checks; update visual spec and commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Added a shared non-focusable LT/RT bracket using the existing32px controller glyphs. Library brackets only its four trigger-cycled collections, Activity brackets its sections, and Settings pins glyphs outside its horizontal tab scroll. Removed duplicate footer prompts. Desktop has no corresponding controller section rail and is unchanged. Inspected all three real fullscreen-shell captures at100%/140%;32 focused UI tests pass and build has zero warnings/errors.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Moved LT/RT glyphs beside Library, Activity and Settings section headers, matching main-menu bumper cues. Settings glyphs stay visible while tabs scroll. Verified captures at100%/140%,32 passing UI checks and clean build.
<!-- SECTION:FINAL_SUMMARY:END -->
