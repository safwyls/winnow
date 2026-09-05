---
id: TASK-119
title: 'Edit a game metadata by hand, including cover and background art'
status: To Do
assignee: []
created_date: '2026-09-05 03:47'
labels:
  - ui
  - data
dependencies:
  - TASK-89
priority: medium
type: feature
ordinal: 146000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
User request: "we should allow a user to manually change the metadata with an editor if desired allowing them to set all the fields we normally pull from igdb including the cover art and background art".

Today the user has two partial recourses and neither is an editor. TASK-89 lets them pick a different IGDB record, which replaces every field at once with another source truth. TASK-99 lets them hand-build an entry from nothing. Neither lets them correct one wrong field on a game that is otherwise right, and nothing lets them supply their own art.

This needs a field-level editor over the same fields enrichment writes — title, release year, publisher, summary, genres and the rest — plus cover art and background art from a local file or a URL.

The hard part is precedence, not the form. A hand-edited field must survive later enrichment passes the way TASK-89 pin survives them, but at field granularity rather than per work: a user who fixes the year must not lose it on the next pass, while the fields they did not touch should keep updating. Decide and document how a hand-edited field, an IGDB pin and an automatic pass compose, and where a user edit can be reverted to automatic. Migration 0026 established the pin shape; the last shipped migration is 0026.

Note TASK-111 covers pulling screenshots from IGDB; user-supplied background art should share whatever surface that produces rather than inventing a second one.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Every field enrichment writes can be edited by hand from the details view
- [ ] #2 Cover art and background art can be set from a local file or a URL
- [ ] #3 A hand-edited field survives later enrichment passes; untouched fields keep updating
- [ ] #4 The user can see which fields they have overridden, and revert any of them to automatic
- [ ] #5 How a hand edit, an IGDB pin and an automatic pass compose is documented, not merely implemented
- [ ] #6 User-supplied art goes through the existing cover cache and honours the --data-dir override
- [ ] #7 Tests cover a hand-edited field surviving an enrichment pass and reverting cleanly
<!-- AC:END -->
