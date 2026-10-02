---
id: TASK-237
title: Keep merge candidates within the pane at narrow widths
status: Done
assignee:
  - '@codex'
created_date: '2026-09-12 04:35'
updated_date: '2026-09-12 04:43'
labels: []
dependencies: []
type: bug
ordinal: 278000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Some merge proposal cards extend past the right edge at smaller desktop widths, hiding the ends of candidate rows and controls. Constrain long titles to the available space and verify the separate fullscreen merge flow.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 At the 1200px desktop minimum and wider sizes, long merge titles stay within their cards and all right-side actions remain visible and usable.
- [x] #2 Fullscreen merge proposals and member actions remain within their surface with long titles.
- [x] #3 Rendered verification and focused checks cover the affected layouts; the visual specification records the sizing rule.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Reproduce overflow with long proposal and candidate titles at the desktop minimum pane width. 2. Constrain title and badge layouts while preserving existing trailing controls and short-title spacing. 3. Exercise rendered desktop sizing and right-side actions, plus the separate fullscreen proposal/member flow. 4. Update the visual sizing rule and record checks.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Reproduced the desktop failure with a long title: the card reached x=953 in a 950px pane. Two issues contributed: ScrollViewer padding did not reduce the content measurement width, and horizontal title stacks allowed badges and metadata to overlap. Queue spacing now uses a content margin; title groups use bounded grid columns. Short titles retain adjacent labels.

Desktop verification: real-font headless rendering at pane widths 1670, 950, 1030, then 950 again (reserving shell space at the 1200px window minimum); long title, two store chips, card/header/row bounds and non-overlap, Details pointer activation, inclusion checkbox toggling, and resolved strip containment. Fullscreen verification: actual FullscreenView shell at 1920x1080 and 1280x720, proposal/member bounds and keyboard Make header behavior; its existing composition needs no production change. Rendered frames inspected in C:/Temp/winnow-merge-layout-after.

Validation: dotnet build -p:BaseOutputPath=C:/Temp/winnow-merge-verify/ succeeded with zero warnings/errors; all 17 merge-related UI tests and 184 merge-related domain/source checks passed. The four MergeRowActionsTests passed again after tightening the non-overlap assertions. Tests use temporary SQLite data and the production host was not started.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Kept merge cards inside narrow panes by measuring the content inset and constraining long titles while preserving badges and right-side actions. Documented the desktop sizing rule. Verified rendered desktop resize and fullscreen flows, a clean solution build, and 201 merge-related checks.
<!-- SECTION:FINAL_SUMMARY:END -->
