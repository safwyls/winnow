---
id: TASK-28
title: Add parser size and depth limits on storefront-owned files
status: To Do
assignee: []
created_date: '2026-08-29 21:53'
labels:
  - ingest
  - security
milestone: m-4
dependencies: []
priority: medium
ordinal: 48000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
VDF and other storefront-owned file parsers have no size or depth limits. A malformed file could cause excessive memory use or stack overflow. Finding F44. Source: stabilization-2026-08-28.md Group 3. Needs oversize and deep fixtures, sanitized as usual.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Parsers reject files exceeding a configured size limit
- [ ] #2 Parsers reject nesting exceeding a configured depth limit
- [ ] #3 Oversize and deep-nesting fixtures exist in `tests/fixtures/steam/` (sanitized)
- [ ] #4 Tests demonstrate rejection for both cases
<!-- AC:END -->
