-- 0022_storefront_relations.sql — the storefront's own answer to "what kind of
-- thing is this, and what is it part of".
-- Append-only: never edit this file once shipped; add 0023_*.sql instead.
--
-- ── What these columns are ───────────────────────────────────────────────
--
-- Five additive columns on works, beside the steam_app_type column 0006
-- added for the same reason: they store an observed external FACT, never a
-- decision. No CHECK on any of them; the vocabularies are Valve's and
-- IGDB's, and a constraint would turn a new type name into a failed
-- enrichment write.
--
-- ── Why the kind is derived, never stored ────────────────────────────────
--
-- Nothing in this table is a relation. The kind is derived in code because
-- parent_appid's meaning DEPENDS ON THE TYPE. On type 1 (demo) or 12
-- (beta/playtest) it names the game the sample belongs to, a variant_of
-- claim. On type 4 it names the base game of a DLC, an expansion_of claim.
-- On type 14 (retired) it names the app that REPLACED this one, a
-- same_game claim and not a child relation at all. Three of the author's
-- pairs are exactly that shape: the retail-era Civilization IV, Warlords
-- and Beyond the Sword appids, which point at works with the SAME TITLE.
-- Storing a derived kind would freeze that mapping into the database; the
-- mapping belongs in Winnow.Core.Identity.StorefrontRelation, where
-- correcting it is a code change rather than a migration.
--
-- ── Column descriptions ─────────────────────────────────────────────────
--
-- steam_store_type: StoreItem.type from IStoreBrowseService/GetItems.
-- INTEGER. Valve publishes no name table; observed constants: 0 game,
-- 1 demo, 2 mod, 4 DLC, 6 application, 10 hardware, 11 music,
-- 12 beta/playtest, 14 retired. NULL is "not known", and 0 is a real value
-- meaning game.
--
-- steam_parent_app_id: related_items.parent_appid, or common.parent from
-- the steamcmd PICS mirror when the store said nothing. TEXT, matching the
-- encoding external_ids.provider_id uses for a Steam appid, so resolving
-- the parent to a work is a plain join rather than a cast. related_items
-- arrives with no include_ flag, so this value is already present in every
-- GetItems body the cache holds; 49 of the author's 954 carry a
-- parent_appid, and re-projecting them costs no HTTP request.
--
-- igdb_game_type: the games.game_type label. IGDB deprecated `category`
-- in favour of `game_type`, a reference to /v4/game_types whose label
-- field is `type`, not `name`. Fifteen values today: main_game, dlc_addon,
-- expansion, bundle, standalone_expansion, mod, episode, season, remake,
-- remaster, expanded_game, port, fork, pack, update.
--
-- igdb_parent_id and igdb_version_parent_id: games.parent_game (the main
-- game when this is DLC, an expansion or part of a bundle) and
-- games.version_parent (where a remaster, remake or port names its
-- original). Two columns rather than one because they are two different
-- claims, and the reader that folds them into one parent should be the one
-- deciding which wins.
--
-- ── Refutation ──────────────────────────────────────────────────────────
--
-- These columns are the refutation half as much as the assertion half. A
-- game_type of main_game with a null parent refutes any proposal that this
-- work extends another, and a known parent pointing at a different work
-- refutes it outright. On the author's 1,033-work library that alone kills
-- nine of the sequel false positives the title heuristic produced.
--
-- ── Complementarity, stated so nobody looks ─────────────────────────────
--
-- Steam is authoritative for demos, betas, playtests, mods and tools and
-- SILENT on expansions: every genuine standalone expansion in the library
-- is type 0 with no parent. IGDB is the reverse: it models expansions
-- precisely and does not model demos, betas or playtests at all
-- (alpha/beta/early_access are a release game_status, not entities). They
-- are complementary, not redundant, which is why both sets of columns
-- exist.
--
-- ── Correction to 0006, on the record ───────────────────────────────────
--
-- Migration 0006 states that "Valve has no beta/playtest type". That is no
-- longer true: the author's database holds three works whose
-- steam_app_type is literally Beta, and the store cache types seven apps
-- as 12 with a parent appid. The signal exists now.
--
-- ── Shape ────────────────────────────────────────────────────────────────
--
-- No index. The readers are the enrichment sweep and the relation scan,
-- both of which already walk every work row.

ALTER TABLE works ADD COLUMN steam_store_type INTEGER;

ALTER TABLE works ADD COLUMN steam_parent_app_id TEXT;

ALTER TABLE works ADD COLUMN igdb_game_type TEXT;

ALTER TABLE works ADD COLUMN igdb_parent_id INTEGER;

ALTER TABLE works ADD COLUMN igdb_version_parent_id INTEGER;
