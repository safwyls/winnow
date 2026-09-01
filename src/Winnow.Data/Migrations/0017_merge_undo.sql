-- 0017_merge_undo.sql — the merge undo journal, and `undone` as a fourth
-- merge_candidates status.
-- Append-only: never edit this file once shipped; add 0018_*.sql instead.
--
-- 0016's merge_applications records counts, not identities or values, so it is
-- a receipt and cannot reverse anything. Three things it cannot restore at all:
-- the surviving works row is COALESCE-filled across eight columns plus a
-- name/name_is_provisional promotion, with no record of the prior values; the
-- surviving ownership_accounts row takes playtime_minutes, last_played_at and
-- source whole from the absorbed row; and eleven repointed tables leave no
-- record of which rows moved. Meanwhile duplicate_rows_dropped is one scalar
-- over nine tables, four of which drop rows carrying payload the survivor does
-- not have (list_items.position, release_facets.rank, feed_surfacings.shelf_id,
-- and the folded ownerships and ownership_accounts rows). That is loss, not
-- deduplication, and a count could never restore it.
--
-- This migration adds: merge_undo_rows (the row-level journal), undone_at and
-- undo_journal_version on merge_applications, and 'undone' as a fourth
-- merge_candidates status. Nothing is rebuilt except merge_candidates, and only
-- because SQLite cannot ALTER a CHECK constraint.

-- ── (a) merge_candidates: a fourth status ─────────────────────────────────
--
-- SQLite cannot ALTER TABLE ADD/DROP CONSTRAINT, so admitting a fourth status
-- means the same create-copy-drop-rename 0016 performed. Nothing foreign-keys
-- merge_candidates, so the twelve-step dance is not needed.
--
-- The new status is 'undone', not one of the three that already exist.
-- 'confirmed' would let GetConfirmedUnappliedCandidateIdsAsync re-merge the
-- pair on the next pass, which is a loop. 'pending' re-asks a question the
-- user has answered twice. 'rejected' overstates, because undoing a merge is a
-- complaint about the merge (wrong survivor, editions that should stay apart),
-- not a claim that the two are different games.
--
-- Every row and status is carried across unchanged. The CHECK
-- (left_release_id < right_release_id) and UNIQUE (left, right) that 0016
-- introduced are restated verbatim.

CREATE TABLE merge_candidates_rebuilt (
    id                INTEGER PRIMARY KEY,
    left_release_id   INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
    right_release_id  INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
    score             REAL NOT NULL CHECK (score >= 0.0 AND score <= 1.0),
    signals_json      TEXT,
    status            TEXT NOT NULL DEFAULT 'pending'
                      CHECK (status IN ('pending', 'confirmed', 'rejected', 'undone')),

    CHECK (left_release_id < right_release_id),
    UNIQUE (left_release_id, right_release_id)
);

INSERT INTO merge_candidates_rebuilt (
    id, left_release_id, right_release_id, score, signals_json, status)
SELECT id, left_release_id, right_release_id, score, signals_json, status
FROM merge_candidates;

DROP TABLE merge_candidates;

ALTER TABLE merge_candidates_rebuilt RENAME TO merge_candidates;

CREATE INDEX ix_merge_candidates_status ON merge_candidates(status);

-- ── (b) merge_applications: two columns ───────────────────────────────────
--
-- undone_at is NULL while the merge stands; set when it is reversed. The row
-- is marked, never deleted: the history screen shows a reversed merge as
-- history, and gate one has to see it to know the identity is free.
--
-- undo_journal_version is NULL for merges applied before this migration, which
-- is exactly the set of merges that cannot be reversed. No live install has
-- ever applied one (MergeExecutor had no call site), so today the column is
-- vacuous; it is here for installs that upgrade later.

ALTER TABLE merge_applications ADD COLUMN undone_at TEXT;

ALTER TABLE merge_applications ADD COLUMN undo_journal_version INTEGER;

-- ── (c) merge_undo_rows: the journal ──────────────────────────────────────
--
-- One generic journal, not fifteen typed ones. The fifteen dependent tables
-- have five key shapes and three operations, so fifteen typed journals would be
-- fifteen blocks of DDL all saying the same thing. Legibility lives in the
-- capture and restore statements, which read this table per table_name and op.
--
-- op = 'repoint': the row still exists and one or more parent columns were
-- rewritten. key_json holds the primary key and the repointed columns at their
-- post-merge values, so "is the row still where the merge left it" is the same
-- test as "does the row exist". before_json holds the repointed columns at
-- their pre-merge values.
--
-- op = 'delete': the row was removed. key_json is the key whose freedom a
-- restore needs; before_json is every column.
--
-- op = 'update': the row still exists and was edited in place. key_json is the
-- primary key; before_json is the columns that could have changed, at their
-- prior values. This is the only record of the two operations 0016 left no
-- trace of at all.
--
-- The hard foreign key to merge_applications is deliberate, the opposite call
-- to the one 0016 made for its identity columns. Those name rows that are gone
-- by design; a journal row cannot outlive the application it describes, so
-- ON DELETE CASCADE is right here.
--
-- UNIQUE (application_id, seq) makes the capture order recoverable. Restore
-- order is the repository's own (parents before children), not seq's.

CREATE TABLE merge_undo_rows (
    id              INTEGER PRIMARY KEY,
    application_id  INTEGER NOT NULL REFERENCES merge_applications(id) ON DELETE CASCADE,
    seq             INTEGER NOT NULL,
    table_name      TEXT NOT NULL,
    op              TEXT NOT NULL CHECK (op IN ('repoint', 'delete', 'update')),
    key_json        TEXT NOT NULL,
    before_json     TEXT NOT NULL,
    UNIQUE (application_id, seq)
);
