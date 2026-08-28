-- 0007_facets.sql — the descriptors the library filters on.
-- Append-only: never edit this file once shipped; add 0008_*.sql instead.
--
-- ── What this is for ─────────────────────────────────────────────────────────
--
-- A Steam-style filter panel needs to ask "which of these 926 titles are
-- single-player RPGs I have never played", and it needs to render each checkbox
-- with a count beside it. Both questions are about descriptors — genres, themes,
-- store tags, game modes, storefront features — and none of those had anywhere
-- to live. They were fetched, they were cached, and then they were unreachable:
-- IGDB genres and themes sit inside `metadata_cache` payloads as JSON arrays,
-- Steam tag ids and category ids sit inside the raw store item bodies, and no
-- query can join on any of it.
--
-- This migration is the join surface. It stores nothing new about the world —
-- every value it holds is already on disk in `metadata_cache` — it only makes
-- what is already there queryable. Migration 0005 wrote down exactly this plan:
-- "the full publisher list — with genres and themes from the same /games
-- response — is already persisted verbatim in metadata_cache, so nothing is
-- thrown away and a later milestone ... can build its table from data already on
-- disk without spending a single request."  This is that later milestone, and it
-- spends no request on any app the cache already answers for.
--
-- ── This is NOT a bucket, and nothing here is derived ────────────────────────
--
-- §6.1's rule is that DERIVED values stay queries: buckets are recomputed on
-- every read because thresholds get tuned and stored answers rot. Facets are the
-- other kind of thing entirely — observed external FACTS, like
-- `works.first_release_year` (0001), `works.publisher` (0005) and
-- `works.steam_app_type` (0006). "Elden Ring is tagged Souls-like" does not
-- change when a threshold moves. It changes when Valve's users change it, and
-- the answer to that is a re-read of the cache, which is what the backfill does.
--
-- The line is worth stating precisely because the next table someone wants to
-- add will be on the wrong side of it: if the value would change because Winnow
-- changed its mind, it may not be stored. Nothing here can.
--
-- ── Three tables, and why not one ────────────────────────────────────────────
--
-- `facets`         the vocabulary: one row per distinct descriptor
-- `work_facets`    descriptors that are true of the WORK
-- `release_facets` descriptors that are true of one RELEASE
--
-- The split between the last two is §6's four-layer identity model, not
-- ceremony. A genre is a fact about the game: Skyrim is an RPG on every
-- storefront that ever sold it. A Steam store tag is a fact about ONE appid:
-- Skyrim and Skyrim Special Edition are separate apps, separately tagged by
-- separately-voting users, and folding their tags together would be the same
-- mistake §6.2 forbids for achievements — two facts averaged into one that is
-- true of neither. So IGDB descriptors attach to `works` and Steam descriptors
-- attach to `releases`, each at the layer where the fact actually lives, and a
-- reader that wants everything true of one release unions the two.
--
-- Today works and releases are 1:1 (see 0006's note) so the union is trivial.
-- The day a Work carries two Steam releases, this schema already says the right
-- thing and the reader does not change.
--
-- ── The vocabulary is keyed on the NAME, not the provider's id ───────────────
--
-- `facets.slug` is a normalised form of the display name, and (kind, slug) is
-- the natural key. That is forced by what is actually on disk, and it turns out
-- to be the better key anyway.
--
--   * FORCED, for IGDB. The IGDB cache does not store the raw API response — it
--     stores the projected `IgdbGame` record, whose Genres and Themes are
--     `IReadOnlyList<string>`. There are 865 such rows in the author's database
--     and they carry names only; the ids were dropped at projection time, years
--     of cache ago. Keying on IGDB's genre ids would therefore require
--     re-fetching all 865 — and worse, changing the cached shape would make
--     every existing row unreadable, so a machine with no Twitch credentials
--     would LOSE the genres it already has. Names are what we have; names are
--     what we key on.
--
--   * BETTER, for Steam. Valve's own category vocabulary ships duplicate display
--     names: ids 55 and 56 are both "DualShock Controller Support" (wired and
--     Bluetooth), 57 and 58 are both "DualSense Controller Support", and 30 and
--     51 are both "Steam Workshop" (global and Steam China). Keyed on id, the
--     filter panel would render "Steam Workshop" twice with two different
--     counts, which is not a distinction any user asked for. Keyed on the name,
--     the two ids collapse into one checkbox that means what it says.
--
-- The provider ids are not lost, and no column here pretends to hold them: they
-- remain verbatim in `metadata_cache`, exactly as 0005 left the full publisher
-- list. A future feature that needs Steam's tagid can read it from the row it
-- was always in. Storing "the id this facet happened to be minted from" would be
-- a half-true value on a row that may have absorbed three of them, and a
-- half-true stored value is the thing this schema is most careful not to have.
--
-- ── `facets.id` is a surrogate, and it is stable on purpose ──────────────────
--
-- One integer namespace across every kind, so a filter can carry a flat set of
-- ids and a reader can test membership with one lookup instead of one per kind.
--
-- These ids are what `lists.filter_json` stores for a live list, so they must
-- not move. The backfill therefore only ever INSERTs into this table (ON
-- CONFLICT DO NOTHING) and never DELETEs: a genre that stops appearing anywhere
-- in the library keeps its row, costs one row, and keeps every saved filter that
-- mentions it meaningful. Only the ASSIGNMENT rows in the two child tables are
-- rewritten by a backfill, which is what lets a re-read of the cache correct
-- itself. No AUTOINCREMENT — with no deletes there is no rowid to reuse.
--
-- ── `game_mode` is the one kind Winnow owns, and it is seeded here ────────────
--
-- Every other kind is one provider's vocabulary passed through. Game modes are
-- not: BOTH providers describe them, in different words, and the filter has to
-- ask one question. IGDB answers with `game_modes` ("Single player",
-- "Co-operative"); Steam answers with `categories.supported_player_categoryids`
-- (2 Single-player, 9 Co-op, 38 Online Co-op, 48 LAN Co-op, 39 Shared/Split
-- Screen Co-op — four ids that all mean co-op). So this kind is a normalisation
-- target, and its slugs are Winnow's own vocabulary rather than anyone's ids.
--
-- Seeded here with FIXED ids 1-6 so they are identical in every database that
-- ever runs this migration. That matters more than it does for the other kinds:
-- game modes are the one facet a saved filter refers to by slug
-- (`LibraryFilter.GameModes` is a string list precisely because no provider id
-- could serve), and fixed ids mean the seeded rows also read the same way in
-- every database. Minted facets start at 7.
--
-- Battle Royale has no Steam category and will only ever arrive from IGDB;
-- it is seeded anyway so the vocabulary is complete and visible in the table
-- rather than half-implied by whatever happened to be fetched.
--
-- ── Kinds ───────────────────────────────────────────────────────────────────
--
--   genre               IGDB genres              → work_facets
--   theme               IGDB themes              → work_facets
--   player_perspective  IGDB player_perspectives → work_facets
--   game_mode           IGDB game_modes AND Steam player categories → both
--   tag                 Steam store user tags    → release_facets (ranked)
--   feature             Steam feature categories → release_facets
--   controller          Steam controller categories → release_facets
--
-- No CHECK constraint on `kind`, for the same reason 0006 put none on
-- `steam_app_type`: a new kind is a code change that should not need a
-- migration, and a constraint here would turn one into a failed backfill write.

CREATE TABLE facets (
    id    INTEGER PRIMARY KEY,
    kind  TEXT NOT NULL,
    -- Normalised display name: lower-cased, non-alphanumerics folded to '_'.
    -- The natural key — see the note above on why this is the name and not the
    -- provider's id.
    slug  TEXT NOT NULL,
    -- What the checkbox says. Verbatim from the provider, so a vocabulary that
    -- changes its wording changes here and nowhere else.
    name  TEXT NOT NULL,
    UNIQUE (kind, slug)
);

-- Descriptors of the WORK — the same game on any storefront (IGDB).
CREATE TABLE work_facets (
    work_id  INTEGER NOT NULL REFERENCES works(id) ON DELETE CASCADE,
    facet_id INTEGER NOT NULL REFERENCES facets(id) ON DELETE CASCADE,
    PRIMARY KEY (work_id, facet_id)
);

-- Reverse lookup: "how many works carry this facet", which is the count the
-- filter panel renders beside every checkbox. The primary key serves the
-- forward direction.
CREATE INDEX ix_work_facets_facet_id ON work_facets(facet_id);

-- Descriptors of ONE RELEASE — one storefront's appid (Steam).
CREATE TABLE release_facets (
    release_id INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
    facet_id   INTEGER NOT NULL REFERENCES facets(id) ON DELETE CASCADE,
    -- 1-based position in the provider's own ordering; 1 is this release's top
    -- tag. NULL for every kind that has no order (features, controller support,
    -- game modes) — a set, not a ranking.
    --
    -- RANK, NEVER WEIGHT. docs/spikes/steam-store-tags.md measured `weight`
    -- against the store page's raw vote counts for the same app and found a
    -- constant per-app ratio (7.032-7.037 across all 20 tags) with byte-
    -- identical rank order: it is a per-app normalisation, comparable WITHIN an
    -- app and meaningless ACROSS apps. Elden Ring's 1077 and a small indie's 40
    -- are not on the same scale, and a number that looks cross-comparable but is
    -- not is worse than no number. The raw weights survive verbatim in
    -- metadata_cache if anything ever wants them.
    rank       INTEGER CHECK (rank IS NULL OR rank > 0),
    PRIMARY KEY (release_id, facet_id)
);

CREATE INDEX ix_release_facets_facet_id ON release_facets(facet_id);

-- Winnow's own game-mode vocabulary. Fixed ids; see the note above.
INSERT INTO facets (id, kind, slug, name) VALUES
    (1, 'game_mode', 'single_player',  'Single-player'),
    (2, 'game_mode', 'multiplayer',    'Multiplayer'),
    (3, 'game_mode', 'co_operative',   'Co-op'),
    (4, 'game_mode', 'split_screen',   'Split screen'),
    (5, 'game_mode', 'mmo',            'MMO'),
    (6, 'game_mode', 'battle_royale',  'Battle royale');
