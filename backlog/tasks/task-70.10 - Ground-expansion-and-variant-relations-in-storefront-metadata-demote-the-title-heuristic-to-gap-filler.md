---
id: TASK-70.10
title: >-
  Ground expansion and variant relations in storefront metadata, demote the
  title heuristic to gap-filler
status: In Progress
assignee:
  - '@claude'
created_date: '2026-09-02 12:37'
updated_date: '2026-09-02 13:40'
labels: []
dependencies:
  - TASK-18
parent_task_id: TASK-70
priority: high
type: enhancement
ordinal: 97000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Follow-up to TASK-70.5. The title-prefix expansion detector shipped in that task produces 38 proposals on a real library of 1,033 works (952 Steam, 67 Epic, 14 GOG). Roughly 11 are genuine expansions. The rest are sequels, rebuilds, unrelated games sharing a first word, and demos. Every false positive traces to a structural limit of the heuristic, and every one of the four storefronts Winnow already reads hands it the authoritative answer, which Winnow currently parses and discards.

## Part one: why the false positives get through

Measured against the user's live database, read-only.

**The ordinal guard works and roman numerals are not the leak.** `TitleNormalizer.FoldRomanNumerals` folds IV/V/VI/II to digits, so "Civilization IV" and "Quake II" are correctly refused as SequelOrdinal. The only roman-numeral gap is narrow: bare "x" is never folded (deliberate, it is a name more often than "10"), and `MaxRomanOrdinal` is 30, so anything above XXX is unfolded. Neither shape occurs in this library.

**The real leak is the word-suffixed sequel, which an ordinal guard cannot see by construction.** A guard that tests whether the suffix opens on a NUMBER cannot refuse a sequel whose suffix is a WORD. Measured proposals: "DOOM" to "DOOM Eternal", "BioShock" to "BioShock Infinite", "Magicka" to "Magicka: Wizard Wars", "Duke Nukem" to "Duke Nukem: Manhattan Project", "Liftoff" to "Liftoff: Micro Drones", "Counter-Strike" to "Counter-Strike: Source", "Worlds Adrift" to "Worlds Adrift Island Creator", "Sid Meier's Ace Patrol" to "Sid Meier's Ace Patrol: Pacific Skies". Synthetic confirmations: "Fallout" to "Fallout: New Vegas", "Deus Ex" to "Deus Ex: Human Revolution", "Portal" to "Portal Stories: Mel" all propose.

**The corroboration guard is a near no-op on an enriched library, and its test hides that.** `RequireCorroboration` is satisfied when `yearDelta is not null`, which means merely when BOTH years are known. 947 of 1,033 works (91.7%) carry `first_release_year`, so the guard almost never fires. The pinned test `Two_unrelated_games_sharing_a_first_word_are_not_proposed` passes `year: null, publisher: null`, which is the one shape where the guard activates; the suite reports a guard that production does not have. Consequence: "INSIDE" to "Inside the Backrooms", two completely unrelated games, proposed because both years are known. That is precisely the "Rush" / "Rush Bros" coincidence the guard was written to stop.

**The rebuild guard is bypassed by the generic edition phrase.** `TitleNormalizer` lifts a trailing bare "edition" as a BUNDLE marker, not a REBUILD marker, so "The Outer Worlds: Spacer's Choice Edition" reduces to suffix "spacers choice" and proposes as an expansion. IGDB types it Remaster. "Hellblade: Senua's Sacrifice VR Edition" lands the same way with suffix "vr"; IGDB types it Port.

**Even when the detector is right that a relation exists, it routinely picks the wrong parent,** because longest-owned-prefix-wins is not the same question as who the parent is. Verified against IGDB: "Dishonored: Death of the Outsider" is a Standalone Expansion of Dishonored 2, not Dishonored, which is where it was filed. "Counter-Strike: Condition Zero Deleted Scenes" is a Standalone Expansion of Condition Zero, not Counter-Strike. "Arma 2: DayZ Mod" is a Mod of Operation Arrowhead, not Arma 2. "Sid Meier's Civilization IV: Colonization" is a Remake of Sid Meier's Colonization, not an expansion of Civilization IV at all.

**The demo, beta and playtest proposals.** Eleven of the 38, including "Civilization V: Demo", "Midnight Ghost Hunt - Beta Test", "Fallout 76 Public Test Server", "Rust - Staging Branch", "Starbound - Unstable", "Satisfactory Experimental", "Miscreated: Experimental Server", "Rainbow Six Siege - Test Server", "Barony (Beta)". `DemoConsolidation` already hides these from library buckets at read time, but `LibraryExpansionScan` reads `IReleaseRepository.GetIdentitiesAsync`, which does not apply that suppression. The same rows the library hides, the expansion queue offers, under the wrong word.

## Part two: what the storefronts already provide, and what Winnow discards

**IGDB.** `games.category` is deprecated in favour of `game_type`, a reference to the `/v4/game_types` endpoint whose label field is `type`. Fifteen type names: main_game, dlc_addon, expansion, bundle, standalone_expansion, mod, episode, season, remake, remaster, expanded_game, port, fork, pack, update. Relationship fields: `parent_game` (the main game when DLC, expansion or part of a bundle), `version_parent` with `version_title`, and downward arrays (`dlcs`, `expansions`, `standalone_expansions`, `expanded_games`, `ports`, `forks`, `remakes`, `remasters`, `bundles`). IGDB does NOT model demos, betas or playtests; alpha/beta/early_access are a release `game_status`, not entities.

What Winnow requests today: `Apicalypse.Games` asks for `name, summary, first_release_date, cover, genres, themes, game_modes, player_perspectives, involved_companies`. It asks for NONE of `game_type`, `parent_game`, `version_parent`. `IgdbGameDto` and `IgdbGame` have no field for any of them.

Verified live: one Apicalypse POST using the token already cached in the user's settings table returned `game_type` and `parent_game` for all 54 works involved in the 38 proposals. It classified every one correctly: Standalone Expansion for Opposing Force, Operation Arrowhead, Blood Dragon, Perseus Mandate, Don't Starve Together; Expansion for Civ IV Warlords and Beyond the Sword; DLC for Prey Typhon Hunter; Remake for Counter-Strike: Source; Remaster for Spacer's Choice Edition; Port for Hellblade VR; Mod for DayZ Mod; and Main Game with no parent for DOOM Eternal, BioShock Infinite, Inside the Backrooms, Liftoff Micro Drones, Magicka Wizard Wars, Duke Nukem Manhattan Project, Ace Patrol Pacific Skies, Borderlands The Pre-Sequel and Worlds Adrift Island Creator. Every sequel false positive, refuted.

**Steam.** `SteamStoreClient` calls `IStoreBrowseService/GetItems` and caches the body verbatim in `metadata_cache`; 954 such bodies already exist on the user's disk. Those bodies carry a numeric `StoreItem.type` and a `related_items` block. Valve publishes no name table for the type enum; these are observed constants confirmed across the user's cache and live probes: 0 = game, 1 = demo, 2 = mod, 4 = DLC, 6 = application, 10 = hardware, 11 = music, 12 = beta/playtest. Type 14 appears 12 times in the cache and only ever on delisted or superseded apps (Q.U.B.E., Mortal Kombat Kollection, Darksiders II, F.E.A.R.: Extraction Point, the retail-era Civilization IV appids); treat it as "retired", not as a relation.

`related_items` is bidirectional. Valve's own `webui/common.proto` (SteamDatabase/Protobufs) defines `StoreItem_RelatedItems` with `parent_appid`, `demo_appid[]`, `standalone_demo_appid[]`, `demos[]` (appid, label, show_above_purchase), `standalone_demos[]`, `playtests[]` (appid, is_open), `related_f2p` and `dlc_parent_appids[]`. A base game names its own demos and playtests; a child names its parent. `related_items` has no `include_` flag in the request and is returned unconditionally. The user's cache already holds all of it: 104 bodies with `demos`, 18 with `standalone_demos`, 2 with `playtests`, 49 with `parent_appid`. `SteamStoreItem` and `SteamStoreJson` parse appid, name, tags and categories and read NONE of these fields. The data needs no new HTTP request.

Taking the union of upward and downward pointers already cached, 27 distinct child-to-parent WORK pairs are recoverable today with zero new HTTP requests: 12 demos, 5 betas/playtests, 2 mods, 1 DLC, and 7 retired-app pointers.

**The meaning of `parent_appid` depends on the type.** On a type 1 or 12 it names the game the sample or test build belongs to, which is `variant_of`. On a type 14 it names the app that superseded this one, and three of the user's pairs (the retail Civilization IV, Warlords and Beyond the Sword appids) point at works with the SAME title, which is a `same_game` claim and not a child relation at all. The kind must be derived from the type, never from the mere presence of a parent pointer.

**Steam gets a parent right that the detector got wrong.** The cache says "Counter-Strike: Condition Zero Deleted Scenes" has parent appid 80, which is Condition Zero, the same answer IGDB gives, not the Counter-Strike the detector filed it under. Likewise "Arma 2: DayZ Mod" parents to Operation Arrowhead in both sources.

The second Steam source, `SteamCmdBuildInfoClient` over steamcmd.net PICS, already PARSES `common.parent` into `SteamAppInfo.ParentAppId` and there is a test asserting it. `EnrichmentSyncService` reads only `Info.Type` and `Info.Name` and drops the parent on the floor.

**Why `appdetails` stays out of this, stated positively.** It is the weaker endpoint, not merely the rate-limited one: it cannot see playtests at all (a playtest appid answers `success: true`, `type: "game"`, `fullgame: null`), and it loses demo parentage whenever the demo-role app is typed `game`. `IStoreBrowseService/GetItems` is keyless, batched, and returns the parent in every one of those cases. Winnow's standing rule against `appdetails` costs it nothing here.

Correction to migration 0006: that migration records that "Valve has no beta/playtest type". That is no longer true. The user's database holds three works whose `steam_app_type` is literally `Beta`, and the store cache types seven apps as 12 with a parent appid. The signal exists now.

What Steam will NOT give: expansions. Every genuine standalone expansion in the library (Opposing Force, Operation Arrowhead, Blood Dragon, Civ IV Warlords, Death of the Outsider) is type 0 with no parent appid. Steam is authoritative for demos, betas, playtests, mods and tools, and silent on expansions. IGDB is the reverse. They are complementary, not redundant.

**Epic.** Local `.item` manifests carry `MainGameCatalogNamespace`, `MainGameCatalogItemId` and `MainGameAppName`, empty on a base game and populated on an add-on. `EpicCatalogReader` already reads the first two into `EpicCatalogEntry`. Nothing stores either value. That is why Epic DLC never reaches the database at all, which is a separate question from this task.

**GOG.** Local `goggame-<id>.info` files carry `rootGameId`, which `GogGameInfoReader` already reads into `GogGameInfo`; `IsDlc` is `GameId != RootGameId`. GOG's public API v2 (`api.gog.com/v2/games/<id>`) additionally exposes `_embedded.productType` and `_links.requiresGames` / `isRequiredByGames`, which API v1 does not (v1 has no parent pointer at all). Nothing stores any of these values. GOG DLC, like Epic DLC, never reaches the database.

**Coverage, measured.** 918 of 1,033 works carry an `igdb_id` (88.9%); 947 carry a year; only 50 carry a `steam_app_type`, because `EnrichmentSyncService` asks steamcmd for a type ONLY when `DemoConsolidation.IsVariantTitle` already says the title looks like a variant. The storefront fact is gated behind the title heuristic it was meant to replace. The 115 works with no IGDB id are dominated by exactly the demos and betas IGDB refuses to model, which is the complementarity showing up as data.

## Part three: the recommendation

**Kinds are defined by the numbers they change; labels are vocabulary.** `identity_links.kind` carries a CHECK constraint `IN ('same_game', 'expansion_of')` (migration 0018), and a new kind costs a table rebuild because SQLite cannot alter a CHECK. That is an argument for few kinds with a separate label, not many kinds.

**Three kinds, three behaviours.**

- `same_game`, unchanged. One game sold twice. Rolls up.
- `expansion_of`, semantics unchanged from TASK-70.5. A product you bought that depends on a base: IGDB dlc_addon, expansion, standalone_expansion, episode, season, pack. Counts as a title, playtime does not roll up. What changes is that membership becomes true.
- `variant_of`, new. A sample or test build you were handed: Steam type 1 and type 12, PICS Demo and Beta, staging and experimental branches. Does not count as a title while its parent is owned; counts when it is the only thing you have, which is exactly `DemoConsolidation`'s existing read-time rule made into a stored fact with a storefront source behind it. Playtime never rolls up, but the demo's own hours stay visible on the parent's modal, because "you played forty minutes of the demo and never bought it" is the app's premise, not noise.

**Editions (remaster, remake, port, fork) are numerically identical to `expansion_of` and semantically not expansions.** They take the same kind and a different label rather than a fourth kind; a new `relation_label` (or the existing `evidence_json`) carries the source's own word (expansion, dlc, standalone expansion, episode, season, remaster, remake, port, mod, demo, beta, playtest) so the card says the true word without a migration per vocabulary item. IGDB has 15 type names today and will add more.

**Mods are left as separate titles** and flagged as an open question, not folded in. Enderal and tModLoader are games you play, not add-ons; DayZ Mod under Operation Arrowhead is a grouping the user might want. Recording the signal is cheap; deciding is not this task.

## Migration path

**Available today with zero new HTTP requests: the Steam half.** 954 GetItems bodies are already cached. Parsing `type` and `related_items` (both upward `parent_appid` and downward `demos`, `playtests`) in `SteamStoreJson` and re-projecting the cache yields 27 distinct child-to-parent work pairs: 12 demos, 5 betas/playtests, 2 mods, 1 DLC and 7 retired-app pointers. The steamcmd `ParentAppId` is already parsed and only needs storing.

**One enrichment pass for the IGDB half, and it costs almost nothing.** An Apicalypse `fields` clause is one request whatever it lists, so adding `game_type.type`, `parent_game`, `version_parent`, `version_title` to `Apicalypse.Games` adds no requests. The 1,923 cached IGDB payloads were written without those fields, so the cache must be versioned and re-fetched, which is what TASK-18 (Add payload version to the IGDB metadata cache) exists to make possible. 918 ids at 500 per request is two requests.

**What metadata will not cover, so the heuristic stays.** Seven of the 38 are Steam apps that answer `success: 0` from GetItems because they are delisted or hidden (Starbound - Unstable, Rust - Staging Branch, Miscreated: Experimental Server, Rainbow Six Siege - Test Server, Arma 2 Operation Arrowhead Beta (Obsolete), Magicka: Wizard Wars, Duke Nukem: Manhattan Project). Staging and experimental branch apps carry no parent pointer from any source. Non-Steam works with no IGDB id (Barony (Beta), Satisfactory Experimental, Totally Reliable Delivery Service Beta) have no authoritative answer at all.

**The heuristic's job changes from classifier to gap-filler.** It must never propose a pair that metadata contradicts: a known parent that is a different work refutes the pair outright, and `game_type` main_game with a null `parent_game` refutes it too. On this library that alone kills nine of the sequel false positives. It proposes only where every source is silent. When it does propose and the title carries a `DemoConsolidation` marker, it proposes `variant_of`, never `expansion_of`, so even the fallback path stops calling a demo an expansion. The SequelOrdinal guard stays. The corroboration rule must be strengthened, because two known years is not evidence of anything.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Apicalypse.Games requests game_type.type, parent_game and version_parent; IgdbGameDto and IgdbGame carry all three fields
- [ ] #2 SteamStoreJson parses type and related_items.parent_appid from cached GetItems bodies; SteamStoreItem exposes both
- [ ] #3 The steamcmd ParentAppId already parsed in SteamAppInfo is stored and available to the relation pipeline
- [ ] #4 identity_links.kind accepts variant_of alongside same_game and expansion_of
- [ ] #5 A variant_of link does not count as a title while its parent is owned, counts when it is the only thing owned, and never rolls up playtime
- [ ] #6 A relation_label or evidence_json entry carries the source's vocabulary word so the card shows the true type without a migration per label
- [ ] #7 The detector never proposes a pair that metadata contradicts: a known parent pointing to a different work refutes the pair, and game_type main_game with null parent_game refutes it
- [ ] #8 Demo, beta and playtest proposals surface as variant_of, never expansion_of
- [ ] #9 The corroboration guard is strengthened beyond "both years are known"; two known years alone no longer satisfy RequireCorroboration
- [ ] #10 The heuristic proposes only where every metadata source (IGDB game_type, Steam store type, steamcmd parent) is silent on both members of the pair
- [ ] #11 No new HTTP requests are required for the Steam half; the IGDB half adds no requests beyond the existing enrichment pass
- [ ] #12 Mods are recorded with their source label but not auto-folded; the open question of grouping a mod under its base game is stated, not decided
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Ground the relation in storefront metadata; the title heuristic becomes a gap-filler.
Delivery order is the diagnosis's own: Steam first (no network), then IGDB, then the
kinds, then the heuristic, then the LibraryExpansionScan defect.

## 1. Steam half — zero new HTTP requests
1.1 SteamStoreItem gains StoreType (int?, Valve's numeric StoreItem.type) and
    Related (SteamStoreRelatedItems: ParentAppId, DemoAppIds, StandaloneDemoAppIds,
    PlaytestAppIds, DlcParentAppIds). Init properties, so the 954 cached bodies
    project unchanged.
1.2 SteamStoreJson reads `type` and the `related_items` block per Valve's
    webui/common.proto. Both arrive unconditionally; no data_request flag, no
    new request.
1.3 ISteamStoreClient gains GetCachedItemsAsync: projects metadata_cache rows and
    touches the network on no path. This is what makes the Steam half free.
1.4 SteamAppInfo.ParentAppId (already parsed by SteamCmdBuildInfoClient, dropped by
    EnrichmentSyncService) is carried into the write path.

## 2. IGDB half — no extra requests
2.1 Apicalypse.Games adds game_type.type, parent_game, version_parent, version_title.
    A fields clause costs the same whatever it lists.
2.2 IgdbGameDto/IgdbGame carry GameType, ParentGameId, VersionParentId, VersionTitle.
    game_type's label field is `type`, not `name`; `category` is deprecated and is
    not requested.
2.3 The 1,923 cached payloads predate the fields, so the game cache gets a payload
    version and a mismatch refetches. Narrow slice of TASK-18 (games only), taken
    because this task cannot see its own new fields without it; TASK-18's
    external_games half stays open.

## 3. The kinds
3.1 Migration 0021 rebuilds identity_links: kind CHECK gains variant_of, and a new
    relation_label TEXT carries the source's own word. SQLite cannot alter a CHECK,
    so a rebuild is the only path; append-only, 0018 is untouched.
3.2 Migration 0022 adds the storefront relation facts to works, beside the
    steam_app_type 0006 already established: steam_store_type, steam_parent_app_id,
    igdb_game_type, igdb_parent_id, igdb_version_parent_id. Raw observed facts only;
    the kind is DERIVED, never stored, because parent_appid's meaning depends on the
    type.
3.3 Core gains IdentityLinkKinds.VariantOf, a RelationLabels vocabulary, and
    StorefrontRelation — the mapping from (source, type, parent) to (kind, label).
    Steam type 1/12 and PICS Demo/Beta -> variant_of; type 4 -> expansion_of;
    type 14 -> same_game (it names the app that REPLACED this one, not a child);
    type 2 -> label only, no kind (mods are not folded). IGDB dlc_addon, expansion,
    standalone_expansion, episode, season, pack -> expansion_of; remaster, remake,
    port, fork, expanded_game -> expansion_of with their own label; mod, bundle,
    update -> label only; main_game with a null parent -> a REFUTATION.
3.4 IdentityResolution gains VariantGrouping. variant_of does not count as a title
    while its parent is owned, counts when it is the only thing owned, and never
    rolls up playtime — DemoConsolidation's read-time rule made a stored fact.
    Applied inside LibraryQueryRepository.QueryAsync, which is already the
    RESOLVE chokepoint, so no new reader joins the inventory.

## 4. The heuristic becomes a gap-filler
4.1 ExpansionSubject carries the storefront facts. New refusals:
    MetadataContradicts (a known parent pointing elsewhere; main_game with a null
    parent) and MetadataSpeaks (propose only where every source is silent on BOTH
    members).
4.2 A DemoConsolidation marker makes the proposal variant_of, never expansion_of.
4.3 The corroboration guard stops accepting "both years are known": a year pair
    only corroborates when the delta is plausible AND something else agrees.
    Two known years alone no longer satisfy it, and the pinned test stops passing
    nulls — the one shape where the old guard fired.

## 5. The scan defect
LibraryExpansionScan reads GetIdentitiesAsync, which does not apply
DemoConsolidation, so the queue offers the rows the library hides. ReleaseIdentity
gains IsOwned and the scan runs the same consolidation the bucket query runs.

## 6. Tests, then the full suite
Each of the four false-positive mechanisms refused by name using the diagnosis's own
examples; the corroboration test stops passing nulls; Steam related_items yields the
pairs from cached bodies with no network; the type-14 parent reads as same_game;
IGDB game_type maps to kind and label; a demo does not count as a title while its
parent is owned and does when alone; the heuristic proposes nothing where metadata
speaks. Scoped tests, then the full suite. Build/test via --artifacts-path.
Prose via docs-writer. No commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Steam half, IGDB half, kinds and read-model rule implemented; full suite green before new tests are added (2570 + 107 + 70 passed). Sources now contributing: SteamStoreJson reads StoreItem.type and the whole related_items block (parent_appid, demos, standalone_demos, playtests, dlc_parent_appids) from bodies already in metadata_cache, via a new ISteamStoreClient.GetCachedItemsAsync that touches the network on no path; SteamCmdBuildInfoClient's ParentAppId, previously dropped by EnrichmentSyncService, is now stored; Apicalypse.Games requests game_type.type, parent_game, version_parent and version_title on the fields clause it was already sending. Migration 0021 rebuilds identity_links for variant_of plus relation_label; 0022 adds steam_store_type, steam_parent_app_id, igdb_game_type, igdb_parent_id and igdb_version_parent_id to works. The kind is DERIVED in Winnow.Core.Identity.StorefrontRelation and never stored, because parent_appid's meaning depends on the type and a type 14 parent is a same_game claim rather than a child relation. Three collateral test fixes were needed and each is a real consequence: DatabaseBackupTests seeded works before a rewind that now drops and rebuilds that table because 0022 alters it; two fully-enriched-work tests had to learn that knowing IGDB's game_type is part of being enriched. The IGDB game cache gained a payload version (a narrow slice of TASK-18, games only) with a graceful fallback: a version mismatch asks for a refetch and still serves the superseded payload when no refetch is possible, so an offline install with no Twitch credentials does not lose 1,923 entries.

Tests added, all green before the prose pass. New files: tests/Winnow.Tests/ExpansionMetadataGuardTests.cs (43 cases: each of the four false-positive mechanisms refused by the diagnosis's own examples, the gap-filler rule, the whole IGDB game_type vocabulary, main_game refuting only when it names no parent, an unknown IGDB type recorded and claiming nothing, Steam's variant types outranking IGDB, and the PICS type supplying the variant when the store is silent); tests/Winnow.Tests/SteamStore/SteamStoreRelatedItemsTests.cs (5 cases over a new fixture of thirteen store items lifted verbatim from the author's own metadata_cache, with a handler that throws on any request so a network read is a visible failure); tests/Winnow.Tests/VariantLinkTests.cs (5 cases over a real database: a variant stops counting as a title only while its parent is owned, playtime does not roll up, the label round-trips, an expansion still counts where a variant does not, and the scan no longer offers rows the library hides); tests/Winnow.Tests/Igdb/IgdbRelationFieldTests.cs (7 cases: the query asks for game_type.type and not the deprecated category, a reference field reads as an id in both encodings, and the payload version refetches while still serving a superseded payload when no refetch is possible); two migration tests pinning 0021's rebuild and 0022's columns; three EnrichmentSyncService tests pinning the stored relation facts. The pinned corroboration test in ExpansionDetectorTests now passes two KNOWN years, the shape production actually has, instead of the nulls that were the one shape where the old guard fired.

Measured against the user's live database, read-only, on 2026-09-02, confirming the diagnosis exactly: 905 cached store bodies carry types 0 (841), 1 (31), 2 (5), 4 (1), 6 (8), 12 (7) and 14 (12); 49 carry parent_appid, 104 demos, 18 standalone_demos, 2 playtests. Re-projecting them yields exactly 27 distinct child-to-parent WORK pairs -- 12 demos, 5 playtests, 2 mods, 1 DLC and 7 retired-app pointers -- with no HTTP request. Two apps name THEMSELVES as parent (3900 Civilization IV, 6980 Thief: Deadly Shadows); that is not a relation and is now dropped at parse time, because letting it through would give those works a storefront opinion about their own relations, which is the one thing that silences the title heuristic.

Open question for a later task, observed while measuring: Steam types F.E.A.R.: Extraction Point and F.E.A.R.: Perseus Mandate as 14 with parent F.E.A.R., so they arrive as same_game claims when they are really standalone expansions. The type-14 rule is the diagnosis's and is kept; the claim is a proposal the user answers, so a wrong claim costs a rejected card rather than a bad link. IGDB types both correctly and its claim is the one that will win once igdb_game_type is backfilled.

Prose delegated to three docs-writer agents (src/Winnow.Core + src/Winnow.Data, the Enrich/App/Resolve modules, and the tests plus the fixture README), 148 markers across 33 files, all cleared. A new section in tests/fixtures/steam-store/README.md documents getitems-related-v1.json appid by appid.

Verbatim results, after the prose pass.
  dotnet build --artifacts-path <scratchpad>/art -v q
    Build succeeded. 0 Warning(s) 0 Error(s)
  dotnet test --artifacts-path <scratchpad>/art
    Passed! - Failed: 0, Passed:   70, Skipped: 0, Total:   70 - Winnow.Covers.Tests.dll
    Passed! - Failed: 0, Passed:  107, Skipped: 0, Total:  107 - Winnow.Recommend.Tests.dll
    Passed! - Failed: 0, Passed: 2635, Skipped: 0, Total: 2635 - Winnow.Tests.dll
  Baseline before this task was 2747 total; 2812 now, 65 added and none removed.

No commit. Not finalized. The user's app was never touched; every build and test ran through --artifacts-path into the scratchpad, and the live database was read once, read-only, through a copy that has since been deleted. No live API call was made at any point: the Steam evidence came out of the user's own metadata_cache and the IGDB evidence out of the diagnosis. Two view-model surfaces were deliberately left alone for the agents working on the Same Game screen; the card contract they need is ExpansionProposalMember.RelationLabel (string?, one of RelationLabels) beside ExpansionProposalMember.Kind and .FromMetadata.
<!-- SECTION:NOTES:END -->
