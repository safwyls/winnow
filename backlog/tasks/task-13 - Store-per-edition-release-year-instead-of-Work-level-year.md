---
id: TASK-13
title: Add edition-specific release-year evidence for matching
status: To Do
assignee: []
created_date: '2026-08-29 21:52'
updated_date: '2026-09-11 14:01'
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
- [ ] #1 Define Work first-release-year and Release edition-year semantics, provenance and fallback behavior.
- [ ] #2 Persist edition-year evidence and use it in edition-sensitive matching.
- [ ] #3 Preserve existing years and their provenance; inherited Work dates are not labeled as verified edition dates.
- [ ] #4 Preserve manual corrections, deliberately cleared years and IGDB pin behavior.
- [ ] #5 Verify different editions and missing evidence, and document display/filter behavior on desktop and fullscreen.
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: ReleaseRepository, ReleaseIdentity and LibrarySoftMatchSweep read works.first_release_year. Release has no edition-year field. ManualEntryCorrectionTests, WorkIgdbPinTests and GameMetadataEditorViewModelTests cover existing corrections and unknown values that the change must retain.
<!-- SECTION:NOTES:END -->
