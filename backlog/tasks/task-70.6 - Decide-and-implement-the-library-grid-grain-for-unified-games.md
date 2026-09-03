---
id: TASK-70.6
title: Decide and implement the library grid grain for unified games
status: In Progress
assignee:
  - '@claude'
created_date: '2026-09-02 00:14'
updated_date: '2026-09-02 03:54'
labels: []
dependencies:
  - TASK-70.4
parent_task_id: TASK-70
ordinal: 93000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Stage 5 of TASK-70. **Gated on a product decision, and blocked until the user makes it.** Do not start this without an answer to items 1 and 2 in the product-decision list on TASK-70.

The library grid renders one tile per ownership. It always has, and merging never changed that: a game owned on Steam and Epic is two tiles before unification and two tiles after it, work-only or collapsed. If the user means their library to contain one row per game, this stage is the actual fix, and every earlier stage exists so that this one can be made safely rather than by deleting rows.

**Two questions the implementer cannot answer.**

1. Does the grid show one tile per game with store chips, or one per ownership as today? Note that design-system 11.2 fixes store title counts as per tile on purpose, so a game owned twice counts in both stores. That rule can survive a per-game grid, but only if the two counts are computed from different relations, and that must be said out loud rather than discovered.
2. What is the headline playtime of a game owned on two stores: the sum across stores, the maximum, or per store only? Summing two real observations is defensible. Combining store A minutes with store B last-played into one tuple is the F10 hazard from the stabilisation review and must not happen, whatever is chosen for the headline. Per-store rows stay visible in the details modal in every option.

**Consequences to design once the answers exist.** Buckets stop classifying an ownership and start classifying a game, which changes what Bounced off and Played out mean for a cross-store title. The rail counts change. Filters that cut on store need a defined meaning for a tile that is on two. The recommender sees one candidate where it saw two, which is the point but changes shelf competition. List membership, which is per release by deliberate design, needs a display rule when both entries of one game are in one list.

**Tests.** A game owned on two stores renders one tile with two store chips. Its bucket is derived from the chosen playtime rule and is asserted against the rule, not against a number. The rail counts, the All Games count and the store title counts each agree with their own stated definition, and the store counts still count a twice-owned game twice. No tile ever displays a playtime and a last-played date drawn from different ownerships. The recommender offers a cross-store game once.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The product decisions on grid grain and on headline playtime for a twice-owned game are recorded before implementation starts
- [ ] #2 A game owned on two stores appears in the grid according to the recorded decision, and the rail counts, All Games count and store title counts each agree with their own stated definition
- [ ] #3 No tile displays a playtime and a last-played date taken from different ownerships
- [ ] #4 Bucket membership for a cross-store game is asserted against the chosen playtime rule rather than against a hard-coded number
- [ ] #5 The recommender offers a cross-store game once
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. GRAIN. One tile per ResolvedWorkId. The tile keeps a PRIMARY entry (ownership + release) for everything that is stored per release -- launch, lists, snapshots, journal, feed lookup -- and gains a member list, one entry per store. Expansion links cannot collapse anything: ResolvedWorkId folds same_game only (bucket query kind filter, migration 0018).

2. WHAT A COLLAPSED TILE TAKES FROM THE PRIMARY, deliberately. Cover, title, release year, publisher, summary and the provisional flag come from the PRIMARY WORK and members never vote: the primary is the one fact in the group the user actually chose (the KEEP radio on the Same Game card), so a tile that took a majority or a first-seen value would show something nobody decided. Dormancy and the bucket do NOT come from the primary -- they come from the GROUP, because dormancy answers when you last touched this game and you touched it on the other store.

3. THE GROUP FIGURES COME FROM 70.4'S FACTORY, NOT FROM A LOCAL SUM. Add IPlayedEntry (PlaytimeMinutes + LastPlayedAt) to Winnow.Core.Identity; CoverageEntry and OwnershipBucket both implement it; CoveragePlaytime.Across widens to IEnumerable<IPlayedEntry> (covariant, so every existing call site compiles untouched). The grid headline and the modal TOTAL are then the same function over the same entries and cannot disagree.

4. THE BUCKET RULE MOVES TO CORE, and this is the one place the design has to change rather than be extended. A group bucket cannot be the highest-precedence member bucket: two entries at 60 minutes each are two Active rows and one Bounced game, so the thresholds must be re-applied to the SUM (AC #4). That needs the rules evaluated at two grains, and two evaluations must never be two implementations. New pure LibraryBucketRules.Classify(playtime, lastPlayed, majorUpdateAt, thresholds) in Winnow.Core.Queries carries the CASE verbatim, including SQLite's own +N months normalisation (2024-03-31 +6 months = 2024-10-01, not 2024-09-30) and its second-precision truncation. The SQL drops its CASE and emits mu.occurred_at as MajorUpdateAt instead. Buckets stay derived on read from stored facts, which is what section 6.1 requires; they simply derive one step later.

5. WHY THE FOLD IS NOT IN SQL. Window functions over the resolved work would fold rows that Consolidate() then drops -- a consolidated demo, a hidden non-game, an account-filtered row. The grid would then stand behind a sum the details modal refuses to report, which is the exact contradiction IdentityCoverage.For was built to avoid. The fold therefore runs in the repository immediately AFTER Consolidate, over the visible rows, which is also why it is not in the view model: every consumer of GetOwnershipBucketsAsync (grid, rail, All Games, filter options, list counts, recommender, feed) inherits one answer instead of each folding its own.

6. DATA. OwnershipBucket gains MajorUpdateAt and Game -- a shared GameGrouping record (ResolvedWorkId, Bucket, PlaytimeMinutes, LastPlayedAt, EntryCount, MemberOwnershipIds) held by reference by every member, so agreement between members is structural. HiddenCountAsync counts distinct resolved works, because the label says N games hidden and a game is now a tile.

7. TILE. GameTileViewModel takes an ordered member list (TileEntry: ownership, release, store, own minutes, own last-played, install state, launch route). Stores/StoreChips/IsMultiStore/StoreInitials for the chips. Playtime, last-played, StatText, IdleText, dormancy and bucket read the GROUP. PrimaryAction resolves to the entry that is ON DISK, else the primary's own -- Play must never launch a copy the user does not have. AutomationProperties name distinguishes a collapsed tile and names its stores in words.

8. CHIPS, using the idiom that already exists rather than a new one. The outlined store badge (1px Line, radius 3, 5x1 padding, body face 9px, 0.54 tracking, TextDim) already appears in the hover scrim, the back face, the feed card, the details header and the list column. It becomes a ROW of the same pill, one per store. A single-store tile is byte-identical to today everywhere. At REST on the front face a single-store tile draws nothing at all; a multi-store tile draws a compact initial chip row (S / E / G) bottom-left which fades out under the hover scrim exactly as the placeholder art title already does, because at the 108px density floor a row of word chips is wider than the tile and because the fact is only news on the rare tile that has it. The words are one hover away, on the back face, and in the modal, so the mark is decorative-redundant per section 8.

9. COUNTS, one relation each, stated. All Games / rail buckets / TotalCount / list counts: per TILE, on the group bucket. Filter panel store group: selector returns t.Stores, so a two-store tile counts under both. Platforms screen: TitlesByStore counts TILES THAT INCLUDE THAT STORE -- the same relation as the filter panel's store residual with nothing else cut, so the two readings on screen are the same number by construction. The store counts sum to more than All Games by exactly the number of extra store memberships, which is the section 11.2 rule surviving the grain change rather than contradicting it. IStoreTitleCounts stays unresolved.

10. FILTERS AND LISTS. FilterableRow.Store becomes Stores (IReadOnlyList<string>); LibraryFilterMatcher matches ANY. Facets union across members. A tile is in a list when ANY member release is; adding adds the primary release, removing removes every member release.

11. DETAILS AND FEED. OpenDetailsAsync reads update events for EVERY member release, merged newest-first and compared against the GROUP last-played, so the badge and the update list cannot disagree about which patch is unread. TileForOwnership / TileForRelease match any member. GameDetailsViewModel header takes the chip row; ALSO COVERS is unchanged and is the per-store breakdown the composite is checked against.

12. RECOMMENDER. One candidate per resolved work (AC #5): the pool keeps the primary entry and takes its facts from the group, so the feed and the grid agree on bucket and playtime.

13. COVER WALL. Geometry is a closed form in the item count and needs no change; verify with new tests pinning columns, cell rects, flush-row slack and scroll extent at several densities and counts. Leases are per tile per key, so collapsing strictly reduces the lease count and disturbs nothing.

14. TESTS. A linked pair yields one tile with two chips; an unlinked library yields exactly today's tiles, one for one; the summed playtime comes from the coverage factory and never pairs with a foreign last-played; chips list every store and no more; rail, bucket, filter, Platforms and grid counts agree; an expansion link does not collapse; selection, keyboard and details open from a collapsed tile; the classifier matches SQLite's own month arithmetic at boundary dates; the wall holds at several densities.

15. Scoped runs, then the FULL suite across all three projects, built via --artifacts-path into the scratchpad because the user is holding src/Winnow.App/bin. All prose via docs-writer under the brevity instruction. Never open the live database. Do not commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Unblocked 2026-08-31: grid grain decided as one tile per game with store chips.

IMPLEMENTED, not finalized. Full suite green. Not committed. The live database was never opened.

THE GRAIN. The grid is one tile per RESOLVED WORK. The tile keeps a PRIMARY entry (ownership + release) for everything stored per release -- launch, lists, snapshots, the journal, the feed's lookup, the modal's own row -- and carries a member list, one TileEntry per store entry. Expansion links collapse nothing: ResolvedWorkId folds same_game only, filtered in the bucket query from IdentityLinkKinds.SameGame.

WHAT A COLLAPSED TILE SHOWS, AND WHAT IT TAKES FROM THE PRIMARY. Cover, title, release year, publisher, summary and the provisional-name flag come from the PRIMARY WORK, and the members never vote. The reason is stated rather than assumed: the primary is the one fact about the group the user actually decided -- the KEEP radio on the Same Game card in 70.3 -- so a tile that took a majority or a first-seen value would show something nobody chose. What does NOT come from the primary is playtime, last-played, dormancy and the bucket. Those are the GAME's. Dormancy in particular: it answers when you last touched this game, and you touched it on the other store, so a tile faded on the primary's own date would ghost a game played last week. Play is a third case again -- it acts on the entry that is ON DISK whichever store sold it, falling back to the primary's route, because a tile offering the primary's route while the other store holds the installed copy names an action it cannot perform.

THE HEADLINE, THROUGH 70.4'S FACTORY AND NOT A LOCAL SUM. New IPlayedEntry (PlaytimeMinutes + LastPlayedAt) in Winnow.Core.Identity; CoverageEntry, OwnershipBucket and TileEntry all implement it and CoveragePlaytime.Across widened to IEnumerable<IPlayedEntry> -- covariant, so every existing call site compiled untouched. The grid headline, the bucket query's fold and the modal's TOTAL are now literally one function over one set of entries, so they cannot disagree, and the F10 pairing stays inexpressible rather than merely avoided.

THE ONE PLACE THE DESIGN HAD TO CHANGE RATHER THAN EXTEND. A group's bucket cannot be the highest-precedence member bucket: two entries at 60 minutes each are two Active rows and one Bounced game, so the thresholds must be re-applied to the SUM (AC #4). That means the section 6.1 rules evaluate at TWO grains, and two evaluations must not become two implementations. So the CASE moved out of the SQL into pure LibraryBucketRules.Classify in Winnow.Core.Queries, and the query now emits mu.occurred_at as MajorUpdateAt instead of a verdict. Buckets are still derived on read from stored facts and still never a stored column, which is what 6.1 requires -- they derive one step later in the same read. LibraryBucketRules.AddMonths reproduces SQLite's own '+N months' (add to the month field, then NORMALISE the overflow: 2024-03-31 + 6 months = 2024-10-01, where .NET's AddMonths clamps to 2024-09-30) and truncates to whole seconds as datetime() does. The_month_arithmetic_is_the_one_SQLite_applies asks SQLite itself at eight boundary dates. BucketQueryTests, DemoConsolidationQueryTests and NonGameFilterTests passed unchanged, which is the faithfulness evidence.

WHY THE FOLD IS IN THE REPOSITORY, WHICH IS A DEPARTURE FROM THE BRIEF. The brief expected a grouping in the view model. It is in LibraryQueryRepository.Fold, immediately after Consolidate, for a reason discovered rather than assumed: a window function in the SQL would fold rows that Consolidate then DROPS in C# -- a consolidated demo, a hidden non-game, a row removed by the account filter. The grid would then stand behind a sum the details modal refuses to report, which is exactly the contradiction IdentityCoverage.For was built to avoid. Folding after consolidation fixes that; folding in the repository rather than the view model means every consumer of the 70.4 chokepoint inherits ONE answer -- grid, rail counts, All Games, filter options, list counts, the recommender, the feed -- instead of each folding its own. New GameGrouping is a sealed class with a private constructor and one factory, Of(resolvedWorkId, entries, majorUpdateAt, thresholds), for CoveragePlaytime's reason: there is no constructor that pairs a sum with a date it did not derive, or files a game under a bucket its own playtime does not put it in. Every member row holds the SAME instance by reference, so members cannot disagree.

THE CHIP, WHICH IS THE IDIOM THAT ALREADY EXISTED. The outlined store badge was already in five places (hover overlay, back of the card, feed card, details header, list column): 1px Line, radius 3, 5x1 padding, body face 9px, 0.54 tracking, TextDim. It became a shared store-chip class drawn one per store. A SINGLE-STORE TILE IS BYTE-IDENTICAL IN ALL FIVE. Never Volt, never Flare. Two density corrections were made after measuring rather than assumed: at 148px a two-chip row is about 94px of a 128px content width, so beside the stat it wins the Auto column and leaves the playtime with nothing -- the multi-store chips therefore take their own line in the hover overlay, and wrap on the back face, only on the tile that has them. Still one 'where you own it' fact, so 5.3's four-fact cap holds. The list column went 84px to 112px.

THE RESTING MARK, THE ONLY THING ADDED TO THE FRONT OF A TILE. Drawn ONLY when the game is on more than one store, so a library with nothing linked gains no pixels at all. One letter per store on a new TileChipGround field (the theme's own Ground at 82%, added to both tokens.axaml copies and to WinnowTheme, pinned by ThemeContrastTests), bottom-left, fading out over 140ms as the overlay rises exactly as the baked placeholder title does, and snapped away under reduced motion with the same .snap selector as every other tile transition. Initials because the density floor is 108px and word-chips do not fit there; the words are in the row's tooltip, the overlay, the back face, the modal and AutomationName, so the mark is decorative-redundant per section 8 rather than the only place the fact lives.

COUNTS, AND WHY THE TWO READINGS CANNOT CONTRADICT. All Games, the rail buckets, TotalCount and list counts are per TILE on the GAME's bucket. The filter panel's PLATFORM selector returns the tile's whole Stores list, so a twice-owned game counts under both options and is kept by either. TitlesByStore counts TILES THAT INCLUDE THAT STORE -- which is the SAME RELATION the panel's store option counts with nothing else cut, so the Platforms screen and the panel are the same number by construction, asserted option by option in Every_count_on_screen_agrees_with_its_own_definition. IStoreTitleCounts stays unresolved as 70.4 recorded; section 11.2's per-tile rule survives the change of grain rather than contradicting it, and the consequence is asserted rather than left to be found: the per-store figures sum to MORE than All Games by exactly the number of extra store memberships. CountHiddenByAccountScopeAsync now subtracts DISTINCT RESOLVED WORKS rather than rows, because the label says 'N games hidden' and a linked pair whose Steam entry is filtered away loses a chip and not a tile.

FILTERS, FACETS, LISTS. FilterableRow.Store became Stores and the matcher matches ANY. Facets are UNIONED across the tile's entries, because a genre the Steam entry carries and the Epic one does not is still true of the game. A tile is in a list when ANY entry is; adding adds the primary's release, removing removes every entry's. A LATE FIX FOUND BY REVIEW: list order, the move buttons and MoveInListAsync all keyed on the primary release, so a collapsed tile whose list row was recorded against its NON-primary entry sorted to the end of a list it was visibly in and could not be moved. New GameTileViewModel.ReleaseInList / PositionIn answer for the row the list actually holds; covered by A_collapsed_tile_sorts_and_moves_by_the_entry_the_list_holds.

DETAILS AND FEED. The modal reads update events for EVERY entry, merged newest-first against the GAME's last-played, because the badge is the game's bucket computed from the latest patch anywhere in the group -- a primary-only read could show a badge with nothing under it (Steam patched, Epic primary). TileForOwnership and TileForRelease match any entry. The recommender's pool is one candidate per resolved work with its facts taken from the GAME, so the card and the tile agree; ShortlistBoundTests' CandidateCount moved 7 to 4, which is a sharper statement of what that test was always about.

THE WALL. Geometry is a closed form in width and density and needed no change; it was extracted to CoverWall.GeometryFor and ExtentFor so it can be pinned without a window. Verified at 108/148/200 across 1200/1600/1920/3440 plus two degenerate widths: every row fills its width with under a pixel per column of slack and is charged for the gutters BETWEEN its cells only. Collapsing shortens the wall by WHOLE ROWS and nothing else. The lease system is untouched and in fact leases fewer bitmaps, since two tiles that borrowed one primary's cover key are now one tile.

AVALONIA DETAIL VERIFIED AGAINST CURRENT DOCS RATHER THAN MEMORY. DataType='x:String' compiled but is not the documented form for an ItemTemplate over strings and was removed in favour of the plain DataTemplate the docs show. AutomationProperties.Name and WrapPanel.ItemSpacing / LineSpacing were confirmed present.

WHAT THE USER WILL SEE. Every game they have linked on the Same Game screen is now one tile instead of two, under the title they chose, with a chip for each store; the library total drops by one per link while the Platforms screen's numbers do not move. Two 60-minute halves of one game now read as one Bounced game. A game played recently on the second store stops being ghosted. Play launches whichever copy is installed. Nothing changes anywhere in a library with no links.

NEW FILES: src/Winnow.Core/Queries/LibraryBucketRules.cs; src/Winnow.App/ViewModels/TileEntry.cs (TileEntry + StoreNaming); tests/Winnow.Tests/LibraryGrainTests.cs, LibraryBucketRulesTests.cs, TileFixture.cs. CHANGED: Winnow.Core (IdentityCoverage.cs, Buckets.cs, LibraryFilter.cs); Winnow.Data (LibraryQueryRepository.cs); Winnow.Recommend (RecommendationEngine.cs); Winnow.App (GameTileViewModel, LibraryViewModel, GameDetailsViewModel, Filters/FilterPanelViewModel, Views/CoverWall.cs, Views/GameTileView.axaml, MainWindow.axaml, FeedCardView.axaml, GameDetailsView.axaml, Themes/controls.axaml, Themes/tokens.axaml, Themes/WinnowTheme.cs); root tokens.axaml; design-system.md (two recorded amendments, at 5.3 and 11.2, saying the grain changed and how the per-tile count rule survives it); and the test files listed above plus IdentityReadModelTests, IdentityLinkTests, ThemeContrastTests, LibraryFilterTests, GameListTests, FeedViewModelTests, GameDetailsViewModelTests, TileActionsTests, UpdateFlagTests, ShortlistBoundTests, FranchiseAndTasteTests. ALL PROSE IN EVERY ONE OF THEM AUTHORED BY THE docs-writer AGENT.

TWO 70.4 TESTS WERE DELIBERATELY REWRITTEN, and say so. Linking_moves_no_count_and_no_bucket is now Linking_collapses_one_tile_and_leaves_the_store_counts_alone: linking moves no ROW and no row's own figures, takes exactly one tile off the grid, and leaves the per-store counts untouched. Both_store_entries_of_a_linked_game_take_the_primary_title_and_cover is now A_linked_pair_is_one_tile_under_the_primary_title_and_cover. IdentityLinkTests.A_live_link_moves_the_resolved_work_id_and_nothing_else now holds TWO columns aside, Game being the second.

VERBATIM, scoped first: LibraryGrainTests 27/27; LibraryBucketRulesTests 15/15; the grain/rules/identity/theme/filter/list set together 199/199.
Then the FULL suite across all three projects, built and run via --artifacts-path into the scratchpad because the user is holding src/Winnow.App/bin:
  Passed!  - Failed: 0, Passed: 70,   Skipped: 0, Total: 70,   Duration: 1 s   - Winnow.Covers.Tests.dll (net10.0)
  Passed!  - Failed: 0, Passed: 105,  Skipped: 0, Total: 105,  Duration: 55 s  - Winnow.Recommend.Tests.dll (net10.0)
  Passed!  - Failed: 0, Passed: 2578, Skipped: 0, Total: 2578, Duration: 1 m 20 s - Winnow.Tests.dll (net10.0)
Build: 0 Warning(s), 0 Error(s) under TreatWarningsAsErrors.

NOT FINALIZED: acceptance criteria not checked, no final summary, status left In Progress, nothing committed. The live database was never opened and the user's running app was never touched.
<!-- SECTION:NOTES:END -->
