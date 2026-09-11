---
id: TASK-109
title: Persist per-group preferred platform for already linked games
status: To Do
assignee: []
created_date: '2026-09-05 02:49'
updated_date: '2026-09-11 14:04'
labels:
  - ui
dependencies: []
documentation:
  - game-library-design.md
  - design-system.md
priority: medium
type: feature
ordinal: 136000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Let users choose which store entry provides the header for an already linked group. This is a persisted per-group preference. The existing queue-wide preferred platform applies only to pending proposals and does not implement this feature. Keep presentation preference changes compatible with identity-link invariants.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Users can choose the store supplying an already linked group's header on desktop and fullscreen.
- [ ] #2 The per-group preference persists across launches, re-ingest and further links.
- [ ] #3 The selected preference is visible and reversible.
- [ ] #4 If the preferred store is unavailable or removed, the group falls back to a valid automatic header.
- [ ] #5 Verify identity and undo invariants, and keep this preference distinct from pending-proposal queue defaults.
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: MergeQueueViewModel skips resolved cards when changing the queue-wide platform preference. FullscreenLibraryToolsPage offers Make header only for unresolved cards. Completed TASK-178 explicitly excludes persistent preferences for already linked groups; it is related work, not duplicate delivery.
<!-- SECTION:NOTES:END -->
