---
id: TASK-124
title: 'A user-set name never reaches the library, the release name shadows it'
status: In Progress
assignee:
  - '@claude'
created_date: '2026-09-05 17:23'
updated_date: '2026-09-05 17:32'
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
- [ ] #1 A name the user set shows in the library grid
- [ ] #2 It shows identically in the list view, the feed, the details modal, the Merges queue, and in search and sort
- [ ] #3 A name the user has not set still prefers the release name exactly as before
- [ ] #4 Every other editor-written field is checked for the same shadowing, and any found is fixed or explicitly recorded as correct
- [ ] #5 Tests cover a user-set name winning over a release name, and an unset name still deferring to it
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. VERIFY THE DIAGNOSIS FIRST. Done, and it does not hold. `OwnershipBucket` has no
   `Title` member: the `COALESCE(NULLIF(TRIM(r.name), ''''), w.name)` at
   LibraryQueryRepository line 518 lands on the private `BucketRow` and is consumed only
   by `DemoConsolidation` inside the repository. It never reaches a caller. Every display
   title in the app comes from a separate `IWorkRepository` read of `works.name`
   (LibraryViewModel line 1045 `title: display?.Name`). A scratch test proves a user-set
   name DOES reach the grid tile after a library load.
2. Establish what the user actually saw. A text save does not reload the library
   (design-system.md 10.10, deliberate: a reload would discard the drafts in the other
   five rows), and nothing else refreshes the title, so the new name is invisible until
   the app is restarted. That is the reported defect and it is in the App layer.
3. Fix it in the App layer without a reload: after a successful save of the `name` field,
   push the stored name onto the live tile and the details modal header, and re-apply the
   current sort and filter so the grid, the list view, the feed cards (which borrow the
   tile) and search/sort all move together. Delegate to the avalonia-ui agent.
   Do not touch src/Winnow.App/Views/GameDetailsView.axaml (TASK-123 owns it).
4. Leave LibraryQueryRepository line 518 and WorkRepository lines 157/195-200 alone: both
   want the STOREFRONT title, because both feed storefront-title heuristics (demo/variant
   consolidation, the demo-like prefilter). Add a comment at 518 saying so, so the next
   reader does not re-diagnose it as a display title. Prose via docs-writer.
5. Per-field audit of the other five editor-written fields, with a verdict recorded for
   each. Finding so far: `cover_url` IS shadowed, but in the Merges queue, not the grid.
   MergeQueueViewModel builds its own Steam-first cover ladder with no user-art rule and
   no IGDB-pin rule, so user-set cover art never draws there. Fix it to match
   LibraryViewModel''s TASK-106 precedence. `background_url` has no display consumer at
   all; record, do not expand scope.
6. Tests: a user-set name reaching the grid tile and the details modal; an unset name
   leaving the storefront title alone; the bucket query still handing DemoConsolidation
   the storefront title; the Merges queue drawing user-set cover art.
7. Docs, all authored by docs-writer: design-system.md 10.10 (what a text save refreshes),
   10.9/the merge-queue cover rule, and docs/decisions.md for every sentence replaced.
8. No migration. `work_field_sources` already records who owns each field, and nothing
   here needs a new column. Last shipped migration stays 0027.
<!-- SECTION:PLAN:END -->

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
