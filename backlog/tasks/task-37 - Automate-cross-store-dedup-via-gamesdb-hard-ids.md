---
id: TASK-37
title: Evaluate GamesDB references for reversible cross-store identity links
status: To Do
assignee: []
created_date: '2026-08-29 21:54'
updated_date: '2026-09-11 14:01'
labels:
  - resolve
  - enrich
dependencies: []
documentation:
  - game-library-design.md
priority: medium
ordinal: 87000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Evaluate GamesDB cross-store references as evidence for reversible same-game links. The current enrichment planner uses those references for metadata lookup and writes no identity links. Preserve source releases and globally unique external identifiers; automate only where evidence establishes compatible game and edition identity, with ambiguous mappings left reviewable.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Record validated cross-store reference evidence without assigning another release's globally unique external ID.
- [ ] #2 Define and test the evidence sufficient for automatic linking; ambiguous or conflicting edition mappings cannot auto-link.
- [ ] #3 Qualified links use existing reversible identity operations and admission rules.
- [ ] #4 Repeated observations are idempotent and preserve explicit user decisions.
- [ ] #5 Verify library, queue, details and undo behavior on desktop and fullscreen.
- [ ] #6 Report eligible, conflicting and unresolved coverage; no arbitrary queue-size reduction is required.
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: EnrichmentLookupPlanner routes Epic metadata through Steam/GOG references without writing external IDs. IdentityLinkRepository supplies reversible links. Completed TASK-70, TASK-83 and TASK-189 are the integration context; retired destructive TASK-5 is not a prerequisite. GamesDB game-level references do not by themselves prove edition equivalence.
<!-- SECTION:NOTES:END -->
