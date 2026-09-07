---
id: TASK-150
title: Design-time preview data so Rider's previewer renders populated views
status: Done
assignee:
  - '@bionic'
created_date: '2026-09-07 03:48'
updated_date: '2026-09-07 05:40'
labels:
  - rider
  - previewer
  - developer-experience
dependencies: []
ordinal: 177000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Views render empty in the Avalonia previewer because nothing ever assigns a DataContext at design time (no d:DataContext, no Design.IsDesignTime path). Add a PreviewData factory that builds representative view models from fabricated domain records (no IO), wire it into the views so the Rider/Avalonia previewer shows populated UI, and cover the design-time paths with a headless UI test so the mocks cannot silently drift from constructor changes.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Opening GameDetailsView in the previewer shows a populated details modal (title, stats, updates, actions) without touching a database or the network
- [x] #2 The library shell (MainWindow) previews with cover tiles, rail buckets and command bar populated
- [x] #3 A headless test in tests/Winnow.Ui.Tests instantiates each design-time view model and attaches it to its view, failing if the preview data stops composing
- [x] #4 Runtime behaviour is unchanged: design-time data never activates outside the previewer
- [x] #5 README or AGENTS-adjacent docs tell a human developer how to use the previewer against this project
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Research view-model constructors and view DataContext wiring to find the seams a design-time factory can use. 2. Add src/Winnow.App/Design/PreviewData.cs building representative view models (details modal, tiles, library shell) from fabricated domain records, IO-free. 3. Wire Design.IsDesignTime DataContext assignment into the views' code-behind (works in both Rider and VS previewers, and for views whose XAML is hard to annotate). 4. Add a headless test in tests/Winnow.Ui.Tests that attaches each PreviewData view model to its view. 5. Document the previewer workflow in README.md. 6. Build and run tests.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Design.IsDesignMode (not IsDesignTime) is the 11.3 API — the first draft used the old name and failed CI-locally. HasUnread is derived from stale_but_patched bucket membership, so the unread-badge fixture game had to be the stale one (Stardew), not a bounced one. Verified: 17 DesignTimePreviewTests pass, including a headless MainWindow opening on the shell VM and materializing tile views; full suites pass (Winnow.Tests 3644, Covers 94, Recommend 155, Ui 56); Release build 0 warnings; rendered frame captured at C:/Temp/winnow-capture/preview-game-details.png shows the populated details modal.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added src/Winnow.App/Design/ (PreviewLibrary dataset, PreviewRepositories/PreviewServices fakes, PreviewData view models) — a fabricated eight-game library over real domain records and the real bucket fold, no IO. Thirteen views assign their preview context under Design.IsDesignMode, so Rider's previewer renders the details modal, tile, feed, stores, appearance, merges, stats, library settings, filter panel, action bar and the whole shell populated. tests/Winnow.Ui.Tests/DesignTimePreviewTests.cs (17 tests) attaches the preview data to every surface and opens the shell headlessly, so preview breakage fails CI. README gained a 'Working on the UI' section covering the previewer, --data-dir/--seed-sample and capture conventions. Runtime behaviour unchanged: design branches are inert outside the previewer and all existing suites pass.
<!-- SECTION:FINAL_SUMMARY:END -->
