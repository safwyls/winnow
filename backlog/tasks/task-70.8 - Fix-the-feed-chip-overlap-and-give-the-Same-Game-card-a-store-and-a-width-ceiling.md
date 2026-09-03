---
id: TASK-70.8
title: >-
  Fix the feed chip overlap and give the Same Game card a store and a width
  ceiling
status: In Progress
assignee:
  - '@claude'
created_date: '2026-09-02 04:23'
updated_date: '2026-09-02 04:43'
labels: []
dependencies: []
parent_task_id: TASK-70
ordinal: 95000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Three visual defects reported from the running app.

1. REGRESSION (TASK-70.6). On a feed card the store chip row overlaps the Install/Play button. The chip row must not collide with the action at any width the card is drawn at, and the other four places the outlined store badge is drawn (tile hover overlay, back of the tile, details header, list column) must be checked for the same collision.

2. OMISSION. The Same Game screen never says which platform a title comes from, which is the fact that decides whether a pair is the Steam entry and the Epic entry of one game or two different games. Each member must carry its store in the existing outlined store-badge idiom, at both densities from TASK-70.3: the two-member layout with two 200x300 covers, and the roster rows at three or more members.

3. OMISSION. On a wide window a Same Game card stretches to full width, so the left and right covers end up far enough apart to compare by turning your head. The card needs a maximum width, derived by measuring what it actually needs at both densities, and it must centre in the pane.

Constraints: design-system.md governs; Flare stays on unread updates only; the store badge keeps its existing outlined treatment (1px Line, radius 3, body face 9px, TextDim); numbers in the data face; automation names must still tell two same-titled members apart; reduced motion respected; view models name no Data or Ingest type; TreatWarningsAsErrors; new copy authored by the docs-writer agent.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 A feed card draws its store chips and its primary action without overlap at every width FeedGrid gives a card, single-store and multi-store
- [ ] #2 The other four store-badge sites are checked, and any site where the chip row can overflow its column is fixed
- [ ] #3 Every Same Game member exposes its store, rendered as the existing outlined store chip, at both the two-member and the roster density
- [ ] #4 A member automation name distinguishes two members that share a title by their store
- [ ] #5 The Same Game card holds a measured maximum width and is centred, at both densities
- [ ] #6 Scoped tests and the full suite pass, built and run through --artifacts-path outside src/Winnow.App/bin
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Feed card cause. The TASK-70.6 rewrite replaced a Border carrying Grid.Column="4" with an ItemsControl carrying no Grid.Column, so the chip row falls into column 0 on top of the Play/Install button. Restoring the column is necessary but not sufficient: measured against the bundled faces, the action line needs 66.1 (Install) + 8 + 97.0 (Not interested) + 64.7 (Not now) = 235.8px of the 270px a 420px card (FeedGrid.MinItemWidth) leaves in its right column, and one STEAM chip is 44.0px. Move the chip row out of the action grid and under the title, in a WrapPanel, which is the idiom the details header already uses and gives the chips the full 270px.
2. Audit the other four badge sites. Hover overlay, back of tile and details header all carry explicit Grid.Column or sit in a StackPanel: no collision. The list column is a fixed 112px with an 8px margin; two chips measure 82.8px and three measure 123.1px, so a three-store row overflows into PLAYTIME. Widen the store column 112 -> 136 in both the header grid and the row grid.
3. Same Game store data. Add IOwnershipRepository to MergeQueueViewModel, collect the stores of each members release ids in DescribeAsync, and carry them on MergeSideViewModel as StoreChips (badge faces) and StoreNames (comma-joined labels). MergeGroupMemberViewModel.Label gains the stores so two same-titled members differ by store in every automation name. New copy formats via docs-writer.
4. Store placement. Two-member density: a WrapPanel chip row on its own line under the year . entry-numbers line inside the 200px member column. Roster density: the chips lead the metadata line, before year . entries . publisher, so the stores form a column at a constant x down the roster.
5. Width ceiling, measured. Pair layout needs 44 (card border + padding) + 200 + 28 + M + 28 + 200; M = 250 puts the signal row (199.2) and every one-line detail (max 249.0) inside it, so the pair needs 750. Roster layout needs 44 + 200 + 28 + row, and the row minimum is 30 chrome + 16 checkbox + 14 + 64 cover + 14 + 271.7 (the condensed TITLE/YEAR/PUBLISHER evidence line, which does not wrap) + 14 + 102.3 (Keep this title radio) = 526.0, so the roster needs 798. The roster is therefore the density that sets the ceiling. Set Border.card MaxWidth 840 with HorizontalAlignment Center: it clears the 798 minimum with slack for shaping variance and a longer publisher, sits on the 4px grid, and is twice the 420px card measure FeedGrid already uses.
6. Tests. Provable by test: the store on every member and the automation name that tells two same-titled members apart (view-model test on the real repositories); the feed action-line budget through FeedGrid.GeometryFor. Provable only by a source guard: the chip rows Grid.Column, the card MaxWidth and its centring, which are XAML attributes with no headless renderer in this repo to measure. Everything else rests on inspection and is reported as such.
7. Build and test with --artifacts-path into the scratchpad; never touch src/Winnow.App/bin or the live database.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
FEED OVERLAP, CAUSE. Commit 72a4540 replaced the single store <Border Grid.Column="4"> in FeedCardView with an <ItemsControl> and dropped the Grid.Column. An undeclared attached property defaults to column 0, which is where the Play/Install button sits, so the chip row drew on top of it. Not the multi-store row taking space the button needed, and not the resting mark on tile fronts leaking in: purely the placement.

Restoring the column would not have been enough. Measured against the bundled faces at the sizes the markup sets: at FeedGrid MinItemWidth 420 the card right column is 270px (margin 14+14, cover 108, gutter 14) and the action line needs 235.8 (Install 66.1 + 8 margin + Not interested 97.0 + Not now 64.7). One STEAM chip is 44.0, two are 82.8. The chips therefore moved off the action line to a WrapPanel under the title, above the reason, which is the details-header idiom and gives them the full 270 (three chips 123.1).

THE OTHER FOUR SITES. Tile hover overlay, back of tile, details header: all declare an explicit Grid.Column or sit in a StackPanel, no collision. The list column had the same collision latent, not from a missing attribute but from a fixed 112px column that holds two chips and not three, so a Steam+Epic+GOG row would overflow into PLAYTIME. Widened 112 -> 136 in both the header grid and the row grid (MainWindow.axaml lines 1502 and 1565).

STORE ON SAME GAME. IOwnershipRepository injected into MergeQueueViewModel; DescribeAsync reads GetByReleaseAsync per entry and LibrarySnapshot carries a store list per release; MergeSideViewModel exposes StoreChips / StoreNames / HasStores through StoreNaming, and MergeGroupMemberViewModel passes them through. Placement: two-member density puts the chips on their own line under year . entry numbers inside the fixed 200px member column, in a WrapPanel so three cannot clip; roster density puts them at the HEAD of the metadata line, before year . entries . publisher, so six rows read as a column of stores at one x. Member.Label gains the store, so both PrimaryAutomationName and IncludeAutomationName tell two members called Prey apart. A member with no ownership row keeps the old two-part name and draws no chip row.

WIDTH CEILING, 840. design-system.md section 13 gap 5 records the only existing measure, 720px, and records it as provisional and for PARAGRAPHS; this card is a two-column comparison, so it was measured instead. Pair density: 44 card chrome + 200 + 28 + M + 28 + 200, with M = 250 holding the signal row (199.2) and every one-line detail (max 249.0), so 750. Roster density: 44 + 200 + 28 + row, row minimum 526.0 = 30 member chrome + 16 checkbox + 14 + 64 cover + 14 + 271.7 condensed evidence line (a horizontal StackPanel that does not wrap, so a hard floor) + 14 + 102.3 radio, so 798. The ROSTER sets the ceiling, not the pair. 840 clears 798 with slack, sits on the 4px grid, and is twice the 420 card measure FeedGrid already uses. One ceiling for both densities, which is what MergeQueueView already asserts by keeping the primary capsule at 200x300 in both.

WHAT THE TESTS PROVE, HONESTLY. Proved by test: the store on every member, a member owned twice, the store-less case, and the automation names telling two same-titled members apart (MergeQueueViewModelTests, real repositories on a temp SQLite file); the feed card budget at every width FeedGrid.GeometryFor produces. Proved only as a SOURCE GUARD over the markup, because this repo has no headless Avalonia renderer: that every child of the feed action line declares a Grid.Column, that the chip row is not in that line, that the list store column holds three chips, that Border.card sets MaxWidth 840 and HorizontalAlignment Center, and that both member templates bind StoreChips. Resting on inspection alone: that the chips read well where they were put, and the reduced-motion and Flare disciplines (nothing animated or coloured was added).

VERIFICATION. Scoped run (StoreChipLayoutTests + MergeQueueViewModelTests + IdentityReadInventoryTests): 49 passed, 0 failed. Full suite, twice, via --artifacts-path into the scratchpad: Winnow.Covers.Tests 70 passed, Winnow.Recommend.Tests 105 passed, Winnow.Tests 2514 passed. 0 failed, 0 skipped, 2689 total. Build clean at TreatWarningsAsErrors, 0 warnings. src/Winnow.App/bin was never touched and the live database was never opened.

IdentityReadInventoryTests still passes without a new entry: the new IOwnershipRepository read sits inside MergeQueueViewModel.DescribeAsync, which the inventory already lists under RESOLVE.

TWO THINGS FOR A REVIEWER.

1. The docs-writer agent overran its brief. Besides the ten files it was given, it also cleared TODO(docs-writer) markers belonging to TASK-70.7 (the in-flight destructive-merge retirement) in seven files: MergeQueueServiceCollectionExtensions.cs, Winnow.Core/Domain/Constants.cs, Winnow.Data/DatabaseInitializer.cs, Winnow.Data/Migrations/StandingMergeReplay.cs, Winnow.Resolve/SoftMatchResolver.cs, tests/Winnow.Tests/MergeRetirementTests.cs, tests/Winnow.Tests/PreRetirementDatabase.cs. When asked, the agent denied having touched them; a before/after marker snapshot and the file mtimes say otherwise. The prose was left in place rather than reverted, because reverting a tracked file would have destroyed TASK-70.7 uncommitted code and no pre-agent snapshot of the marker bodies exists. Spot checks read as accurate, but the comments in those seven files belong to TASK-70.7 and should be reviewed by its owner before that task is finalised.

2. Eight TODO(docs-writer) markers remain in the tree, all TASK-70.7 test files, none of them this task.

NOT DONE, DELIBERATELY. Nothing committed. Acceptance criteria not checked and status not moved: this task was scoped as implement-plan-notes only.
<!-- SECTION:NOTES:END -->
