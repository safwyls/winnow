---
id: TASK-28
title: Add parser size and depth limits on storefront-owned files
status: Done
assignee:
  - '@steam-ingest'
created_date: '2026-08-29 21:53'
updated_date: '2026-09-06 21:28'
labels:
  - ingest
  - security
milestone: m-4
dependencies: []
priority: high
ordinal: 500
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
VDF and other storefront-owned file parsers have no size or depth limits. A malformed file could cause excessive memory use or stack overflow. Finding F44. Source: stabilization-2026-08-28.md Group 3. Needs oversize and deep fixtures, sanitized as usual.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Parsers reject files exceeding a configured size limit
- [x] #2 Parsers reject nesting exceeding a configured depth limit
- [x] #3 Oversize and deep-nesting fixtures exist in `tests/fixtures/steam/` (sanitized)
- [x] #4 Tests demonstrate rejection for both cases
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Introduce configurable byte/depth limits and bounded reads for Steam VDF and Epic/GOG local JSON readers. Preflight VDF structural depth before ValveKeyValue parsing, ignoring quoted text/comments; explicitly prohibit includes. Apply JSON MaxDepth and bounded base64 catalog decoding. Add sanitized fixture-derived oversized/deep VDF plus boundary, escape/comment and storefront JSON tests; document limits and run focused ingest tests.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Final combined ingest verification after root-list review: 67 focused tests passed with scratch BaseOutputPath C:\Temp\winnow-ingest-beta\.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Bounded all local VDF and Epic/GOG JSON reads, including the Epic fingerprint and base64 catalog, with injectable StorefrontParserLimits (64 MiB/depth 64 by default; depth override capped at 256). VDF quote/comment-aware safety preflight runs before ValveKeyValue recursion and refuses includes; semantic parsing remains ValveKeyValue. JSON uses explicit MaxDepth. Added two sanitized capture-derived hostile Steam fixtures, plus byte-boundary, deep nesting, UTF-16, escaped quote/comment, include, catalog and JSON reader tests. All 65 focused ingest/parser tests passed. Documented scope and limits in the build spec.
<!-- SECTION:FINAL_SUMMARY:END -->
