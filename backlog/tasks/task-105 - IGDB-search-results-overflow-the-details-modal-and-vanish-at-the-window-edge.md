---
id: TASK-105
title: IGDB search results overflow the details modal and vanish at the window edge
status: In Progress
assignee:
  - '@claude'
created_date: '2026-09-04 22:50'
updated_date: '2026-09-04 23:16'
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
- [ ] #2 The list is reachable by keyboard as well as by pointer, and focus stays visible while scrolling
- [ ] #3 A result set larger than the visible area is evidently scrollable rather than silently cut off
- [x] #4 The modal itself does not grow past the window with a long result set
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Root cause. The candidate ItemsControl has no height bound of its own, and the modal card's MaxHeight of 720 does not contain it: a Border does not clip, and the right column's rest band sits in an Auto row, so its ScrollViewer is measured against infinity and never scrolls. Content past the card is drawn outside it and clipped by the window.
2. Bind the list's height. The candidate list goes inside a ScrollViewer with a MaxHeight and VerticalScrollBarVisibility=Auto, the pattern the Stores modals already use (StoresView MaxHeight 440 / 380 / 460). It takes Classes=inner, so the modal's own scrollbar rule applies and the resize-border inset (section 9.1) stays opted out.
3. Make the cut visible. The bound is set so a row is cut part way rather than landing on a row boundary, and the scrollbar is the second cue. No count line, no fade: section 7 keeps an explanation to a short phrase and there is nothing here the two cues do not already say.
4. Keyboard. Each row's assign button stays a Tab stop. A GotFocus handler on the list calls BringIntoView so a row reached by Tab scrolls into view rather than being focused off screen; the ring is the existing brush swap on a constant-thickness border (10.7).
5. Contain the column the control lives in. TASK-102 moves the control into the left column, so that column gets a bounded scroll region under the cover as well, and the open disclosure can no longer push the card past its own MaxHeight.
6. Verify. dotnet build with a scratch BaseOutputPath, then dotnet test per project. Compiled bindings make the build a check that every binding in the new markup resolves. Appearance, the scroll bound and the overflow itself need a run; they are named for the user rather than checked.
7. design-system.md 10.9 gains the bounded list; all prose delegated to docs-writer.
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
<!-- SECTION:NOTES:END -->
