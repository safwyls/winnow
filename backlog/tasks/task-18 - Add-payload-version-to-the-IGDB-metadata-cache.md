---
id: TASK-18
title: Add payload version to the IGDB metadata cache
status: To Do
assignee: []
created_date: '2026-08-29 21:53'
labels:
  - enrich
  - data
dependencies: []
priority: medium
ordinal: 43000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The IGDB enrichment cache has no payload_version field. If a field is added to the cached shape, existing entries silently yield empty results for 30 days rather than triggering a refetch. A latent trap, not yet a bug. Finding F29. Sources: stabilization-2026-08-28.md Group 2; ROADMAP.md section 6. Trigger: next enrichment cache change.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Each cached IGDB entry carries a payload_version
- [ ] #2 A version mismatch triggers a refetch instead of returning stale data
- [ ] #3 A test demonstrates that bumping the version causes re-enrichment
<!-- AC:END -->
