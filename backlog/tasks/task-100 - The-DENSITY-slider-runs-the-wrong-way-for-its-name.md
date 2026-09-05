---
id: TASK-100
title: The DENSITY slider runs the wrong way for its name
status: Done
assignee:
  - '@avalonia-ui'
created_date: '2026-09-04 18:14'
updated_date: '2026-09-04 18:56'
labels:
  - ui
dependencies: []
priority: medium
type: bug
ordinal: 127000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The slider labelled DENSITY in MainWindow.axaml binds Library.TileWidth over 108 to 200. Dragging it right makes the tiles larger, so fewer games fit — the opposite of what "more density" means. Either invert the control so right means denser, or rename the label to what the control does (tile size). One of the two, so the label and the behaviour agree.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The label and the direction of travel agree: whichever end the user drags towards produces what the label promises
- [x] #2 The persisted preference still round-trips, and an existing stored value maps onto the same visual result as before
- [x] #3 The tooltip agrees with the label
- [x] #4 Build succeeds and no test regresses
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Confirm what persists: TileWidth is session-only (no settings key), so the fix cannot break a stored value.
2. Keep the DENSITY label — it is the design system's own name for the control (design-system.md §4 layout sketch, §13 control list) — and invert the direction of travel instead.
3. Add LibraryViewModel.MinimumTileWidth/MaximumTileWidth constants and a Density property that mirrors TileWidth about the middle of that range, so the slider's own left-to-right value grows as tiles narrow. Preferred over Slider.IsDirectionReversed because that also flips which RepeatButton the filled-track style paints, and the keyboard mapping would have to be re-verified.
4. Bind the slider to Library.Density with Minimum/Maximum from the constants via x:Static so the mirror and the range cannot drift.
5. Delegate the new tooltip copy and the property's XML doc to docs-writer.
6. dotnet build + dotnet test.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Kept the DENSITY label and inverted the direction of travel, rather than renaming the label to 'Tile size'.

Why the label wins: DENSITY is the design system's own name for this control — design-system.md §4's command-bar sketch draws 'density ──○──', and §13 lists 'Search, layout, density, display, sort' as what the command bar operates. The label had a spec behind it; the direction did not.

How: LibraryViewModel keeps TileWidth as the source of truth and gains two public constants (MinimumTileWidth 108, MaximumTileWidth 200) plus a Density property that mirrors TileWidth about the midpoint of that range. The slider binds Library.Density with Minimum/Maximum pulled from the constants via {x:Static}, so the mirror and the range cannot drift.

Slider.IsDirectionReversed was the other route and was rejected: verified against api-docs.avaloniaui.net that it exists on Avalonia's Slider, but it reverses the Track, which swaps which RepeatButton is DecreaseButton — the density style paints DecreaseButton in TextDim and IncreaseButton in Line, so the filled 'travelled' side of the track would have ended up on the right-hand end.

On AC2: there is no persisted density preference to break. A grep of every ISettingsRepository key shows no density or tile-width setting; the slider sits left of the command bar's divider, which is where per-session controls live ('Rule: per-session controls left, persisted prefs right'). TileWidth's default 148 and its every consumer (CoverWall MinCellWidth, TileHeight, GameTileView's decode width) are untouched, so any value in force maps to exactly the tile size it did before — only the thumb's position on the track is mirrored. The mirror is an exact involution (Min + Max − x), so the value round-trips without drift.

New source guard: tests/Winnow.Tests/LibraryChromeTests.cs pins the binding to Library.Density and the two x:Static range attributes, so rebinding to TileWidth — a one-word change with no visible diff — fails the build.

Tooltip changed from 'Tile size' to 'How tightly the grid packs' (docs-writer).
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
The DENSITY slider now runs the way its label reads: dragging right narrows the tiles and fits more games. The label was kept because it is the design system's own name for the control (§4, §13) and the direction was the part with no spec behind it; LibraryViewModel.Density mirrors TileWidth about the midpoint of the 108..200 range and the slider binds that, with Minimum/Maximum bound to the view model's own constants via x:Static so the two cannot drift. Tooltip is now 'How tightly the grid packs'. No persisted preference exists to break — density has no settings key and TileWidth's default and every consumer are unchanged, so any value in force produces the same tile size as before. Verified: dotnet build clean (0 warnings, 0 errors) and dotnet test — Winnow.Tests 2922 passed, Winnow.Recommend.Tests 152 passed, Winnow.Covers.Tests 70 passed, with a new guard in LibraryChromeTests pinning the binding and the range.
<!-- SECTION:FINAL_SUMMARY:END -->
