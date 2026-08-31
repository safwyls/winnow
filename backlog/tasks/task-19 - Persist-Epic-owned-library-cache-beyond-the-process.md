---
id: TASK-19
title: Persist Epic owned-library cache beyond the process
status: To Do
assignee: []
created_date: '2026-08-29 21:53'
labels:
  - ingest
  - data
dependencies: []
priority: medium
ordinal: 19000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The Epic owned-library cache lives only in memory and is lost when the process exits. This forces a full re-fetch on every launch when authenticated. Finding F31. Source: stabilization-2026-08-28.md Group 2. Trigger: next Epic ownership change.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Epic library cache persists to disk under the data directory
- [ ] #2 A restart reads the cache without re-fetching, until the configured staleness interval expires
<!-- AC:END -->
