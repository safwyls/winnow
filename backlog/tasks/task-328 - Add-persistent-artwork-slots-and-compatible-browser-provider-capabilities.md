---
id: TASK-328
title: Add persistent artwork slots and compatible browser provider capabilities
status: To Do
assignee: []
created_date: '2026-09-17 16:10'
labels: []
dependencies: []
documentation:
  - doc-1
type: feature
ordinal: 370000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Artwork selection currently supports user cover/background imports and an unpaged plugin artwork list. A shared browser needs persistent hero, cover and icon choices with provenance, offline retention and compatible provider capabilities. Product proposal: doc-1.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Hero, Cover and Icon choices retain asset provenance and downloaded images through restart, refresh, offline use, credential removal and provider disablement.
- [ ] #2 Manual choices take priority over collection assignments and automatic sources; resetting one slot restores its automatic behavior without changing other slots.
- [ ] #3 Providers declare supported slots and expose browsable candidates with paging and attribution where available; existing SDK artwork plugins remain compatible.
- [ ] #4 Confirmed linked copies share displayed choices with documented and tested unlink behavior; artwork matching never changes library identity.
- [ ] #5 Windows jump-list artwork uses a selected icon with existing cover fallback; desktop and fullscreen can read and save the same icon choice.
- [ ] #6 Focused persistence, plugin-compatibility and refresh tests pass; relevant architecture and plugin documentation describes shipped behavior.
<!-- AC:END -->
