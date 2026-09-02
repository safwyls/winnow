-- 0019_retire_destructive_merge.sql — retires the destructive merge now
-- that identity links (0018) carry the feature.
-- Append-only: never edit this file once shipped; add 0020_*.sql instead.
--
-- Drops merge_applications and merge_undo_rows, and narrows
-- merge_candidates.status to pending/rejected. 0018 introduced the link
-- model; this script removes the delete model. Between the two, a C#
-- one-shot (Migrations/StandingMergeReplay.cs) replays the old journal
-- into the new tables. See DatabaseInitializer for the two-pass upgrade.
--
-- ── The replay that precedes this script ──────────────────────────────
--
-- StandingMergeReplay runs BEFORE this script, at the C# layer, between
-- 0018 and 0019. It has to be C# because the journal is generic
-- (table_name, op, key_json, before_json) and SQLite cannot name a table
-- from a value. DatabaseInitializer applies the upgrade in two passes
-- when, and only when, 0019 is in the pending set; every other launch is
-- the single pass it has always been. One backup, taken once, over the
-- whole pending list, before either pass.
--
-- Three cases:
--
--  (1) An application already undone: its rows are back, all it needs is
--      its candidate reset to 'pending', which section (b) below does.
--  (2) An application still standing WITH undo_journal_version set: the
--      replay reverses the deletion, restores every moved row, and writes
--      an identity link from the restored child work to the survivor, so
--      the decision is kept and every row recovered.
--  (3) An application standing with undo_journal_version NULL, applied
--      before 0017 added the journal: not replayable. The replay refuses
--      by name rather than guessing.
--
-- ── Why the status set narrows ────────────────────────────────────────
--
-- 'undone' goes because linking is retractable: 'undone' existed only to
-- stop a re-merge loop under the destructive model, and it is what made
-- a pair read as permanently unmergeable after an undo (the user's fifth
-- complaint). 'confirmed' goes because a pair is answered affirmatively
-- if and only if a live link exists between its two resolved works, so
-- the answer has exactly one home. The replay has already written a link
-- for every confirmed pair whose releases sit under different works, so
-- a row mapped to 'pending' here either has its link or was never a live
-- question; either way the grouped queue drops it on the next read.
--
-- ── Why a link beats a delete ─────────────────────────────────────────
--
-- game-library-design.md §6.2: "The unified view is a query, not a
-- stored merge." The failure modes are asymmetric. A destructive merge
-- with a bug loses rows permanently. A link a surface fails to resolve
-- degrades to exactly what the app showed before the link: two entries
-- for one game. The link is purely additive, so an unresolved link
-- degrades to the status quo, never to corruption.

-- ── (a) Refuse to drop a standing merge ───────────────────────────────────
--
-- SQLite has no RAISE outside a trigger, so a CHECK on a throwaway table
-- is how a script refuses: INSERT fails if the value violates the
-- constraint. This is the backstop for a database that reached 0019
-- without the replay.

CREATE TABLE refuse_to_retire_a_standing_merge (
    standing_applications INTEGER NOT NULL CHECK (standing_applications = 0)
);

INSERT INTO refuse_to_retire_a_standing_merge (standing_applications)
SELECT COUNT(*) FROM merge_applications WHERE undone_at IS NULL;

DROP TABLE refuse_to_retire_a_standing_merge;

-- ── (b) merge_candidates: pending and rejected, and nothing else ──────────
--
-- Third rebuild of merge_candidates (0016 for canonicality, 0017 for the
-- fourth status, 0019 to take the fourth and the third away). SQLite
-- cannot ALTER a CHECK, so it is rebuilt again. 0016's CHECK
-- (left_release_id < right_release_id) and UNIQUE (left_release_id,
-- right_release_id) are restated verbatim so F20 stays closed. Any
-- leftover 'confirmed' or 'undone' row maps to 'pending'.

CREATE TABLE merge_candidates_rebuilt (
    id                INTEGER PRIMARY KEY,
    left_release_id   INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
    right_release_id  INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
    score             REAL NOT NULL CHECK (score >= 0.0 AND score <= 1.0),
    signals_json      TEXT,
    status            TEXT NOT NULL DEFAULT 'pending'
                      CHECK (status IN ('pending', 'rejected')),

    CHECK (left_release_id < right_release_id),
    UNIQUE (left_release_id, right_release_id)
);

INSERT INTO merge_candidates_rebuilt (
    id, left_release_id, right_release_id, score, signals_json, status)
SELECT id,
       left_release_id,
       right_release_id,
       score,
       signals_json,
       CASE WHEN status = 'rejected' THEN 'rejected' ELSE 'pending' END
FROM merge_candidates;

DROP TABLE merge_candidates;

ALTER TABLE merge_candidates_rebuilt RENAME TO merge_candidates;

CREATE INDEX ix_merge_candidates_status ON merge_candidates(status);

-- ── (c) The journal and the application log ───────────────────────────────
--
-- The journal goes with the executor that wrote it, and undone_at /
-- undo_journal_version go with the table that carried them. Both tables
-- are now fully consumed: the replay read the journal, the guard read
-- the log, and 0019 itself mapped every surviving status.

DROP TABLE merge_undo_rows;

DROP TABLE merge_applications;
