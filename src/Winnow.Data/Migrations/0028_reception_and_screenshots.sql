-- 0028_reception_and_screenshots.sql — screenshots, artworks and ratings
-- from IGDB and the Steam store, stored per work per source.
-- Append-only: never edit this file once shipped; add 0029_*.sql instead.
--
-- ── Two tables, the same arrangement 0024 uses ───────────────────────
--
-- work_images: one row per (work_id, source, kind).
-- work_ratings: one row per (work_id, source).
-- A re-read from one source replaces its own row and can never clobber
-- another source's, exactly as work_maturity does.
--
-- ── work_images ──────────────────────────────────────────────────────
--
-- image_ids holds IGDB image_id values, comma-joined, verbatim and in
-- IGDB's own order — the order the publisher chose is the order the
-- strip is drawn in. The ids are the durable handles; no URL is stored,
-- because the size token in the CDN path decides the rendition and a
-- stored URL would carry a size that has to be rewritten on every read.
-- kind is "screenshot" or "artwork": they are separate IGDB assets and
-- a game can have one and not the other.
--
-- ── work_ratings ─────────────────────────────────────────────────────
--
-- Sources are igdb_users, igdb_critics and steam. The three figures are
-- stored apart and are never blended: IGDB's user body, IGDB's
-- aggregation of external critics, and Steam's reviewers are three
-- different populations answering three different questions. An
-- unattributed score is worse than none.
--
-- rating_count travels with score and is never optional in meaning: a
-- 90 from four people and a 90 from four thousand are different claims.
-- label is Steam's own words (e.g. "Very Positive"), stored verbatim
-- rather than re-derived from the percentage. IGDB publishes no label,
-- so its two rows leave it null.
--
-- A source with no figure gets no row. Absence is recorded as absence,
-- never as a zero — which is what makes "a game with no rating data
-- shows nothing rather than a zero or an empty scale" a property of the
-- data rather than something every view has to remember.
--
-- ── score is not a derived value ─────────────────────────────────────
--
-- work_ratings.score is a figure a third party published, recorded as
-- observed, exactly as merge_candidates.score records what the matcher
-- thought at the moment it queued a pair. Nothing in Winnow computes
-- it, so no threshold retune can make it rot.
--
-- ── No CHECK constraint on source or kind ────────────────────────────
--
-- For the two reasons 0024 states: the vocabularies live in C#
-- (ImageSources, ImageKinds and RatingSources in
-- Winnow.Core.Queries.Reception) and are expected to grow as more
-- storefronts are supported, and migration 0021 had to rebuild a table
-- to widen a CHECK.
--
-- ON DELETE CASCADE on both, so deleting a work takes its reception
-- rows with it.

CREATE TABLE work_images (
    work_id     INTEGER NOT NULL REFERENCES works(id) ON DELETE CASCADE,
    source      TEXT NOT NULL,
    kind        TEXT NOT NULL,
    image_ids   TEXT NOT NULL,
    observed_at TEXT NOT NULL,
    PRIMARY KEY (work_id, source, kind)
);

CREATE TABLE work_ratings (
    work_id      INTEGER NOT NULL REFERENCES works(id) ON DELETE CASCADE,
    source       TEXT NOT NULL,
    score        REAL,
    rating_count INTEGER,
    label        TEXT,
    observed_at  TEXT NOT NULL,
    PRIMARY KEY (work_id, source)
);
