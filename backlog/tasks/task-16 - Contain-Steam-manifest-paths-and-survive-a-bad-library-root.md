---
id: TASK-16
title: Contain Steam manifest paths and survive a bad library root
status: To Do
assignee: []
created_date: '2026-08-29 21:53'
labels:
  - ingest
dependencies: []
priority: medium
ordinal: 16000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Steam manifest path resolution does not contain paths to the library root, and a bad library root can abort startup entirely instead of degrading gracefully. Finding F25. Source: stabilization-2026-08-28.md Group 2. Trigger: next Steam ingest change.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Manifest paths are validated against and contained within the library root
- [ ] #2 A malformed or inaccessible library root logs a warning and skips that root, not abort startup
- [ ] #3 A test with a nonexistent library root demonstrates graceful degradation
<!-- AC:END -->
