---
id: TASK-97
title: >-
  Rename the bucket labels: "Patched since" reads as "Patched", and "Bounced
  off" misdescribes what it holds
status: Done
assignee:
  - '@avalonia-ui'
created_date: '2026-09-04 18:14'
updated_date: '2026-09-04 18:56'
labels:
  - ui
  - docs
dependencies: []
priority: medium
type: task
ordinal: 124000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Two bucket names in LibraryViewModel read wrong. "Patched since" is an unfinished sentence — it should read "Patched". "Bounced off" implies the user quit a game, but the bucket is defined by playtime at or above the refund line (BouncedFloorMinutes, 120 by default) and short of played-out, so it also holds games in active play. Rename the first, and settle what the second bucket actually means before renaming it — the name and the rule have to agree.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The stale-but-patched bucket label reads "Patched" wherever the user sees it
- [x] #2 The bounced bucket is either renamed to match its rule, or its rule is narrowed to match the name, and the decision is recorded in docs/decisions.md
- [x] #3 Bucket ids in Winnow.Core are untouched — this is a label change, not a schema change
- [x] #4 Every documentation reference to the renamed labels is updated in the same commit
- [x] #5 Build succeeds and no test regresses
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. 'Patched since' -> 'Patched' in LibraryViewModel's bucket list, and in every live document that quotes the label (design-system.md copy table and the places that name the bucket, mock-library.html rail).
2. Bounced: rename the label rather than narrow the rule. Narrowing means editing src/Winnow.Core/Queries/LibraryBucketRules.cs, which another agent owns right now; the 120-minute refund floor is also a documented rule (game-library-design.md §6.1, docs/decisions.md, BucketQueryTests) rather than an accident, so the rule is right and the name is wrong.
3. Delegate the replacement word to docs-writer with the constraints: it must describe a playtime band (past the refund line, short of played out) without implying the user quit, must not be 'Barely played' (the copy table forbids it), and must sit in the rail beside Never played / Played out / Won't run.
4. Apply the chosen label in LibraryViewModel, mock-library.html, and the tests that assert on it (FilterPanelViewModelTests, ListsViewModelTests, GameDetailsViewModelTests, TileActionsTests, UpdateFlagTests).
5. docs-writer edits design-system.md §7 copy table and the surrounding references, game-library-design.md §6.1's sentence about what 'Bounced off' names, README.md's bucket bullet, and appends both superseded sentences plus the decision to docs/decisions.md.
6. Bucket ids in Winnow.Core untouched. dotnet build + dotnet test.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Two label changes, no schema change. LibraryBuckets.StaleButPatched and LibraryBuckets.Bounced are untouched.

'Patched since' -> 'Patched'. The old label was an unfinished sentence. The badge/tooltip sentence 'Patched since you played' is a different string and is deliberately unchanged.

'Bounced off' -> 'Started'. DECISION: the label was changed to fit the rule, not the rule narrowed to fit the label. Three reasons. (1) The 120-minute floor is a deliberate, documented threshold — game-library-design.md §6.1, docs/decisions.md, and a test literally named BouncedFloorIsOneTwentyMinutes — not an accident to be tuned away. (2) Narrowing would mean adding a recency term to a bucket family whose whole point is that the buckets are playtime bands computed as queries; the precedence rule 'stale outranks bounced' is itself stated in terms of that span. (3) src/Winnow.Core/Queries/LibraryBucketRules.cs is owned by another agent this session and was off-limits, which the task anticipated. The word 'Started' was chosen by docs-writer: it is true of every member of the band, implies neither quitting nor dormancy, is not the forbidden 'Barely played', and makes the playtime axis read Never played -> Started -> Played out.

Code: the two BucketViewModel labels in LibraryViewModel; mock-library.html's rail; the copy string in StoresViewModel; section comments in SampleDataSeeder, Program.cs and GameTileViewModel (all prose by docs-writer). The tile's back face and the detail modal's chip both read BucketLabel from the library's bucket list, so they follow automatically.

Documentation (all authored by docs-writer): design-system.md §4's rail sketch (re-padded so the ASCII box still closes), §6's rail-bucket paragraph, §7's copy table (with 'Bounced off' added to the Don't-write column) and its empty-state bullet, §10.1's detail-modal chip (re-padded), §11's no-has-updates-group sentence, §12's save-prompt example; game-library-design.md §6.1's false claim that the band names a user who 'gave up anyway'; README.md's bucket bullet. docs/decisions.md carries the decision and both superseded sentences verbatim, per AGENTS.md.

Deliberately not touched: docs/plans/ssot-migration.md, docs/code-review-2026-08-28.md, docs/merge_queue_design/ and notes.md — dated records quoting the old text, which rewriting would falsify. game-library-design.md's bucket-rule table row 'Bounced' and the precedence paragraph name the rule/id, not the label.

RESIDUAL, needs another owner: four comments still name the old label in files another agent holds — src/Winnow.App/Views/GameTileView.axaml:639 ('the "Patched since" bucket name'), src/Winnow.Core/Queries/LibraryFilter.cs:89, src/Winnow.Data/Repositories/LibraryQueryRepository.cs:17 and :175. All are comments; no user-visible string is affected.

Tests updated where they assert the label: FilterPanelViewModelTests, ListsViewModelTests (CutChips and the live-list save prompt), GameDetailsViewModelTests, TileActionsTests, UpdateFlagTests. VisualDisciplineTests' Flare allowlist still matches on 'Patched since', which is the tooltip string that remains in the markup.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
'Patched since' now reads 'Patched', and 'Bounced off' now reads 'Started'. The bounced bucket was renamed to match its rule rather than the rule narrowed to match the name: the 120-minute refund floor is a documented, tested threshold, narrowing it would put a recency term into a family of pure playtime bands, and LibraryBucketRules.cs was owned by another agent. The decision and both superseded sentences are recorded verbatim in docs/decisions.md. Bucket ids stale_but_patched and bounced are untouched. Every live documentation reference moved in the same change — design-system.md (copy table, rail sketch, detail-modal chip, four prose references), game-library-design.md §6.1's false 'gave up anyway' claim, README.md's bucket bullet, mock-library.html — while dated records that quote the old text were left as records. Verified: dotnet build clean and dotnet test — 2922 passed in Winnow.Tests, 152 in Winnow.Recommend.Tests, 70 in Winnow.Covers.Tests, including the five test files that assert on these labels.
<!-- SECTION:FINAL_SUMMARY:END -->
