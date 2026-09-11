---
id: TASK-201
title: Preserve account identity through Steam account-page imports
status: Done
assignee:
  - '@enrichment-api'
created_date: '2026-09-11 04:59'
updated_date: '2026-09-11 07:30'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.Core/Ingest/SteamAccountPages.cs:50'
  - 'src/Winnow.App/Services/SteamAccountPageImportService.cs:226'
  - 'src/Winnow.App/Services/SteamAccountPageImportService.cs:472'
  - 'src/Winnow.App/Services/SteamAccountPageImportService.cs:559'
documentation:
  - docs/architecture-review-2026-09-10.md
priority: medium
type: bug
ordinal: 232000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R13. Evidence: Source verified. The Steam account-page capture contract carries HTML, time and route but no account identity. Imports resolve against a global Steam title index and store source=steam facts without account scope. Embedded sign-in knows the captured account but discards it at this boundary. Two accounts' purchases/licenses can mix or deduplicate identical transactions. Account statistics and acquisition evidence can be attributed or collapsed incorrectly. Saved files may have genuinely unknown identity and need an explicit representation rather than a guessed account.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Known account identity survives capture, parsing, resolution and persistence, including observation deduplication keys.
- [x] #2 Unknown saved-file/legacy identity is represented explicitly with a documented compatibility policy; no current account is guessed.
- [x] #3 Tests import the same game and identical transaction facts under two accounts and verify account statistics/acquisition provenance on both surfaces.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Carry captured Steam identity through Core page and parse contracts, validate per-page identity before/after embedded capture and retain unknown for saved or legacy files. 2. Add migration0035 account-scoped fact identity plus acquisition observations, preserving legacy unknown rows and immutable shipped migrations. 3. Resolve acquisition candidates only against membership of the captured account, persist evidence without flattening accounts, and project shared Details/export through one reader. 4. Expose honest account scope in shared statistics and add two-account identical-fact, unknown-file, delayed-capture, projection/export and migration regressions. 5. Coordinate LibraryViewModel seam and both surface tests with UI owner, update narrow build-spec sections and decisions, and run focused serialized tests.

UI verification: render account-specific acquisition facts and aggregate conflicts through the production shared LibraryViewModel reader, then render identified and unknown capture scope in AccountStatsView and FullscreenLibrarySummaryPage.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Captured SteamID64 now survives Core pages/parser and normalizes to account_ref in the importer. Embedded pages are checked before/after capture and across pages; changed/lost identity discards the page set. Migration0035 adds account-scoped fact uniqueness without reassigning legacy unknowns and separate acquisition observations. Known-account title matching requires that account membership under the sync gate/transaction. Unknown saved captures preserve the legacy aggregate fill but cannot populate a selected known account. Shared acquisition reader projects Details; UI owner integrated its LibraryViewModel seam. CSVschema2 writes separate account rows and blank unknown refs. Shared stats reports identified accounts and unknown groups, withholding money totals when they may overlap.232/232 focused account-page/import/stats/export/identity-inventory/sign-in checks passed.34 migration hashes verified against HEAD. AC3 pending explicit desktop/fullscreen rendering evidence from UI owner; source capture gate covered with deterministic identity changes, without live WebView sessions.

UI AC3 evidence: real AccountAcquisitionReader over temp SQLite projects selected account10002 date2024 and gift license into desktop/fullscreen Details Library tabs. Switching to all accounts refreshes the same details to earliest2020 and withholds the conflicting license. Real account facts for two identified accounts plus unknown identity render identical scope disclosure in AccountStatsView and FullscreenLibrarySummaryPage and withhold ambiguous spend totals. Combined surface suite passed17/17 in tests/Winnow.Ui.Tests/TestResults/ui-account-context.trx.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Preserved account identity through Steam capture/import and account-scoped fact deduplication; separate acquisition observations power shared Details and CSVschema2, while unknown identity remains explicit. Verified232/232 backend checks and17/17 shared surface checks including account-specific acquisition and honest statistics on both surfaces; migration0035 integrity verified.
<!-- SECTION:FINAL_SUMMARY:END -->
