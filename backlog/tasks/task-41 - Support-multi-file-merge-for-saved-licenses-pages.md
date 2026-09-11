---
id: TASK-41
title: Support multi-file merge for saved licenses pages
status: To Do
assignee: []
created_date: '2026-08-29 21:54'
updated_date: '2026-09-11 13:59'
labels:
  - ingest
dependencies: []
documentation:
  - game-library-design.md
priority: low
ordinal: 91000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Combine multiple saved Steam licence pages selected in one import. The file dialog already allows multiple selection, but the loader currently accepts only the first file of each page kind. Extend that contract while retaining per-file outcomes, account identity and honest pagination coverage.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Load and combine multiple saved licence pages while retaining per-file success and failure outcomes.
- [ ] #2 Deduplicate overlapping pages and repeated imports without discarding unique licence observations.
- [ ] #3 Preserve captured account provenance and do not silently combine distinct account identities.
- [ ] #4 Report coverage and incomplete pagination honestly for partial, overlapping or failed inputs.
- [ ] #5 Verify two different licence pages, duplicate input, an unreadable file and mixed page kinds.
- [ ] #6 Verify the shared saved-page import flow from desktop and fullscreen.
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: SteamAccountPageFilePicker already enables multiple selection. SteamAccountPageFileLoader rejects a second page of the same kind; SteamAccountPageFileLoaderTests pins that limitation. The remaining work is loader/contract aggregation, not a multi-select picker.
<!-- SECTION:NOTES:END -->
