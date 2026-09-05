-- 0026_igdb_pins.sql — a user-pinned IGDB mapping: "this is the game
-- I actually own, not the one the resolver guessed."
-- Append-only: never edit this file once shipped; add 0027_*.sql instead.
--
-- ── Append and stamp, never update in place, never delete ───────────────
--
-- The same discipline 0012 and 0023 use. Pinning inserts a row, clearing
-- stamps cleared_at rather than deleting, so "is this work pinned" stays
-- a query and the row is the history. Re-pinning to a different IGDB
-- game stamps the old row and inserts a fresh one: no terminal state,
-- and changing your mind twice is allowed.
--
-- ── Why the pin overwrites where the automatic write only fills ─────────
--
-- The values on the work row belonged to the game the resolver got wrong.
-- PinAsync writes igdb_id, first_release_year, summary, cover_url,
-- publisher, igdb_game_type, igdb_parent_id and igdb_version_parent_id
-- unconditionally — including back to NULL — because keeping half one
-- game and half another is worse than a blank column. The name is the
-- exception: works.name is NOT NULL, so it COALESCE's over blank and
-- clears name_is_provisional to stop the provisional-name pass renaming
-- the work later. Storefront-observed columns (steam_app_type,
-- epic_categories, steam_store_type, steam_parent_app_id) are left
-- untouched: they are facts about the store entry, not the IGDB game.
--
-- ── How the pin blocks automatic enrichment ─────────────────────────────
--
-- GetEnrichmentTargetsAsync opens its WHERE with NOT EXISTS against this
-- table's live rows, so the pinned work never becomes a target. That is
-- the primary enforcement and it costs no request. ApplyEnrichmentAsync
-- carries the same guard as defence in depth. Clearing the pin removes
-- the guard and returns the work to automatic resolution; the metadata
-- the pin wrote stays in place, and the next automatic pass fills what
-- is empty around it.
--
-- ON DELETE CASCADE on work_id matches 0012 and 0023: a pin is
-- meaningless without the work it pins.

CREATE TABLE work_igdb_pins (
    id          INTEGER PRIMARY KEY,
    work_id     INTEGER NOT NULL REFERENCES works(id) ON DELETE CASCADE,
    igdb_id     INTEGER NOT NULL,
    pinned_at   TEXT NOT NULL,
    cleared_at  TEXT
);

-- At most one live pin per work. WorkIgdbPinRepository.PinAsync stamps
-- the previous row before inserting, so the index is never contested in
-- normal operation; the partial unique index makes the invariant a fact
-- about the database rather than a convention held by that code.
CREATE UNIQUE INDEX ux_work_igdb_pins_live
    ON work_igdb_pins(work_id)
    WHERE cleared_at IS NULL;

CREATE INDEX ix_work_igdb_pins_work ON work_igdb_pins(work_id);
