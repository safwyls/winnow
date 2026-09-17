---
id: TASK-331
title: Add SteamGridDB hero cover and icon browsing through the plugin
status: To Do
assignee: []
created_date: '2026-09-17 16:11'
labels: []
dependencies:
  - TASK-329
documentation:
  - doc-1
type: feature
ordinal: 373000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The bundled plugin currently supplies automatic static hero candidates only. It should contribute community artwork to the common browser so users can choose an image rather than relying on resolution ranking.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 An enabled configured SteamGridDB plugin appears as a source for Hero, Cover and Icon, with paged candidates and creator/source links where supplied.
- [ ] #2 Known external identifiers resolve games reliably; any fallback artwork search requires explicit selection and does not alter library identity.
- [ ] #3 Existing credential setup, rate limits, bounded downloads, content filtering and cached fallback remain effective; missing credentials and failures have actionable browser states.
- [ ] #4 Selected images persist independently of subsequent provider refreshes or disablement; existing automatic hero enrichment remains compatible.
- [ ] #5 Standalone plugin contract tests and host integration tests cover all three slots, paging and failures; desktop and fullscreen browser behavior is verified and plugin documentation updated.
<!-- AC:END -->
