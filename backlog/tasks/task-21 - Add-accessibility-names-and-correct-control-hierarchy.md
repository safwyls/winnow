---
id: TASK-21
title: Add accessibility names and correct control hierarchy
status: Done
assignee:
  - '@avalonia-ui'
created_date: '2026-08-29 21:53'
updated_date: '2026-09-06 18:10'
labels:
  - accessibility
  - ui
dependencies: []
priority: medium
ordinal: 74000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Views lack accessibility names and the control hierarchy does not correctly express the semantic structure to screen readers and automation. Finding F35. Source: stabilization-2026-08-28.md Group 2. Trigger: next view-authoring pass.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Every interactive control has an accessibility name
- [x] #2 The control hierarchy expresses the semantic structure (headings, groups, lists)
- [x] #3 A screen reader can navigate the primary views meaningfully
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Audit all primary views for unnamed interactive controls and missing semantic headings/groups. 2. Add explicit names where composed content loses the visible label, preserve native names for plain content, and expose semantic regions. 3. Exercise actual automation peers and a running throwaway-data app through Windows UI Automation; record exact screen-reader coverage and close only criteria proved.
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

NOT CLOSED, and deliberately. This pass delivered the details modal's half only. Inside the modal: Bands 1, 2 and 3 and the reception line are named groups (AutomationProperties.Name with AccessibilityView=Control, which is what un-prunes a panel whose own peer reports itself out of the control view); the title is a level-1 heading and every section heading is level 2 through HeadingLevel; update rows and screenshot thumbnails carry ControlTypeOverride=ListItem on the DataTemplate root rather than on the ItemsControl, whose ContentPresenter containers report themselves out of the control view; an update row's name states the unread fact in words; the close, launch and outbound-link buttons gained names; and the refetch status is a live region by bound Text. AutomationProperties.Name is never placed on a TextBlock, whose peer ignores it. All four attached properties were verified wired to Windows UIA in the Avalonia 11.3.20 source, and AutomationNameReachabilityTests (from TASK-129) proves each name sits on an element the control view keeps. What criterion 1 still wants is every interactive control in every OTHER view; criterion 2 wants the same hierarchy work on those views; and criterion 3 cannot be verified without NVDA or Narrator against a running app, which was out of bounds here. The task stays open on all three.

2026-09-06 completion: audited every authored interactive control in the App views. Added names to composite/icon buttons, fields and sliders; named primary screen groups; ranked screen/section headings; named native list items; propagated list/filter counts through names and live ItemStatus. The live UIA audit also found the focusable details root was unnamed despite named inner bands; it now exposes a game-specific name and Window role. Six focused tests passed, including all-XAML name coverage, peer reachability and count/rename notifications. docs/spikes/accessibility-navigation.ps1 passed twelve live Windows UIA navigation surfaces using only a unique throwaway --data-dir with --seed-sample --no-sync, and closed its process in finally. It checked all visible focusable controls for meaningful names, named regions/list items/dialog roles, and moved/read actual focus on a game row and the acquisition export button. Evidence is docs/spikes/accessibility-navigation.md. Criterion 3 is verified through the live Windows provider consumed by screen readers, not a Narrator/NVDA speech recording; announcement order and external sign-in/browser content were not certified.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Completed accessibility names and semantic hierarchy across primary views. Six focused tests and twelve live Windows UIA surface checks passed; verified named controls, groups, game list rows, dialog identity and actual focus navigation on isolated sample data. Speech output in Narrator/NVDA was not recorded.
<!-- SECTION:FINAL_SUMMARY:END -->
