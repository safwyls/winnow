---
id: TASK-191
title: >-
  Route manual entry corrections through authoritative metadata and identity
  operations
status: Done
assignee:
  - '@data-layer'
created_date: '2026-09-11 04:58'
updated_date: '2026-09-11 06:42'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.Data/Repositories/ManualEntryRepository.cs:185'
  - 'src/Winnow.Data/Repositories/ManualEntryRepository.cs:286'
  - 'src/Winnow.App/ViewModels/LibrarySettingsViewModel.cs:817'
documentation:
  - docs/architecture-review-2026-09-10.md
priority: high
type: bug
ordinal: 222000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R03. Evidence: Reproduced. ManualEntryRepository.UpdateAsync directly changes works metadata/igdb_id and appends external IDs without coordinating live pins or field sources. Editing a manual entry from IGDB333/Steam123 to IGDB444/Steam456 after pinning333 leaves live pin333, works444, both pairs of hard external IDs, and an IGDB source stamp on the user-entered year. Correcting an ID preserves the mistaken ID as an automatic hard-join assertion. Later storefront discovery can attach the wrong game, while provenance misrepresents ownership of edited fields.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Manual creation and edits use authoritative metadata/identity contracts with correct user provenance and consistent work, pin and external-ID state.
- [x] #2 A documented correction policy can retract or replace erroneous user-entered IDs without deleting genuine independent storefront observations.
- [x] #3 Regressions cover edits after pinning/enrichment and correcting IDs after storefront attachment, including both desktop and fullscreen entry points.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add migration 0033 for explicit manual identifier assertion history and IGDB mapping revisions; preserve untracked legacy identifiers without speculative backfill. 2. Route manual metadata through shared field ownership writes and mapping changes through a lease-scoped mapping/pin transition; validate conflicts across work mappings, live pins and external identifiers. 3. Correct only tracked manual identifiers whose removal cannot strand independent storefront observations; refuse ambiguous corrections with actionable errors while permitting ordinary metadata edits. 4. Populate and validate the shared manual form from authoritative entry state on both surfaces. 5. Add correction, pin, attachment, legacy, rollback and presentation regressions, update governing docs/manifest, and verify through the shared build helper.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented migration0033 manual assertion history and mapping revision with no legacy provenance backfill. Tracked corrections retract old hard IDs, maintain the live pin, and stamp edited metadata as user-owned atomically. Attached storefront observations or unknown/reused mapping origins refuse correction without blocking unchanged-ID title/year edits. Both forms reopen from authoritative current state and preserve drafts on conflict. Final shared Release verification:217 focused tests passed, including22 new repository correction/history/stale-intent/rollback cases and6 form bounds cases;26 headless UI tests passed, including8 new desktop-keyboard/fullscreen-controller correction and refusal cases. Verify-Migrations passed33hashes; scoped diff whitespace check passed. Root owns solution integration and milestone commit. Downstream IGDB projection fencing/invalidation remains the separately assigned TASK-196.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Manual corrections now keep metadata provenance, work mapping, live pin and retractable identifier history consistent. Legacy or independently observed IDs receive actionable refusals, and stale forms cannot overwrite later matches. Verified by 217 focused tests,26 headless tests and33 migration hashes; both desktop and fullscreen entry points exercised.
<!-- SECTION:FINAL_SUMMARY:END -->
