---
id: TASK-211
title: Share cover selection policy across library merge and preview surfaces
status: To Do
assignee: []
created_date: '2026-09-11 05:00'
updated_date: '2026-09-11 05:06'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.App/ViewModels/LibraryViewModel.cs:961'
  - 'src/Winnow.App/ViewModels/MergeQueueViewModel.cs:1888'
  - 'src/Winnow.App/ViewModels/MergeQueueViewModel.cs:2059'
  - 'src/Winnow.App/Services/PluginSyncService.cs:165'
documentation:
  - docs/architecture-review-2026-09-10.md
priority: medium
type: bug
ordinal: 242000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R23. Evidence: Source verified. LibraryViewModel handles PluginArtRef and source availability in its cover ladder. MergeQueueViewModel duplicates the policy but only understands user art, pinned IGDB, Steam and ordinary IGDB; its fallback work preview is narrower still. PluginSyncService already stores plugin art observations. The same work can have a cover in the library and a placeholder in the merge queue. Repeated provider precedence logic has already diverged.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 One cover-selection policy defines provider availability, precedence and typed artwork references for library, merge groups and fallback previews.
- [ ] #2 Plugin artwork receives the same supported behavior as other sources while preserving user override and pin precedence.
- [ ] #3 Parity regressions cover plugin/user/IGDB/Steam combinations and unavailable sources across desktop and fullscreen consumers.
<!-- AC:END -->
