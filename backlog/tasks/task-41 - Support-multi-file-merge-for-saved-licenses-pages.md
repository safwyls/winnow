---
id: TASK-41
title: Support multi-file merge for saved licenses pages
status: To Do
assignee: []
created_date: '2026-08-29 21:54'
labels:
  - ingest
dependencies: []
priority: low
ordinal: 41000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The saved-file licenses route captures one page per file. The embedded route paginates automatically, but the manual save route inherently gets one page per saved HTML. Multi-file merge in the loader is the fix if coverage matters for accounts with more than 100 licenses. Source: ROADMAP.md section 6.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The saved-file loader accepts multiple HTML files for the licenses page
- [ ] #2 Rows from multiple files merge without duplication
- [ ] #3 A test with two fixture files demonstrates correct merge
<!-- AC:END -->
