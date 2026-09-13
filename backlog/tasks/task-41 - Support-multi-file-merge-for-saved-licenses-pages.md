---
id: TASK-41
title: Support multi-file merge for saved licenses pages
status: Done
assignee:
  - '@codex'
created_date: '2026-08-29 21:54'
updated_date: '2026-09-11 18:51'
labels:
  - ingest
dependencies: []
documentation:
  - game-library-design.md
priority: low
ordinal: 103000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Combine multiple saved Steam licence pages selected in one import. The file dialog already allows multiple selection, but the loader currently accepts only the first file of each page kind. Extend that contract while retaining per-file outcomes, account identity and honest pagination coverage.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Load and combine multiple saved licence pages while retaining per-file success and failure outcomes.
- [x] #2 Deduplicate overlapping pages and repeated imports without discarding unique licence observations.
- [x] #3 Preserve captured account provenance and do not silently combine distinct account identities.
- [x] #4 Report coverage and incomplete pagination honestly for partial, overlapping or failed inputs.
- [x] #5 Verify two different licence pages, duplicate input, an unreadable file and mixed page kinds.
- [x] #6 Verify the shared saved-page import flow from desktop and fullscreen.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Extend the saved-page capture with additional licence documents and conservative coverage metadata. Parse each page separately, deduplicate identical licence observations, retain per-file outcomes and reject conflicting saved account markers without assigning unknown files to current credentials. Keep purchase-history first-file behavior. Verify loader/import idempotency, mixed/failing inputs and both shared UI routes; document and commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: SteamAccountPageFilePicker already enables multiple selection. SteamAccountPageFileLoader rejects a second page of the same kind; SteamAccountPageFileLoaderTests pins that limitation. The remaining work is loader/contract aggregation, not a multi-select picker.

Combined separate saved licence documents with tuple-based observation deduplication and per-file outcomes. Contiguous advertised ranges establish coverage; gaps, conflicting totals, failed inputs and multiple unpaged files remain incomplete. Conflicting saved g_steamID markers are refused; files remain unknown-account evidence and current credentials never supply provenance. Marker-free saved files cannot establish account identity, so both surfaces instruct selecting files from one account. Verified 128 parser/loader/import/provenance/model tests and both real desktop/fullscreen import interaction cases, including repeated import with two persisted facts and no duplicates.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Import multiple saved licence pages through both presentations. Preserve unique observations and honest coverage/account limits. Verified 128 focused tests plus two headless end-to-end desktop/fullscreen import tests.
<!-- SECTION:FINAL_SUMMARY:END -->
