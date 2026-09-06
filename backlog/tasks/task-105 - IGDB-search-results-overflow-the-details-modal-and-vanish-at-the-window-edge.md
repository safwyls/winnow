---
id: TASK-105
title: IGDB search results overflow the details modal and vanish at the window edge
status: Done
assignee:
  - '@codex'
created_date: '2026-09-04 22:50'
updated_date: '2026-09-06 17:31'
labels:
  - ui
dependencies:
  - TASK-89
priority: high
type: bug
ordinal: 132000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The manual IGDB assignment control (TASK-89) renders its candidate list unconstrained. With more than three results the list extends past the bottom of the details modal and is then clipped at the edge of the main window, so the remaining candidates cannot be seen or reached. IGDB search returns far more than three results for a common title, so this is the normal case rather than an edge case.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The candidate list scrolls within a bounded height instead of extending past the modal
- [x] #2 The list is reachable by keyboard as well as by pointer, and focus stays visible while scrolling
- [x] #3 A result set larger than the visible area is evidently scrollable rather than silently cut off
- [x] #4 The modal itself does not grow past the window with a long result set
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Review current details view against design-system.md 10.9. 2. Exercise the compiled view with twenty candidates in an isolated Avalonia headless harness: Tab, visible focus, pointer scrolling, overflow cues and window containment. 3. Fix demonstrated defects and close with measured evidence.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
ROOT CAUSE, and it was not only the candidate list. The card carries MaxHeight 720 and a Border does not clip, so nothing stopped content drawing outside it. The right column's rest band sat in an Auto row: an Auto row is measured against infinity, so the ScrollViewer in it took its content's full height and never scrolled. The modal's scrollbar was inert, and anything past the card was drawn outside it and cut off at the window edge. The IGDB candidate list was simply the first content long enough for a user to notice.

MEASURED, NOT ASSUMED. A throwaway console project against Avalonia 11.3.20 (the version this app references) measured a Border of MaxHeight 720 holding a two-row Grid, both row modes, short content and content far past the bound:

  rows=Auto,* bottomHeight=50    gridDesired=0,150  hostDesired=0,150  bottomBounds=0,100,500,50
  rows=Auto,* bottomHeight=2000  gridDesired=0,720  hostDesired=0,720  bottomBounds=0,-590,500,2000
  rows=Auto,Auto bottomHeight=50   gridDesired=0,150  hostDesired=0,150
  rows=Auto,Auto bottomHeight=2000 gridDesired=0,720  hostDesired=0,720  bottomBounds=0,100,500,2000

Three things follow. A star row sizes to content when the content fits, so the modal does not grow to its maximum on a short game. A star row's arrange slot is bounded by the card's own height when it does not (the 2000px child is centred in a 620px slot, at y=-590), which is the bound a ScrollViewer scrolls inside. An Auto row keeps its measured height instead (the child sits at y=100 with height 2000), which is the overflow. The scratch project is in the session scratchpad and is not in the repository; a probe test written into tests/Winnow.Tests was deleted after it answered.

WHAT CHANGED, all in src/Winnow.App/Views/GameDetailsView.axaml plus one handler in its code-behind.
- Right column rows Auto,Auto,Auto to Auto,Auto,*. The rest band now scrolls inside the card instead of drawing past it. This fixes every long section, not only this one.
- Left column is a two-row Grid: the cover fixed, and everything under it (STEAM APPID, ON DISK, the IGDB control) in a scroll region on a star row. Needed because TASK-102 moves the control here and an open search would otherwise push the column past the card - the same defect in the other column.
- The candidate list is a ScrollViewer of MaxHeight 208 with VerticalScrollBarVisibility Auto, the pattern the Stores modals already use (StoresView MaxHeight 440 / 380 / 460). 208 fits two rows and most of a third, so a row is cut part way rather than the list ending on a row boundary; that cut and the scrollbar are the two cues.
- Both new ScrollViewers take Classes=inner, so their scrollbars opt out of the window resize-border inset (section 9.1): their edge is a divider of ours, not the window's.
- GameDetailsView.axaml.cs gains OnCandidateGotFocus, which calls BringIntoView on the focused row. Avalonia's ScrollViewer.BringIntoViewOnFocusChange defaults to true, but this repository has not measured that path, and the request also has to travel through the left column's scroll region; the handler makes it explicit.

NO NEW COPY. Nothing user-facing was added, so GameIgdbMatchCopy is untouched.

AC 1 checked: the list is a ScrollViewer with MaxHeight 208. The probe above shows a container clamping to its MaxHeight with content far past it; a ScrollViewer whose extent exceeds its viewport is a scrolling ScrollViewer.
AC 4 checked: the card measures to min(MaxHeight, window height less its 40px margins) in both directions - the probe's short case sizes to content, the tall case clamps to the bound - and both bands are now star rows bounded by that height.
AC 2 and AC 3 deliberately unchecked. The assign control on each row is an ordinary Tab stop with the modal's existing focus ring, and the BringIntoView handler is in place, but no test in this repository drives focus or renders this view, so whether Tab reaches a row below the fold and whether the ring is visible there needs the running app. AC 3 is a judgement about what the cut row and the scrollbar look like, which also needs eyes.

VERIFICATION, PowerShell, scratch output path:
  dotnet build -p:BaseOutputPath=C:\Temp\winnow-h2\ -m:1   Build succeeded. 0 Warning(s) 0 Error(s)
  dotnet test tests\Winnow.Tests --no-build -p:BaseOutputPath=C:\Temp\winnow-h2\   Passed! - Failed: 0, Passed: 3098, Skipped: 0, Total: 3098
  dotnet test tests\Winnow.Recommend.Tests -p:BaseOutputPath=C:\Temp\winnow-h2\   Passed! - Failed: 0, Passed: 152, Total: 152
  dotnet test tests\Winnow.Covers.Tests -p:BaseOutputPath=C:\Temp\winnow-h2\   Passed! - Failed: 0, Passed: 70, Total: 70
Compiled bindings are on, so the build is a check that every binding in the rewritten markup resolves. The counts are above the 3066 / 152 / 70 baseline because other work landed in this tree; this change adds no tests, because nothing in it is testable below the view.

One run of Winnow.Tests failed IgdbResilienceTests.Rate_limiter_caps_the_initial_burst_and_spaces_the_rest_at_4_per_second, a wall-clock rate-limiter test, on a machine running several agents at once. It passed alone and passed on the next full run, quoted above.

2026-09-06 review: verified the current compiled GameDetailsView under Avalonia.Headless 11.3.20 with the real App resources, Fluent theme and Skia renderer. The current design places results in the right rest band and caps them at 238px (the earlier 208px/left-column notes describe an obsolete intermediate version). At 1200x640, 1280x820 and 1920x1080, twenty results measured a 1360px extent inside a 238px viewport. Real simulated Tab presses reached all twenty assign buttons in order; each focused button remained fully inside both enclosing scroll viewports, with the rendered presenter using the Volt border at constant 1px thickness. The final row reached offset 1122. Pointer wheel moved the inner list to offset 150. Inspected Skia captures at minimum window size: visible scrollbar and partial fourth row at the top, complete focus ring on result twenty, and card contained within 40px window margins. Harness and captures: C:/Temp/winnow-task105 (dotnet run --project Probe.csproj -- --data-dir C:/Temp/winnow-task105/data). No application change was needed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Already implemented. Closed after headless interaction and rendered-image verification of twenty candidates at three window sizes: bounded scroll, pointer wheel, all Tab stops with visible focus, scrollbar/partial-row overflow cues, and modal containment.
<!-- SECTION:FINAL_SUMMARY:END -->
