---
id: TASK-49
title: Decide on GOG sign-in scope based on session-history endpoint
status: To Do
assignee: []
created_date: '2026-08-29 21:55'
updated_date: '2026-08-29 21:56'
labels:
  - auth
  - ingest
dependencies: []
priority: low
ordinal: 49000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
GOG sign-in is held because the authenticated endpoint carries no playtime, no last-played, no title, and no DLC flag, all of which the local reader already has. A GOG sign-in button would add a login, a stored credential, and an embedded browser in exchange for nothing. One reopen condition exists: `GET gameplay.gog.com/.../sessions` accepts GET (PUT/DELETE answer 405) but no known client reads it and its payload is unverified. If it carries session history, that is longitudinal data worth having and the feature gets rescheduled. Source: ROADMAP.md section 4 ("GOG is held, and the reason corrects an error").
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The `gameplay.gog.com/.../sessions` endpoint is probed and its payload documented
- [ ] #2 If it carries session history, a GOG sign-in task is created with defined scope
- [ ] #3 If it carries nothing useful, the held status is ratified with a documented finding
<!-- AC:END -->
