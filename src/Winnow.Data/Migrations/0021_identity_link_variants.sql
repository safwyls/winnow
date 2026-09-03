-- 0021_identity_link_variants.sql — adds the variant_of kind and a relation
-- label to identity_links.
-- Append-only: never edit this file once shipped; add 0022_*.sql instead.
--
-- ── Why a rebuild ────────────────────────────────────────────────────────
--
-- SQLite cannot ALTER a CHECK constraint, so admitting a third kind to the
-- CHECK (kind IN (...)) on identity_links means rebuilding the table. That
-- cost is the argument for FEW kinds and a separate label column rather than
-- many kinds: IGDB alone has fifteen game_type names today and will add more,
-- and a migration per vocabulary word is not a design.
--
-- Rebuild procedure follows 0019's: build beside, copy, drop, rename,
-- recreate every index verbatim. Nothing else in the schema references
-- identity_links, so no foreign key elsewhere has to be re-pointed.
--
-- ── The three kinds ─────────────────────────────────────────────────────
--
-- Kinds are defined by the NUMBERS they change. same_game is one game sold
-- twice; it rolls up count and playtime. Unchanged from 0018. expansion_of
-- is a product you bought that depends on a base (Civilization IV and Beyond
-- the Sword); it counts as a title and its playtime does not roll up.
-- Unchanged from 0018.
--
-- variant_of is NEW. A sample or test build you were handed: a demo, a
-- beta, a playtest, a staging or experimental branch. It does not count as
-- a title while its parent is owned, and it does count when it is the only
-- thing owned. Playtime never rolls up, but the variant's own hours stay
-- visible on the parent's modal, because "you played forty minutes of the
-- demo and never bought it" is the app's premise, not noise. This is
-- DemoConsolidation's existing read-time rule turned into a stored fact
-- with a storefront source behind it.
--
-- ── relation_label ──────────────────────────────────────────────────────
--
-- The source's own word for the relation: expansion, dlc, standalone
-- expansion, episode, season, pack, remaster, remake, port, fork, mod,
-- demo, beta, playtest, superseded. A card can say the true word without a
-- migration per vocabulary item, because labels cost nothing and kinds cost
-- a table rebuild each. Editions (remaster, remake, port, fork, expanded
-- game) are numerically identical to expansion_of and semantically not
-- expansions: they take that kind and a different label, rather than a
-- fourth kind.
--
-- Nullable, because every row written before this migration has no label,
-- and inventing one for it would be a claim no source made.
--
-- No CHECK on relation_label. The vocabulary belongs to IGDB and Valve, is
-- undocumented on the Steam side, and a constraint would turn a new type
-- name into a failed write. The same reasoning 0006 gives for leaving
-- works.steam_app_type unconstrained.

CREATE TABLE identity_links_rebuilt (
    id                  INTEGER PRIMARY KEY,
    act_id              INTEGER NOT NULL REFERENCES identity_acts(id) ON DELETE CASCADE,
    child_work_id       INTEGER NOT NULL REFERENCES works(id) ON DELETE CASCADE,
    parent_work_id      INTEGER NOT NULL REFERENCES works(id) ON DELETE CASCADE,
    kind                TEXT NOT NULL CHECK (kind IN ('same_game', 'expansion_of', 'variant_of')),
    source              TEXT NOT NULL CHECK (source IN ('user', 'hard_id')),
    relation_label      TEXT,
    evidence_json       TEXT,
    applied_at          TEXT NOT NULL,
    retracted_at        TEXT,
    retracted_by_act_id INTEGER REFERENCES identity_acts(id) ON DELETE SET NULL,

    CHECK (child_work_id <> parent_work_id),
    CHECK ((retracted_at IS NULL) = (retracted_by_act_id IS NULL))
);

INSERT INTO identity_links_rebuilt (
    id, act_id, child_work_id, parent_work_id, kind, source,
    relation_label, evidence_json, applied_at, retracted_at, retracted_by_act_id)
SELECT id, act_id, child_work_id, parent_work_id, kind, source,
       NULL, evidence_json, applied_at, retracted_at, retracted_by_act_id
FROM identity_links;

DROP TABLE identity_links;

ALTER TABLE identity_links_rebuilt RENAME TO identity_links;

-- Every index 0018 created, restated verbatim. A rebuild drops them with the
-- table, and the partial unique index in particular is not decoration: it is
-- what makes "at most one live parent per work" a fact about the database and
-- resolution a single LEFT JOIN.

CREATE UNIQUE INDEX ux_identity_links_live
    ON identity_links(child_work_id) WHERE retracted_at IS NULL;

CREATE INDEX ix_identity_links_parent
    ON identity_links(parent_work_id) WHERE retracted_at IS NULL;

CREATE INDEX ix_identity_links_act ON identity_links(act_id);
