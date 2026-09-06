---
id: TASK-118
title: 'IGDB result rows: the platforms line runs under the Use this button'
status: Done
assignee:
  - '@claude'
created_date: '2026-09-05 03:47'
updated_date: '2026-09-05 04:13'
labels:
  - ui
dependencies: []
priority: high
type: bug
ordinal: 145000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Reported with a screenshot. In the wrong-game candidate list, a row with many platforms renders its subtext past the available width — "Xbox Series X|S, PlayStation 4, PC (Microsoft Windows), PlayStation 5, Xbox One, Nintendo Swit" continues underneath the "Use this" button and is cut off at the scrollbar rather than trimming inside its own column.

The row moved to full width in TASK-102 and went back to a single line there. The subtext is evidently not bounded by the column holding the button, so it overlaps rather than ellipsizing. Note TASK-70.8 fixed a comparable overlap on the Same Game card; check whether the same cause and the same remedy apply rather than treating this as new ground.

Screenshot state: details modal for Fortnite, four candidates, the second and third rows both overflowing.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The subtext trims within its own column and never renders under or past the Use this button
- [x] #2 The trim is visible as a trim — an ellipsis, not a hard cut at the scrollbar
- [x] #3 The full value remains available, by tooltip or equivalent, since a trimmed platform list is how a user tells two editions apart
- [x] #4 Rows with short and long subtext keep the same height and alignment
- [x] #5 A test pins the row layout so the columns cannot silently overlap again
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. CAUSE, and it is not TASK-70.8's. The detail line under a candidate's name is a horizontal StackPanel holding YearText and PlatformsText. A horizontal StackPanel measures its children with infinite width on the stacking axis, so TextTrimming=CharacterEllipsis on the platforms TextBlock never engages, and it arranges each child at its own desired width, so the panel overruns its own bounds. Nothing clips, so a long platform list draws straight over the Auto column holding Use this and out to the ScrollViewer edge. TASK-70.8's feed overlap was a missing Grid.Column attached property defaulting to column 0; the remedy there was reparenting into a WrapPanel. Same family (an unbounded child in a shared row), different mechanism, different remedy.
2. REMEDY. Replace the horizontal StackPanel with Grid ColumnDefinitions=Auto,* -- YearText in the Auto column, PlatformsText in the star column. A Grid star column measures its child at the allocated width, so TextTrimming engages and the ellipsis lands inside column 1 of the row. No ColumnSpacing: the separator already lives in YearText, so the rendered spacing is unchanged.
3. BOTH SITES. The same markup is in GameDetailsView.axaml (details modal, the reported defect) and LibrarySettingsView.axaml (the hand-added game form, latent behind a fixed 480px region). Fix both.
4. THE FULL VALUE STAYS REACHABLE. IgdbCandidateViewModel gains PlatformsTooltip -- PlatformsText when there are platforms, null when there are none so no empty tooltip opens -- bound to ToolTip.Tip on the platforms TextBlock at both sites.
5. ROW HEIGHT IS UNCHANGED AND CONSTANT. The row's height is set by the 34x51 cover plus the Border padding, not by the text: body 12 + 4 spacing + body 11 is well under 51. The Grid keeps both TextBlocks on one line with VerticalAlignment=Center, so a one-word and a nine-platform subtext measure the same.
6. TEST. New tests/Winnow.Tests/IgdbCandidateRowLayoutTests.cs, source guards in the StoreChipLayoutTests idiom (this repo has no headless Avalonia renderer). Over both views: every child of the row grid declares Grid.Column; the text column is the star column and the assign button is in the trailing Auto column; the detail line is not a horizontal StackPanel; the platforms TextBlock is in a star column and declares TextTrimming and ToolTip.Tip.
7. PROSE. Every XAML comment and XML doc comment authored by docs-writer, including the design-system.md 10.9 correction TASK-117 reported and its docs/decisions.md entry.
8. VERIFY. dotnet build -p:BaseOutputPath=C:\Temp\winnow-p1\ -m:1, then dotnet test per project --no-build against the same path. No app run.
<!-- SECTION:PLAN:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
The detail line holding the year and platforms was a horizontal StackPanel. A horizontal StackPanel measures its children with infinite width on the stacking axis, so TextTrimming never engaged, and it arranges each child at its full desired width, so the platforms ran under the Use this button and were cut at the scrollbar. Not the same cause as TASK-70.8, which was a missing Grid.Column attached property defaulting to column 0 — same family, different mechanism. Replaced with Grid ColumnDefinitions="Auto,*" at both sites: GameDetailsView.axaml, and the same latent markup in LibrarySettingsView.axaml, where the add-by-executable form would have overflowed identically the first time a result carried a long platform list. The full list is kept on a tooltip, because a trimmed platform list is how a user tells one edition from another. Verified by IgdbCandidateRowLayoutTests, parameterised over both views: every child declares its column, the text takes the star column and the button the last, the detail line is a Grid and not a horizontal stack, and the platforms trim inside the star column while keeping their full value. Full suite 3191/152/70, clean build.
<!-- SECTION:FINAL_SUMMARY:END -->
