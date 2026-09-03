---
id: TASK-70.10
title: >-
  Ground expansion and variant relations in storefront metadata, demote the
  title heuristic to gap-filler
status: In Progress
assignee:
  - '@claude'
created_date: '2026-09-02 12:37'
updated_date: '2026-09-02 21:00'
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
- [x] #7 The detector never proposes a pair that metadata contradicts: a known parent pointing to a different work refutes the pair, and game_type main_game with null parent_game refutes it
- [ ] #8 Demo, beta and playtest proposals surface as variant_of, never expansion_of
- [ ] #9 The corroboration guard is strengthened beyond "both years are known"; two known years alone no longer satisfy RequireCorroboration
- [ ] #10 The heuristic proposes only where every metadata source (IGDB game_type, Steam store type, steamcmd parent) is silent on both members of the pair
- [ ] #11 No new HTTP requests are required for the Steam half; the IGDB half adds no requests beyond the existing enrichment pass
- [ ] #12 Mods are recorded with their source label but not auto-folded; the open question of grouping a mod under its base game is stated, not decided
- [x] #13 A remake, remaster or port is never offered on the Expansions surface: metadata naming one of those kinds refutes an expansion proposal the same way main_game with a null parent does
- [x] #14 The metadata claim path passes through the same refusal guards as the title heuristic rather than writing straight into the results, so a guard cannot be bypassed by a source naming a kind
- [x] #15 No proposal arrives with its checkbox pre-ticked when the relation the metadata names is not the relation the surface is asking about
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

## expanded_game -> IgdbRebuildTypes (small surgical slice, decision already made)
1. Copy live winnow.db (+ -wal/-shm) read-only to scratch; build a file-based
   C# measurement tool (Winnow.Data + Winnow.Resolve) that runs
   LibraryExpansionScan.ScanAsync over the copy and prints base-game/proposal
   counts. Run it BEFORE the change (HEAD 1a9bc09) for a baseline, and record
   the raw proposal list.
2. Confirm the Witcher pair ("The Witcher: Enhanced Edition" under a second
   work of the identical title) is a same_game candidate independent of this
   change: StorefrontRelation is consumed only by LibraryExpansionScan, never
   by the same-game soft-match sweep, so the ordinary same-game detector's
   answer cannot move. Verify by inspecting merge_candidates / soft-match
   output on the copy.
3. Move "expanded_game" from IgdbTypes to IgdbRebuildTypes in
   src/Winnow.Core/Identity/StorefrontRelation.cs (currently line 160): keeps
   RelationLabels.ExpandedGame, gets Kind: null and RefutesExtension: true,
   same shape as remake/remaster/port. fork stays in IgdbTypes untouched.
   I make the structural code edit; docs-writer authors the split comment
   (the shared expanded_game+fork justification at lines 156-159 no longer
   applies to both), the updated IgdbRebuildTypes block comment, and the
   class-level summary's "remake, remaster, port" mention.
4. Update tests/Winnow.Tests/ExpansionMetadataGuardTests.cs: move the
   "expanded_game" InlineData row out of An_igdb_game_type_maps_to_a_kind_and_a_label
   and into An_igdb_rebuild_type_records_its_word_and_refutes_an_expansion (and
   the wire-spelling equivalents), and add one dedicated pinning Fact using a
   measured real pair (Ori and the Blind Forest / Definitive Edition) showing
   TryPropose refuses with MetadataContradicts. I write the code (attributes,
   assertions); docs-writer writes the XML doc comments.
5. Build and run the full suite via --artifacts-path into the scratchpad.
6. Re-run the measurement tool AFTER the change on the same db copy; report
   base-game/proposal counts before/after and confirm all 22 Expanded Game
   proposals with no genuine expansion are gone while the Witcher same_game
   candidate is unaffected. Delete the db copy afterward. No commit.
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

Verified in the running app on 2026-09-02, first run with migrations 0021/0022 applied to the real library (948 of 1,033 works now carry igdb_game_type, 69 carry igdb_parent_id).

The metadata grounding works and the labels are correct: the Counter-Strike card draws REMAKE on the Counter-Strike: Source row, which is exactly what part two of this task predicted IGDB would say. TASK-71's relation-label fix is confirmed on real data.

What is NOT fixed, and is the user's original complaint: the row is still OFFERED as an expansion of Counter-Strike, with its checkbox pre-ticked and Group as the primary button, under a header reading 'Expansion?'. Correct identification did not change what the surface proposes.

Cause, at src/Winnow.Core/Identity/ExpansionDetector.cs:290. The metadata claim path accepts any Kind except SameGame and writes the proposal directly into best[], bypassing every refusal guard the title-heuristic path runs. RebuildEdition is one of the guards it skips. So metadata naming a kind makes a proposal MORE likely to survive than a title guess, which inverts the intent of demoting the heuristic to gap-filler.

AC #7 does not cover this shape: it refutes a pair where metadata contradicts the parent, or where game_type is main_game with a null parent. A remake has a real parent and a real relation, it is simply not an expansion. AC #8 routes demos and betas to variant_of but says nothing about remake, remaster or port. Three criteria added above to close that gap.

## The last three criteria (AC #13, #14, #15), and AC #7 with them

The complaint was that correct identification did not change what the surface proposed. Three
changes, and a fourth found by measuring.

**AC #13 — a rebuild is never offered here.** IGDB `remake`, `remaster` and `port` move out of
the expansion table in `StorefrontRelation` into `IgdbRebuildTypes`: they keep their label and
now carry `Kind: null` with `RefutesExtension: true`. A rebuild is the same game built again, so
there is nothing to group; it refutes even when it names a real parent, which is the shape
main_game-with-no-parent never covered. `expanded_game` and `fork` deliberately stay
`expansion_of` — both still name something acquired on top of the base, and `fork` sits beside
the mod question AC #12 leaves open. Decided, not derived; stated here rather than buried.

**AC #14 — one gate, not two.** `ExpansionDetector.Detect`'s storefront pass wrote straight into
`best[]`, so a proposal a SOURCE made had passed fewer checks than one a TITLE GUESS made. Both
paths now run `ExpansionDetector.Refuses`: SameWork, the metadata refutation, a named parent that
is not this base, RebuildEdition, and SequelOrdinal (conditioned on the titles actually standing
in a prefix relation). The guards left out are left out for stated reasons in the file:
EmptyTitle, BaseTooShort, NotAPrefix and NoCorroboration all judge whether a PREFIX MATCH is
trustworthy, and a storefront claim does not rest on one (Arma 2: DayZ Mod's parent is Operation
Arrowhead, whose title it does not begin with); MetadataSpeaks is true by construction on that
path; PublisherMismatch and the two year guards are sanity checks on a guess, and a source that
names the parent knows more than a year delta does.

**AC #15 — the checkbox.** `ExpansionMemberViewModel` gains `IsAskedRelation` and pre-ticks only
`expansion_of`. A `variant_of` row is still shown, because that is where the pair was found, and
arrives unticked so G cannot assert a relation the header never asked about.

**AC #7, found while measuring, and it was dead.** `/v4/game_types.type` returns the HUMAN LABEL,
not the documented snake_case id. Measured read-only on the live database 2026-09-02:
`works.igdb_game_type` holds "Main Game" 833, NULL 85, "Standalone Expansion" 23, "Expanded Game"
22, "Remaster" 20, "Bundle" 19, "Remake" 18, "Mod" 4, "Expansion" 4, "Port" 3, "Fork" 1, "DLC" 1.
Single-word names matched the lookup tables by accident; every multi-word one fell through to the
unknown branch. So main_game's refutation had never once fired in production, and 46 works IGDB
types as expansions were being read as silence. `StorefrontRelation.Canonical` now folds spaces
and hyphens to underscores before lookup, and `"dlc"` is a key because IGDB's label for
`dlc_addon` is the bare word "DLC". The raw value still feeds the unrecognised branch. This is the
same failure the task's own diagnosis names: a suite reporting a guard production did not have.

## Measured on the real library, read-only through a copy, 1,003 works compared

- HEAD:               27 base games, 29 proposals, 22 of them remake/remaster/port pre-ticked as
                      expansions (Counter-Strike: Source, Black Mesa, Skyrim Special Edition,
                      BioShock Remastered, Half-Life: Source, XCOM: Enemy Unknown, Darksiders
                      Warmastered, Day of Defeat: Source, and fourteen more).
- rebuild guard only:  6 base games,  7 proposals. All 22 rebuilds gone.
- with the wire-spelling fix: 19 base games, 23 proposals, still ZERO rebuilds, and the genuine
  expansions this task was written about appear for the first time: Half-Life: Opposing Force and
  Blue Shift, Don't Starve Together, Prey: Typhon Hunter, Company of Heroes: Opposing Fronts and
  Tales of Valor, MGSV: Ground Zeroes, Subnautica: Below Zero, Wolfenstein: The Old Blood, Alan
  Wake's American Nightmare, Jedi Knight: Mysteries of the Sith, and Dishonored: Death of the
  Outsider under DISHONORED 2, which is the parent the title heuristic got wrong.

Two rows worth a later look, neither caused here: "Alan Wake's American Nightmare" appears twice
(two unlinked works share the title), and "The Witcher: Enhanced Edition" proposes under a second
work of the same name, which is a same_game question wearing an Expanded Game label.

## Files and verification

src/Winnow.Core/Identity/StorefrontRelation.cs, ExpansionDetector.cs, IdentityConstants.cs;
src/Winnow.App/ViewModels/ExpansionMemberViewModel.cs;
tests/Winnow.Tests/ExpansionMetadataGuardTests.cs, MergeQueueViewModelTests.cs.

Prose by docs-writer, every marker cleared. No migration: the refutation is derived, and no new
refusal reason was needed (MetadataContradicts covers the rebuild shape).

  dotnet build --artifacts-path <scratchpad> -v q   Build succeeded. 0 Warning(s) 0 Error(s)
  dotnet test  --artifacts-path <scratchpad>
    Passed! - Failed: 0, Passed:   70, Total:   70 - Winnow.Covers.Tests.dll
    Passed! - Failed: 0, Passed:  115, Total:  115 - Winnow.Recommend.Tests.dll
    Passed! - Failed: 0, Passed: 2712, Total: 2712 - Winnow.Tests.dll
  Winnow.Tests was 2700 before this slice; +12, none removed.

Build and test ran in a scratch git worktree, because concurrent work in src/Winnow.Recommend
does not compile in the shared tree; the worktree carried HEAD plus these files. The live database
was read once, read-only, through a copy that has since been deleted. No live API call. No commit.

## expanded_game moved to IgdbRebuildTypes (2026-09-02)

IGDB's `expanded_game` game_type moved from the `IgdbTypes` dictionary to the `IgdbRebuildTypes` dictionary in `StorefrontRelation.cs`. It now carries `Kind: null` and `RefutesExtension: true`, the same shape as remake/remaster/port, while keeping its own label (`RelationLabels.ExpandedGame`, "expanded game").

Reasoning, measured read-only against the real 1,033-work library:
- 22 works are typed "Expanded Game" by IGDB.
- Only 5 of those 22 have a base game the user also owns: Ori and the Blind Forest: Definitive Edition, Q.U.B.E: Director's Cut, Guacamelee! Super Turbo Championship Edition, Divinity: Original Sin Enhanced Edition, The Witcher: Enhanced Edition (under a second work of the identical name, a different work id).
- None of the five is a genuine expansion. An edition is not something bought on top of a base game; it is the base game again. Calling the pair the same game is refused by the hard constraint against collapsing Release into Work (Skyrim SE is not Skyrim), and a Definitive/Enhanced/Director's Cut edition is exactly that shape. The pair is neither expansion_of nor same_game, and the code says nothing rather than say the wrong thing.

## fork deliberately left alone

`fork` stays in `IgdbTypes` with `expansion_of` and its own label. It still names something acquired on top of the base. It rides beside the mod-grouping question TASK-70.10 AC #12 deliberately leaves open, and it covers exactly one work in the measured library, unlike expanded_game's 22. Decided separately, not swept in.

## The Witcher same_game candidacy is unaffected

The Witcher pair (two distinct works, identical title, different stores) is a genuine same_game question. `merge_candidates` already holds a pending row (score 0.70, band "Review") pairing the two Witcher works' releases, produced entirely by the soft-match signal pipeline, which never reads `StorefrontRelation` at all. This change only stops the Expansions surface from mislabelling the same pair as "Expanded Game"; the same_game detector and its row are untouched.

## Measured before/after on the real library

Read-only through a database copy (since deleted). LibraryExpansionScan's admitted proposal set:
- Before: 19 base games, 23 proposals (including three expanded_game-labelled rows: Guacamelee! Gold Edition <= Guacamelee! Super Turbo Championship Edition; Q.U.B.E. <= Q.U.B.E: Director's Cut; The Witcher: Enhanced Edition <= The Witcher: Enhanced Edition [different work]).
- After: 16 base games, 20 proposals. Exactly those three gone, nothing else moved.

## Files touched

- `src/Winnow.Core/Identity/StorefrontRelation.cs` -- expanded_game entry moved from IgdbTypes to IgdbRebuildTypes; comments rewritten on fork entry, IgdbRebuildTypes dictionary, and class-level summary.
- `tests/Winnow.Tests/ExpansionMetadataGuardTests.cs` -- expanded_game InlineData row added to the rebuild theory; new pinning Fact added; doc comments rewritten on both.

## Build and test

Build succeeded, 0 warnings, 0 errors. Full suite green:
- Winnow.Covers.Tests: 70/70
- Winnow.Recommend.Tests: 145/145
- Winnow.Tests: 2713/2713

Baseline was 2927 total across all three projects before this slice. +1 net test (two InlineData rows moved between existing theories with no net count change, plus one new pinning Fact). 2928 total now. All run via `--artifacts-path` into the scratchpad. No commit. Live database never opened or written; read once through a copy since deleted.

## New test

`Expanded_game_refutes_on_a_measured_owned_base_pair` in `ExpansionMetadataGuardTests.cs`. Pins the decision on the Ori and the Blind Forest: Definitive Edition pair, one of the five measured owned-base cases.
<!-- SECTION:NOTES:END -->
