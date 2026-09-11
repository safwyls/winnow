---
id: TASK-13
title: Add edition-specific release-year evidence for matching
status: Done
assignee:
  - '@codex'
created_date: '2026-08-29 21:52'
updated_date: '2026-09-11 18:47'
labels:
  - data
  - resolve
dependencies: []
documentation:
  - game-library-design.md
  - docs/facet-provenance.md
priority: medium
ordinal: 66000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Represent edition-specific release-year evidence for matching without losing existing Work metadata, user corrections or unknown-year semantics. Releases currently use the Work first-release year in matching. Define the relationship between an edition year and the displayed first-release year before changing persistence or consumers.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Define Work first-release-year and Release edition-year semantics, provenance and fallback behavior.
- [x] #2 Persist edition-year evidence and use it in edition-sensitive matching.
- [x] #3 Preserve existing years and their provenance; inherited Work dates are not labeled as verified edition dates.
- [x] #4 Preserve manual corrections, deliberately cleared years and IGDB pin behavior.
- [x] #5 Verify different editions and missing evidence, and document display/filter behavior on desktop and fullscreen.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Keep Work first-release year as the shared display/filter value and user correction authority. Add independent release-year observations with provider provenance; never backfill inherited Work values. 2. Project explicit original release dates from exact Steam listings through the existing library-wide reception pass, validating external-ID applicability at persistence/read boundaries. 3. Prefer applicable edition evidence in soft matching, retain Work fallback and user-owned null semantics, and retain evidence source in match snapshots. 4. Verify temporary SQLite persistence, edition/missing/conflicting identity cases, manual and IGDB pin regressions, and shared desktop/fullscreen read paths; update current docs and commit only TASK-13.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: ReleaseRepository, ReleaseIdentity and LibrarySoftMatchSweep read works.first_release_year. Release has no edition-year field. ManualEntryCorrectionTests, WorkIgdbPinTests and GameMetadataEditorViewModelTests cover existing corrections and unknown values that the change must retain.

Implemented migration 0039 and exact-ID release-year evidence, populated from positive numeric Steam release.original_release_date via the shared reception pass. Steam arrival dates never substitute. Evidence read/write checks the current external ID; conflicting applicable dates yield no edition year. Matching and expansion detection use edition evidence then Work fallback; user-owned Work values, including null, win. Work data, provenance and pins are untouched. Soft-match snapshots retain source. Desktop and fullscreen retain primary Work year display/filter behavior through GameTileViewModel; YearFilterParityTests now seed different edition years and verify both surfaces. Validation: clean App build; Verify-Migrations verified 39 hashes; 584 targeted matching, expansion, Steam, library/details, correction, pin and migration tests passed; 29 UI detail/year tests passed; final 14 edition-evidence tests and 14 updated desktop/fullscreen year parity tests passed. TASK-37 remains independent: a listing year does not populate or prove IgdbVersionId; broader automatic cross-store links still need validated edition identity acquisition.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added separate exact-listing edition-year evidence and conservative matching fallback while preserving Work display years, field provenance, user-cleared years and IGDB pins. Verified temporary SQLite persistence, matching snapshots, stale/conflicting IDs, cache parsing, and desktop/fullscreen year-filter parity. App build and migration hashes pass; targeted suites pass.
<!-- SECTION:FINAL_SUMMARY:END -->
