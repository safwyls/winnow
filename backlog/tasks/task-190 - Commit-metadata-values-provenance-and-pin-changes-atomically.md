---
id: TASK-190
title: Commit metadata values provenance and pin changes atomically
status: To Do
assignee: []
created_date: '2026-09-11 04:58'
updated_date: '2026-09-11 05:06'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.Data/Repositories/WorkFieldSourceRepository.cs:132'
  - 'src/Winnow.Data/Repositories/WorkIgdbPinRepository.cs:32'
  - src/Winnow.Data/Repositories/WorkRepository.cs
  - 'src/Winnow.App/Services/WorkMetadataEditService.cs:69'
  - src/Winnow.App/Services/PluginSyncService.cs
documentation:
  - docs/architecture-review-2026-09-10.md
priority: high
type: bug
ordinal: 221000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R02. Evidence: Reproduced. WorkFieldSourceRepository writes a field and its provenance in separate statements without a local transaction. Rejecting the provenance INSERT leaves the new value committed without user ownership. WorkIgdbPinRepository clears/inserts pins before updating works; rejecting the works UPDATE leaves live pin222 while works still maps to111. WorkRepository enrichment also depends on caller transaction coverage, while PluginSyncService calls it directly. A failed edit can leave requested user ownership unrecorded or leave contradictory IGDB identity authorities. RepositoryWriteBatch already supplies a suitable local transaction/savepoint pattern.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Public field set/reset, pin and enrichment operations atomically persist values, provenance and history when called alone or inside an ambient unit of work.
- [ ] #2 Fault injection at each dependent write and cancellation cannot leave partial changes, including when an ambient caller catches the error and commits other work.
- [ ] #3 Built-in enrichment, plugin enrichment and metadata editing on both surfaces use the same atomic contract and preserve user-owned fields.
<!-- AC:END -->
