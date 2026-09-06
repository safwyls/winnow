---
id: TASK-17
title: >-
  Fix cover pipeline: negative cache reopens, bounded responses, decoded
  dimensions
status: Done
assignee:
  - '@codex'
created_date: '2026-08-29 21:53'
updated_date: '2026-09-06 21:24'
labels:
  - covers
  - enrich
milestone: m-4
dependencies: []
priority: high
ordinal: 400
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Three cover-pipeline defects. The negative cache does not reopen when capability changes (e.g., a new cover source becomes available) (F26). Responses are not size-bounded, so a malicious or malformed upstream can exhaust memory (F28). Decoded image dimensions are not checked. F27 is closed (resolved by F14's per-view cover state). Sources: stabilization-2026-08-28.md Group 2, findings F26 and F28. Trigger: next cover loading change.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A negative-cache entry is re-evaluated when a new cover source becomes available
- [x] #2 Cover responses are size-bounded; an oversized response is rejected, not buffered
- [x] #3 Decoded image dimensions are validated before display
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Revalidate negative identities and refresh source capabilities; bound streamed downloads; validate dimensions before allocating pixels; add regression tests and run the isolated cover suite.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Verified all 94 cover tests with dotnet test tests/Winnow.Covers.Tests -p:BaseOutputPath=C:\Temp\winnow-covers-beta\ --no-restore. Ten new regression cases cover same-session IGDB configuration, memory/disk identity changes, declared and streaming oversize HTTP rejection for both CDNs, and dimension/pixel ceilings. Downloads stop at the declared header or one byte beyond 16 MiB; image headers are checked before pixel allocation (8192 per axis, 32 Mi pixels). Cached positive art is read before capability refresh.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Reopened negative cover results when source capability changes, bounded Steam/IGDB response streaming to 16 MiB, and rejected unsafe image dimensions before decode/display. All 94 cover tests passed, including ten new regression cases.
<!-- SECTION:FINAL_SUMMARY:END -->
