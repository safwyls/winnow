---
id: TASK-94
title: List view rows lost their per-game cover icon
status: Done
assignee:
  - '@avalonia-ui'
created_date: '2026-09-04 18:14'
updated_date: '2026-09-04 18:57'
labels:
  - ui
dependencies: []
priority: medium
type: bug
ordinal: 121000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The list view renders rows of text with no art. A library is recognised by its covers, and dropping them makes the list view a spreadsheet of titles. Each row should carry a small cover thumbnail on the left, using the same cover cache and dormancy dimming the grid tiles use.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Every list-view row shows a small cover on its left edge
- [x] #2 The thumbnail uses the existing cover cache and the same dormancy ramp as the grid
- [x] #3 A row with no cover shows the same placeholder treatment the grid uses, not a blank gap
- [x] #4 Row height and alignment stay stable across rows with and without art
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add src/Winnow.App/Views/RowCoverView.axaml(.cs): a 24x36 (2:3) cover for one list row, holding its own CoverPresenter and retargeting it on data-context change, the same way GameTileView and FeedCardView do — the wall must not blank a row's art and vice versa.
2. Layers are the grid's: TileGround, the placeholder gradient pair (FloorBrush / VividBrush) under Cover.ShowPlaceholder, then the Floor and Vivid images. Opacity carries DormancyAlpha (the resting ramp) rather than DisplayAlpha, because the list's hover affordance is the row veil, not a per-cover wake.
3. Request art at 24 DIP x render scaling; CoverImaging.SnapWidth puts that in the same 160px bucket the 148 DIP tiles use, so rows share cache entries with the grid.
4. Widen both list grids (header and row) from 2,18,*,136,104,92,24 to 2,32,18,*,136,104,92,24 and shift the header columns; the art column is fixed-width and the row stays 44px, so height and alignment are identical with and without art.
5. design-system.md §6 says the list view is 'Same data, no art dependency'; that becomes untrue, so docs-writer rewrites it and appends the superseded sentence to docs/decisions.md (AGENTS.md). Same for the two XAML comments in MainWindow.axaml that quote it.
6. dotnet build + dotnet test.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
New view: src/Winnow.App/Views/RowCoverView.axaml and .axaml.cs — a 24x36 (2:3) cover for one list row. It owns its own CoverPresenter and retargets it on data-context change and on attach, releasing on detach, exactly as GameTileView and FeedCardView do; the ListBox recycles containers, and a shared presenter would let the wall blank a row's art or the reverse.

Same cache: art is requested at 24 DIP x render scaling. CoverImaging.SnapWidth puts that in the 160px width bucket — the same one a 148 DIP tile uses at 1x — so rows share cache entries with the grid instead of adding a second decode size.

Same ramp: the layers are the grid's — opaque TileGround, then the floor variant under the vivid one whose opacity carries the ramp. Opacity binds DormancyAlpha (the resting ramp) rather than DisplayAlpha (the ramp with the tile's hover restore folded in): the list's hover affordance is the row's ChromeRaisedHalf veil, and a cover that also woke under the pointer would be a second hover language on one row. Dimming-preference changes reach it through GameTileViewModel.RefreshDormancy, which already raises DormancyAlpha.

Placeholder: a coverless row gets the grid's deterministic-hue gradient pair, floor under vivid — not a gap. The one omission is the placeholder's baked Bricolage title: at 24px wide no title is legible, and the row already sets the title beside the art in Display type.

Geometry: both list grids (the column-header strip and the row template) went from 2,18,*,136,104,92,24 to 2,32,18,*,136,104,92,24 and every Grid.Column after the new one shifted by one. The art column is a fixed 32px (24px cover plus 4px either side) and the row stays 44px, so a row with art and a row without are identical in height and in where every later column starts. Radius is RadiusControl (4px), not RadiusTile (6px), on §4's rule that the three radii rank by the size of the object they round; the 1px LineSoft border is the one every other cover surface carries.

design-system.md §6 said the list view was 'Same data, no art dependency: title, store, playtime, idle, unread dot.' That is now false; docs-writer rewrote the paragraph and appended the superseded sentence verbatim to docs/decisions.md per AGENTS.md, along with the two judgements the change rests on. The three MainWindow.axaml comments that quoted or assumed the old claim were rewritten too.

Verification: no headless Avalonia renderer exists in this project, so the behaviour is held by source guards over the markup, which is the established pattern here (StoreChipLayoutTests). tests/Winnow.Tests/LibraryChromeTests.cs asserts the row carries a RowCoverView and that its column parses as a fixed number rather than * or Auto — the exact failure mode AC4 names — and that the header grid and the row grid declare identical columns. StoreChipLayoutTests was updated for the new column shape (prefix and store-column index) and still passes, which proves the 136px store column survived the shift.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Every list row now carries a 24x36 cover on its left edge, drawn by the new RowCoverView from the same cover cache, the same lease mechanism and the same two-layer dormancy ramp as the wall tiles; a coverless row gets the grid's placeholder gradient rather than a gap. The cover takes the resting ramp rather than the tile's hover-restored one, because the list's hover affordance is the row veil. Both list grids gained a fixed 32px art column and every later Grid.Column shifted, so row height (44px) and column alignment are identical with and without art. design-system.md §6's 'no art dependency' clause was rewritten and the superseded sentence appended to docs/decisions.md. Verified: dotnet build clean (0 warnings, 0 errors) and dotnet test — 2922 passed in Winnow.Tests, 152 in Winnow.Recommend.Tests, 70 in Winnow.Covers.Tests — with new source guards in LibraryChromeTests pinning the fixed art column and the header/row column agreement, and StoreChipLayoutTests updated and passing on the new column shape.
<!-- SECTION:FINAL_SUMMARY:END -->
