---
id: TASK-124
title: 'A user-set name never reaches the library, the release name shadows it'
status: Done
assignee:
  - '@codex'
created_date: '2026-09-05 17:23'
updated_date: '2026-09-06 17:31'
labels:
  - ui
  - data
dependencies:
  - TASK-119
priority: high
type: bug
ordinal: 151000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Reported by the user after using the metadata editor: "the name metadata override doesnt populate into the library, it stays as it was matched previously".

Diagnosed. LibraryQueryRepository line 518 builds the tile title as:

    COALESCE(NULLIF(TRIM(r.name), ), w.name) AS Title

The release name wins over the work name, and ingest writes the storefront title to the release, so r.name is populated for essentially every owned entry. The metadata editor writes works.name via WorkFieldSourceRepository, so the value is stored correctly and then shadowed on read. The user sees no change and reasonably concludes the edit did not save.

The fix parallels the cover-precedence rule settled in TASK-106: a value the user owns outranks the storefront value. The query needs to consult work_field_sources for the name field with source user, and prefer w.name when the user owns it. Check every other surface that titles a game the same way — the list view, the feed, the Merges queue, search and sort — since a title that changes in one place and not another is worse than one that changes nowhere.

Consider also whether the same shadowing affects any other field the editor writes: the editor sets name, first_release_year, summary, cover_url, publisher and background_url, and any of those read through a COALESCE preferring a release or store value has the same defect.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A name the user set shows in the library grid
- [x] #2 It shows identically in the list view, the feed, the details modal, the Merges queue, and in search and sort
- [x] #3 Every other editor-written field is checked for the same shadowing, and any found is fixed or explicitly recorded as correct
- [x] #4 Tests cover a user-set name winning over a release name, and an unset name still deferring to it
- [x] #5 A game the user has not named keeps its automatic work title, and demo consolidation still prefers the storefront release title.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Verify the landed live-title refresh through editor save commands and repository-backed regression tests. 2. Audit the other editor fields and merge cover precedence. 3. Record the corrected diagnosis, verification evidence and close the task.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Reviewed 2026-09-06: the original AC3 assumed release-title display precedence that never existed; corrected to the behavior proved by UserSetNameTests. All other fields audited: first_release_year, publisher and summary read from the work into the tile and details; no competing release value. cover_url now uses user-art then pinned IGDB precedence in both library and Merges, covered by MergeQueueViewModelTests. background_url has no display consumer, so cannot be shadowed. Text edits preserve other drafts; art edits reload. UserSetNameTests and MetadataEditorModalTests: 13 passed. MergeQueueViewModelTests, GameMetadataEditorViewModelTests and the rating/executable suites: 164 passed together.
<!-- SECTION:NOTES:END -->

## Comments

<!-- COMMENTS:BEGIN -->
author: @claude
created: 2026-09-05 17:32
---
Diagnosis check, before any code changed. The description''s diagnosis does not hold, and
the query it names is not a display surface.

`OwnershipBucket` — the public projection `GetOwnershipBucketsAsync` returns — has no
`Title` member. The COALESCE at LibraryQueryRepository line 518 lands on the private
`BucketRow` record, whose own doc comment already says these columns stay off the public
projection because they are inputs to a decision the repository has already made by the
time the caller sees a row. Its one consumer is `DemoConsolidation`, inside the
repository. Changing it would alter demo/variant folding and fix nothing on screen.

Every display title comes from a separate `IWorkRepository` read of `works.name`:
LibraryViewModel line 1045, `title: display?.Name`. The Merges queue, the hidden-games
list and the hand-added list read `works.name` too. A test written before any fix
(`The_grid_tile_carries_a_user_set_name`) passes on the unmodified code: a user-set name
DOES reach the tile once the library loads.

What the user saw is real, and it is in the App layer. A text save does not reload the
library — design-system.md 10.10 makes that deliberate, because a reload would discard the
drafts in the other five rows — and nothing else refreshes the title, so the new name is
invisible for the rest of the session. Restarting the app shows it. Fixing that without a
reload is the work.
---
<!-- COMMENTS:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
The landed fix refreshes names in place after editor saves, preserving drafts and reapplying search and sort; Merges honors user cover art. Verified repository-backed title/modal and merge/editor regression tests. Corrected the original release-title diagnosis to match actual display behavior.
<!-- SECTION:FINAL_SUMMARY:END -->
