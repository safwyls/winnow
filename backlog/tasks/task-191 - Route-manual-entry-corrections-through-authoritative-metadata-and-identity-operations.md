---
id: TASK-191
title: >-
  Route manual entry corrections through authoritative metadata and identity
  operations
status: To Do
assignee: []
created_date: '2026-09-11 04:58'
updated_date: '2026-09-11 05:06'
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
- [ ] #1 Manual creation and edits use authoritative metadata/identity contracts with correct user provenance and consistent work, pin and external-ID state.
- [ ] #2 A documented correction policy can retract or replace erroneous user-entered IDs without deleting genuine independent storefront observations.
- [ ] #3 Regressions cover edits after pinning/enrichment and correcting IDs after storefront attachment, including both desktop and fullscreen entry points.
<!-- AC:END -->
