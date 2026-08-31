---
id: TASK-8
title: 'Fix recommendation correctness: coverage, pruning, maturity, and collapse'
status: To Do
assignee: []
created_date: '2026-08-29 21:52'
labels:
  - recommend
dependencies: []
priority: high
ordinal: 8000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Four recommendation scoring defects. Missing genre/tag coverage is treated as negative evidence instead of absent evidence (F15). Score-bound shortlist pruning can drop valid candidates (F32). The maturity tier is biased by library composition (F33). Work collapse must happen before shortlist capacity is applied, not after (F38). Sources: stabilization-2026-08-28.md Group 2, findings F15, F32, F33, F38. Trigger: next recommender scoring change.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 A game with no coverage data scores neutrally, not negatively
- [ ] #2 Shortlist pruning preserves candidates above the quality threshold regardless of score proximity
- [ ] #3 Maturity tier distribution is normalized against library composition
- [ ] #4 Work-level collapse precedes shortlist capacity enforcement
<!-- AC:END -->
