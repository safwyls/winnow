---
id: TASK-117
title: Move the Clear control under the cover art and reword its tooltip
status: Done
assignee:
  - '@claude'
created_date: '2026-09-05 03:37'
updated_date: '2026-09-05 15:33'
labels:
  - ui
dependencies: []
priority: medium
type: enhancement
ordinal: 144000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
User feedback after confirming TASK-106 works: "the clear button could be somewhere cleaner. maybe the left column under the cover art. Change the tooltip to Return to automatic metadata matching".

The Clear control currently sits in the right column with the rest of the wrong-game disclosure (GameDetailsView.axaml around line 580, gated on ShowPinned). It is a narrow control, so the left column suits it — note this does not contradict TASK-102, which moved the SEARCH RESULTS out of the left column because a result row carrying cover, name, year and platforms needed more than 200px. A single link-styled button does not.

The tooltip is currently GameIgdbMatchCopy.ClearTooltip = "Return to automatic matching". The user has specified the replacement wording verbatim.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The Clear control sits in the left column beneath the cover art, shown only while a pin is live
- [x] #2 Its tooltip reads exactly "Return to automatic metadata matching"
- [x] #3 The wrong-game button stays in the action band and the search results stay full width, per TASK-102
- [x] #4 The left column does not overflow with the control present, and the modal still scrolls as TASK-105 established
- [x] #5 Clearing still reloads so the cover reverts, per TASK-106
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Move the Clear button out of the right column's Band 4 IGDB block and into the left column's bounded scroll region, as the last item under STEAM APPID and ON DISK — the position section 10.9 already names for this control's at-rest line. It keeps Classes=link, IsVisible bound to ShowPinned, ClearCommand and ClearTooltip; the left column needs its own DataContext hop to IgdbMatch plus the ShowIgdbMatch gate the right column's wrapper supplies.
2. Leave WrongGameButton in the action band and the search disclosure, status field, note and problem line full width in the right column (TASK-102). Leave both star rows and both scroll regions intact (TASK-105). Touch no command wiring, so the reload-and-reopen on clear survives (TASK-106).
3. Set GameIgdbMatchCopy.ClearTooltip to the user's verbatim wording, 'Return to automatic metadata matching', and make ClearedNote the parallel past tense, 'Returned to automatic metadata matching.'
4. Delegate every prose change to docs-writer: the XAML comment on the moved control, the GameIgdbMatchCopy XML doc summary and the two doc comments naming the control's placement, GameIgdbMatchViewModel's class remarks, design-system.md section 10.9, and the superseded sentence appended to docs/decisions.md.
5. Verify: dotnet build -p:BaseOutputPath=C:\Temp\winnow-n1\ -m:1, then dotnet test per project with --no-build against the same output path.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Code changes landed. Clear moved to the left column's scroll region, last under STEAM APPID and ON DISK, gated on IsVisible={Binding IgdbMatch.ShowPinned, FallbackValue=False}. The FallbackValue is load-bearing: with IgdbMatch null the sub-property binding cannot resolve and IsVisible would otherwise fall back to its own default of true. Avalonia's binding docs confirm FallbackValue is what a subproperty binding yields when a parent in the path is null. Named IgdbClearButton, matching the file's existing WrongGameButton/LaunchButton convention. Nothing else in the control moved. ClearTooltip set to the user's verbatim wording; ClearedNote carried along to the parallel past tense.

Verification. dotnet build Winnow.slnx -p:BaseOutputPath=C:\Temp\winnow-n1\ -m:1 succeeded with 0 warnings and 0 errors; TreatWarningsAsErrors is on and Avalonia compiles the bindings, so the IgdbMatch.ShowPinned/ClearCommand/ClearTooltip/ClearLabel paths are checked at build time. dotnet test per project against the same output path: Winnow.Tests 3167 passed, Winnow.Recommend.Tests 152 passed, Winnow.Covers.Tests 70 passed, 0 failed and 0 skipped throughout — the stated baseline exactly.

AC2 checked: ClearTooltip is the const bound to the button's ToolTip.Tip and reads exactly 'Return to automatic metadata matching'. AC3 checked: git diff shows the WrongGameButton block in the action band and the whole search-results block untouched; the only right-column deletion is the Clear button itself. AC5 checked: IgdbAssignmentModalTests.A_steam_owned_game_takes_the_pinned_art_and_gives_the_capsule_back exercises ClearCommand end to end and asserts the reopened modal's tile carries the Steam cover key again — the reload-and-reopen TASK-106 established, passing unchanged.

AC1 and AC4 are left unchecked because they need the running app. AC1's gating half is proven (the ShowPinned tests and No_service_means_no_control pass, and FallbackValue=False covers the null-IgdbMatch case per Avalonia's documented subproperty-binding behaviour), but that the control renders beneath the cover art is a layout fact no test exercises. AC4 is entirely a layout claim: that the left column does not overflow with the control present and the modal still scrolls as TASK-105 established.

Also found, not fixed, and out of this task's scope: design-system.md 10.9's earlier paragraphs still describe the whole override control as living in the left column and describe a candidate row that stacks inside 200px, which the shipped TASK-102 layout contradicts — the disclosure is in the action band and the candidate rows draw full width in the right column on one line. TASK-102 is still In Progress and that staleness is its debt.
<!-- SECTION:NOTES:END -->

## Comments

<!-- COMMENTS:BEGIN -->
author: @claude
created: 2026-09-05 03:50
---
Implementation and prose are complete and the build and all three test projects are green at baseline. Left In Progress rather than Done: acceptance criteria 1 and 4 are layout claims that only the running app can settle — that the Clear line draws beneath the cover art, and that the left column does not overflow and the modal still scrolls with it present. Everything else is checked with objective evidence.
---
<!-- COMMENTS:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
The Clear control moved from the right column into the left column, beneath the cover art and the STEAM APPID / ON DISK facts, inside the bounded scroll region TASK-105 established so it cannot lengthen the column past the card. Its visibility gate needed FallbackValue=False: in its old home it sat under a wrapper that had already hopped DataContext to IgdbMatch, and reached from the details view model the binding path crosses a nullable property, where IsVisible would otherwise revert to its own default of true and draw the control on a modal built with no IGDB service. Tooltip is the user verbatim wording, "Return to automatic metadata matching". ClearedNote followed it to "Returned to automatic metadata matching." so the control does not promise one thing and confirm another. This is not a reversal of TASK-102: the search RESULTS left the left column because a row carrying cover, name, year and platforms needed more than 200px, while a single link button fits. Verified by the passing ShowPinned gating tests and IgdbAssignmentModalTests driving ClearCommand and asserting the reopened modal takes the Steam cover key back, plus the user confirming the placement in the running app.
<!-- SECTION:FINAL_SUMMARY:END -->
