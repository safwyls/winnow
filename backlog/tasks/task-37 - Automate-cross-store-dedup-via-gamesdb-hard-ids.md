---
id: TASK-37
title: Evaluate GamesDB references for reversible cross-store identity links
status: To Do
assignee: []
created_date: '2026-08-29 21:54'
updated_date: '2026-09-11 16:06'
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
Acquire and validate edition-specific evidence for broader reversible cross-store identity automation. GamesDB references now qualify for automatic linking only when both referenced releases have matching positive IgdbVersionId values; ordinary launcher imports do not populate that field, so most pairs remain reviewable. Preserve source releases, globally unique external IDs, explicit user decisions and ambiguous edition mappings.
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

PR-12 rebase review against merged PR-13 added a conservative release-version gate, transactional release/root evidence checks and pending-pair cleanup, plus integration with the shared startup/scheduled/account refresh pipeline. Broad GamesDB-only linking is deliberately not claimed complete: game-level references alone do not prove edition equivalence, and production edition-evidence acquisition remains outstanding.
<!-- SECTION:NOTES:END -->
