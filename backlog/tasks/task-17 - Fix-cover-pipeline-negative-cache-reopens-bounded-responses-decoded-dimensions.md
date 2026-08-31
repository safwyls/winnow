---
id: TASK-17
title: >-
  Fix cover pipeline: negative cache reopens, bounded responses, decoded
  dimensions
status: To Do
assignee: []
created_date: '2026-08-29 21:53'
labels:
  - covers
  - enrich
dependencies: []
priority: medium
ordinal: 17000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Three cover-pipeline defects. The negative cache does not reopen when capability changes (e.g., a new cover source becomes available) (F26). Responses are not size-bounded, so a malicious or malformed upstream can exhaust memory (F28). Decoded image dimensions are not checked. F27 is closed (resolved by F14's per-view cover state). Sources: stabilization-2026-08-28.md Group 2, findings F26 and F28. Trigger: next cover loading change.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 A negative-cache entry is re-evaluated when a new cover source becomes available
- [ ] #2 Cover responses are size-bounded; an oversized response is rejected, not buffered
- [ ] #3 Decoded image dimensions are validated before display
<!-- AC:END -->
