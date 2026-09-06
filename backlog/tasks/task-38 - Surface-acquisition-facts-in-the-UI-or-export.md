---
id: TASK-38
title: Surface acquisition facts in the UI or export
status: Done
assignee:
  - '@codex'
created_date: '2026-08-29 21:54'
updated_date: '2026-09-06 18:11'
labels:
  - data
  - ui
milestone: m-1
dependencies:
  - TASK-2
priority: medium
ordinal: 88000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The ownership columns acquired_at, license_type, and price_paid_cents are stored (migration 0014) and populated by M5's account-page importer, but no UI or export reads them. M6 export is the intended first consumer. These columns currently have no visible effect. Source: ROADMAP.md section 6.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 At least one consumer (export or UI) reads and displays acquired_at, license_type, and price_paid_cents
- [x] #2 The export format includes these columns when populated
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Preserve the existing acquisition date/licence UI and keep price out of the game modal. 2. Add a local acquisition CSV export in Library settings, one row per ownership with schema version, title, store, acquired_at, license_type, price_paid_cents and price_source. 3. Verify populated, missing and zero values, CSV quoting, cancel/failure and the export command using temporary SQLite and a fake destination. 4. Document this limited export without claiming the deferred full JSON/import milestone.
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

NOT CLOSED, and deliberately. Acceptance criterion 1 asks for a consumer that reads and displays acquired_at, license_type AND price_paid_cents. The details modal now reads and draws the first two, in the object column under ON DISK, with the licence in words for the four types the parser recognises and nothing at all for one it does not. It will never read the third: the approved design decision on this task puts price paid outside this modal under section 7's 'never be smug', because '$59.99 · never opened' is the sentence the product must not write, and a test now asserts that no member of either the acquisition view model or the details view model names a price. Criterion 2, the export format, is untouched by this pass. Both remaining halves - price paid, and the export columns - belong to the export and account-stats work, and neither was in scope here. The task stays open on those two.

Completed 2026-09-06: Settings > Library now exports versioned acquisition CSV, one row per ownership with title/store/date/licence/price/source, including blank unknowns and known zero prices. Price stays out of the modal. AcquisitionExportTests: 5 passed, covering populated facts, multiline/quoted titles, blank vs zero prices, command save/cancel/failure and unavailable service. Actual LibrarySettingsView pointer click in headless Skia harness with temp SQLite sent the 1299-cent acquisition to the destination and rendered completion status; screenshot C:/Temp/winnow-task105/export.png inspected without clipping. Win32 UIA also found the named export Button inside Acquisition export and Library settings Groups. Native OS save dialog uses the existing Avalonia storage-provider pattern; tests substitute the destination. Full JSON/import remains deferred and docs state that scope.

Final integration: solution build succeeded with zero warnings/errors. Main suite3469 passed, Covers84 passed, Recommend152 passed; final store-action/accessibility regression selection82 passed. Identity-read inventory explicitly classifies acquisition export as per-ownership facts that must not be folded by identity links.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added acquisition CSV export to Library settings, completing the consumer for date, licence and price while keeping price out of the game modal. Verified five regression tests, actual-view export command/rendering with temporary data, and Win32 UIA reachability. Documented the CSV schema and deferred full JSON/import.
<!-- SECTION:FINAL_SUMMARY:END -->
