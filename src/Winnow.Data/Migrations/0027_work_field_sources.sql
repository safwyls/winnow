-- 0027_work_field_sources.sql — per-field provenance: each user-visible
-- metadata field on a work carries its own source, and that source IS the
-- truth for that field. One value per field, one answer to where it came
-- from.
-- Append-only: never edit this file once shipped; add 0028_*.sql instead.
--
-- ── One row per (work, field), replaced in place ──────────────────────
--
-- This table is deliberately NOT the append-and-stamp shape 0012, 0023
-- and 0026 use. There is exactly one value per field, so there is
-- exactly one answer to where that value came from. A history of
-- superseded answers would be a second answer to the same question.
-- The row is replaced on each write — an INSERT … ON CONFLICT DO UPDATE
-- — and the caller that stamps it is WorkFieldSourceWrites, shared by
-- WorkRepository and WorkIgdbPinRepository so both write the same shape.
--
-- ── No backfill ───────────────────────────────────────────────────────
--
-- Nothing can retroactively know whether a value written before 0027
-- came from IGDB or the Steam store. Absence of a row means no writer
-- has claimed the field and it is on automatic, which is the honest
-- reading of every value that predates this table. The first write of
-- any kind stamps it.
--
-- ── No CHECK constraint on field or source ────────────────────────────
--
-- Migration 0021 had to rebuild identity_links to widen a CHECK, and a
-- closed list in DDL charges that cost on every new source. The
-- vocabularies live in WorkFields and FieldSources (Winnow.Core.Queries),
-- and the C# layer rejects any value outside them before building SQL.
--
-- ── Fields tracked ────────────────────────────────────────────────────
--
-- name, first_release_year, summary, cover_url, publisher,
-- background_url. These are exactly the columns migration 0026's pin
-- rewrites — the user-visible metadata the editor exposes. Storefront
-- classification columns (steam_app_type, epic_categories,
-- steam_store_type, steam_parent_app_id, igdb_game_type, igdb_parent_id,
-- igdb_version_parent_id) are facts about a store entry rather than
-- fields the editor exposes, and igdb_id is identity — the pin's
-- question, not a field's.
--
-- ON DELETE CASCADE on work_id matches 0012, 0023 and 0026: a field's
-- provenance is meaningless without the work it describes.

-- background_url is a field like any other under this model. IGDB
-- screenshots and user-supplied background art share this column and
-- the same rendering path, rather than the codebase growing a second
-- image path.
ALTER TABLE works ADD COLUMN background_url TEXT;

CREATE TABLE work_field_sources (
    work_id INTEGER NOT NULL REFERENCES works(id) ON DELETE CASCADE,
    field   TEXT    NOT NULL,
    source  TEXT    NOT NULL,
    set_at  TEXT    NOT NULL,
    PRIMARY KEY (work_id, field)
);

-- The only question asked across all works at once is "which fields
-- does the user own", and it is asked by the enrichment target query
-- on its hot path. A partial index over (work_id, field) where
-- source = 'user' answers that question without reading every service
-- stamp in the table.
CREATE INDEX ix_work_field_sources_user
    ON work_field_sources(work_id, field)
    WHERE source = 'user';
