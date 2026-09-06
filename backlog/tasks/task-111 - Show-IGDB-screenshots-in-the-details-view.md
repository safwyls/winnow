---
id: TASK-111
title: Show IGDB screenshots in the details view
status: Done
assignee:
  - '@avalonia-ui'
created_date: '2026-09-05 02:50'
updated_date: '2026-09-06 01:57'
labels:
  - ui
  - enrichment
dependencies: []
priority: medium
type: feature
ordinal: 138000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The details modal carries no imagery beyond the cover. IGDB supplies screenshots per game; pull them and show them, so the modal says what the game looks like rather than only what its box looks like. Reuse the existing cover cache and disk-cache discipline rather than adding a second image path, and respect the same soft-failing, rate-limited rules the other IGDB calls follow.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A game with IGDB screenshots shows them in the details modal
- [x] #2 Images use the existing cover cache and disk cache, not a second image path
- [x] #3 A game with no screenshots shows nothing rather than an empty frame
- [x] #4 Fetching is rate-limited, cached and soft-failing like the other IGDB clients
- [x] #5 The modal does not grow past the window, per the bounded-scroll rule set in TASK-105
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
UI HALF of the combined details-modal pass (TASK-111/112/113/115/38/21/30 land as ONE edit to GameDetailsView.axaml + GameDetailsViewModel, per the approved design at mock-details.html).
1. Wire the seams that already exist but are unregistered: IWorkImageRepository, IWorkRatingRepository, WorkReceptionWriter, ReceptionSyncService (called beside the maturity syncs) and GameRefetchService in Program.cs. Nothing new is fetched here.
2. Band 1 gains a reception line under year/publisher: GameReceptionViewModel over work_ratings, three attributed figures (igdb_users, igdb_critics, steam) each with its count. Steam carries its own label + percentage + count on hover. No row, no line.
3. Band 2 replaces the gap rail in place with a release-to-today PlayAxis (new custom Control beside GapRail). Two zones: a flat band for the pre-coverage amount (shape unknown, never a slope) and one bar per measured month. Measured months are the month-end points only (snapshots stamped at SteamPlaytimeHistory.MonthEnd), which are the backfilled series; live snapshots are never differenced. Sessions are not mixed in. Marks are the unread updates, placed on the whole axis. No release year or fewer than two month-end points falls back to the shipped gap rail; no last-played date keeps the sentence-only branch.
4. Band 3 gains a Refetch metadata row in the More menu (order: Open folder, Refetch metadata, Wrong game?, Edit details, Hide) with its status on a bound-Text TextBlock in Band 3 OUTSIDE the scroll region, LiveSetting=Polite.
5. Band 4 reorders to corrections, updates, ABOUT+screenshots, ALSO COVERS, EXTENDS, EXPANSIONS, LISTS. The update list is renamed so it stops colliding with Band 2 SINCE YOU PLAYED. Screenshots ride inside ABOUT as a thumbnail strip that expands one shot to a hero, inline, no popup, on CoverKey.IgdbScreenshot through the existing cover cache.
6. Left column gains ACQUIRED (acquired_at + license_type) under ON DISK. price_paid_cents is never read into this modal.
7. TASK-21/30 modal half: HeadingLevel on the title and the section headings, named Groups via AccessibilityView=Control on the band containers, ControlTypeOverride=ListItem on update and screenshot template roots with the unread count spelled into the name, names on the close/launch/link buttons. All four attached properties verified wired to Windows UIA in the Avalonia 11.3.20 source; AutomationProperties.Name is never placed on a TextBlock.
8. Re-measure the reception line and the strip at the 420px column in a headless Skia harness at 11.3.20, per docs/spikes/details-action-band-width.md.
9. All prose delegated to docs-writer. design-system.md 10.1/10.2/10.3/10.5 rewritten; every superseded sentence appended to docs/decisions.md.
10. Verify: dotnet build Winnow.slnx -p:BaseOutputPath=C:\Temp\winnow-modal\ -m:1, then dotnet test per project --no-build.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
DESIGN DECISIONS, 2026-09-05, from the combined details-modal design pass (mock at mock-details.html). Approved by the user:
- Band 4 order becomes: corrections, updates, ABOUT+screenshots, ALSO COVERS, EXTENDS, EXPANSIONS, LISTS. This reverses the recorded reason at GameDetailsView.axaml:899 that ALSO COVERS leads because it is a fact about identity; the superseded sentence goes to docs/decisions.md.
- Ratings become a reception line in Band 1 under year and publisher, NOT a new section. Three figures, each attributed with its count: IGDB users, IGDB aggregated critics, Steam.
- Steam shows its own label ("Very Positive") with the percentage and count on hover.
- Acquisition facts: acquired_at and license_type in the left column under ON DISK. Price paid NEVER appears in this modal — section 7 never be smug; "$59.99 / never opened" is the sentence the product must not write. Price goes to export and account stats.
- Screenshots go inside ABOUT as a thumbnail strip that expands one shot to a hero above it, inline in the modal tree, no popup.
- Refetch is a More menu row with its status on a Band 3 TextBlock outside the scroll region.
- The update list is renamed so it stops colliding with the Band 2 rail; the rail keeps SINCE YOU PLAYED. Mock placeholder is "What landed" and a better name is welcome.
- TASK-115 ships BOTH halves in one pass: the release-to-today axis AND the backfilled monthly bars.
- The rule that governs future additions: a label section heading in Band 4 is earned by a list of rows the user can act on, ABOUT being the single prose exception. A fact about the game goes in Band 1; a fact about this copy goes in the left column; a picture goes inside ABOUT; an act goes in the More menu with its status on the strip.

UI HALF LANDED as one edit to GameDetailsView.axaml and GameDetailsViewModel, plus the view models
and the one custom control the new bands needed.

WIRING. The enrichment half shipped in fd53bc9 but nothing was registered. Program.cs now registers
IWorkImageRepository/IWorkRatingRepository, WorkReceptionWriter, ReceptionSyncService and
GameRefetchService (behind a new IGameRefetch seam, the arrangement IIgdbAssignmentService already
has), and calls ReceptionSyncService.SyncAsync in the startup pass beside the two maturity syncs.
LibraryViewModel gained three optional seams and reads the ratings, the images and the ownership
rows when the modal opens.

BAND 1. GameReceptionViewModel over work_ratings: three attributed figures in a fixed order (IGDB
users, IGDB critics, Steam), each with its count on the line. Steam's own label is off the line and
on hover. A source with no figure writes no row, so absence is a property of the data; no figure at
all means no line. Drawn in a WrapPanel because the three figures measure 564px and the right column
is 420px at the card's MinWidth - measured, not estimated.

BAND 2. views/PlayAxis.cs, a custom-drawn control beside GapRail, over PlayAxisSeries. Both halves
shipped in this pass. Only month-end snapshots are differenced: those are the backfilled Steam
Replay readings and the only genuinely per-month data in the table. The floor point becomes a flat
band with a dashed boundary - an amount with no shape - and never a slope. Each bar spans the true
time between two readings, so an uncovered stretch draws wide rather than being compressed into an
ordinal sequence. Sessions are not an input. No release year or fewer than two month-end readings
falls back to the shipped gap rail; no last-played date keeps the sentence-only branch.

BAND 3. Refetch metadata is a More-menu row (Open folder, Refetch metadata, Wrong game?, Edit
details, Hide) with its status on a TextBlock in Band 3 outside the rest band's scroll region. The
field is a live region by bound Text with LiveSetting=Polite and no AutomationProperties.Name,
because a Name change raises no UIA event while TextBlockAutomationPeer raises one on a Text change.
An outcome that wrote something reopens the modal carrying its confirmation.

BAND 4. Order is now corrections, UPDATES, ABOUT with the screenshots inside it, ALSO COVERS,
EXTENDS, EXPANSIONS, LISTS. Screenshots are a horizontal thumbnail strip that expands one shot to a
hero above it, inline, no popup, on CoverKey.IgdbScreenshot through the existing cover cache. The
strip is the modal's fourth bounded scroll region and takes InnerScrollGutterBottom, a new token.

LEFT COLUMN. ACQUIRED under ON DISK: the date and the licence in words. price_paid_cents is never
read, and a test asserts no member of either the acquisition view model or the details view model
names a price.

ACCESSIBILITY. Named groups on Bands 1-3 and the reception line via AccessibilityView=Control;
HeadingLevel 1 on the title and 2 on every section heading; ControlTypeOverride=ListItem on the
update-row and thumbnail template roots, never on the ItemsControl; the unread fact spelled into an
update row's name. All four attached properties were verified wired to Windows UIA in the Avalonia
11.3.20 source.

MEASURED, not estimated. docs/spikes/details-modal-additions-width.md records a headless Skia harness
at the app's own 11.3.20, hosting every control in a real window so Fluent's templates apply. It
reproduced TASK-123's shipped strip figures exactly (347/357px), which is what makes the new numbers
trustworthy. Reception figures 184/210/170px, summing to 564; two rows at 420 and 500, one at 580.
Six thumbnails 760px, three whole ones visible at 420. The ACQUIRED block 180x49 in the object
column's 180px of content.

CONTRAST. No new ink. The line takes Text for the value and TextDim for the attribution and the
count, both already walked over all 256 greys at every slider position. Measured with the repo's own
Colorimetry: Text 13.11/16.44/14.76/13.42 flat and 10.34/13.75/11.90/10.61 over the brightest cover;
TextDim 5.88/6.82/6.44/6.10 and 4.63/5.71/5.19/4.83. The mock sets the source attribution in
TextFaint, which measures 3.63/3.60/3.31/3.28 flat and 2.86/3.01/2.67/2.60 over art - under AA
everywhere - so it was refused and a test now pins the refusal.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
IGDB screenshots ride inside ABOUT as a horizontal thumbnail strip that expands one shot to a hero above it, inline in the modal's own tree with no popup. The images take the existing cover cache under CoverKey.IgdbScreenshot, which resolves to t_screenshot_huge - an IGDB cover is 3:4 and a screenshot is 16:9, so the provider picks the size token and there is no second image path. A game with no screenshot ids in work_images produces no view model, so 'nothing rather than an empty frame' is a property of the data. The strip is the modal's fourth bounded scroll region and takes InnerScrollGutterBottom, a new token that is InnerScrollGutter turned through ninety degrees. Verified by GameScreenshotsViewModelTests (no rows, artwork rows are not screenshots, publisher order preserved, the screenshot provider is used, nothing expanded until a shot is picked), by DetailsModalStructureTests (band order, one popup, every bounded region clears its own bar), and by WorkReceptionTests for the enrichment half's rate-limited, cached, soft-failing storage. Measured in a headless Skia harness at the app's own 11.3.20: six thumbnails are 760px, three whole ones and part of a fourth visible in the 420px column. Build succeeded, 0 warnings, 0 errors; 3373 / 152 / 82 passing. What a running app would add: seeing real IGDB screenshots decode and draw.
<!-- SECTION:FINAL_SUMMARY:END -->
