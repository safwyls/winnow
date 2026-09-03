-- 0016_merge_canonicality.sql — canonical merge pairs and the application log.
-- Append-only: never edit this file once shipped; add 0017_*.sql instead.
--
-- Two changes, both required before merge execution can land safely.
--
-- ── (a) Canonical pairs (F20) ───────────────────────────────────────────────
--
-- The 0001 merge_candidates table permits self-pairs (A,A) and both
-- orientations of a pair ((A,B) and (B,A)), and its UNIQUE key only protects
-- literal orientation. An import or a concurrent writer could therefore
-- duplicate a human review or invalidate it by answering the mirror. The
-- table is rebuilt with CHECK (left < right) + UNIQUE (left, right), which
-- makes both impossible at the schema level.
--
-- SQLite cannot ALTER TABLE ADD CONSTRAINT, so this is a full table rebuild.
-- Nothing references merge_candidates with a foreign key, so the 12-step
-- dance (new table, copy, drop, rename, recreate indexes and every FK that
-- points at it) is not needed; a plain create-copy-drop-rename suffices.
--
-- ── (b) merge_applications ──────────────────────────────────────────────────
--
-- Records what a merge did, after the fact. Once a merge deletes the absorbed
-- work or release, the merge_candidates row that justified it is gone too
-- (ON DELETE CASCADE from releases), so without this table the decision and
-- its effects are unrecoverable from the database alone. The same reasoning
-- that gave 0012's acknowledged_through no foreign key applies here: the rows
-- it points at are gone by design, and a hard reference would either forbid
-- the deletion or cascade this record away with it.

-- ── merge_candidates: canonical pairs only (F20) ──────────────────────────
--
-- The rebuild enforces two structural invariants the original table lacked:
-- left_release_id < right_release_id (no self-pairs, one canonical
-- orientation per pair) and UNIQUE (left, right) (one row per pair).

CREATE TABLE merge_candidates_rebuilt (
    id                INTEGER PRIMARY KEY,
    left_release_id   INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
    right_release_id  INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
    score             REAL NOT NULL CHECK (score >= 0.0 AND score <= 1.0),
    signals_json      TEXT,
    status            TEXT NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'confirmed', 'rejected')),

    -- Self-pairs are meaningless and mirror pairs are a data-integrity hazard:
    -- with both orientations present, confirming one and rejecting the other
    -- is two contradictory answers about one question.
    CHECK (left_release_id < right_release_id),
    UNIQUE (left_release_id, right_release_id)
);

-- Canonicalise existing rows: orient to left < right, drop self-pairs, and
-- collapse mirrors. When the same pair appears more than once, keep the row
-- that carries the most information: a terminal decision (confirmed/rejected)
-- beats a pending proposal, a rejection beats a confirmation (refusing to
-- merge is the reversible outcome; a lost confirmation can be re-confirmed,
-- but a lost rejection lets a future sweep re-ask a question the user already
-- answered), and among ties the lowest id wins (oldest row, stable).
INSERT INTO merge_candidates_rebuilt (
    id, left_release_id, right_release_id, score, signals_json, status)
SELECT m.id,
       MIN(m.left_release_id, m.right_release_id),
       MAX(m.left_release_id, m.right_release_id),
       m.score,
       m.signals_json,
       m.status
FROM merge_candidates m
WHERE m.left_release_id <> m.right_release_id
  AND m.id = (
      SELECT d.id
      FROM merge_candidates d
      WHERE d.left_release_id <> d.right_release_id
        AND MIN(d.left_release_id, d.right_release_id) = MIN(m.left_release_id, m.right_release_id)
        AND MAX(d.left_release_id, d.right_release_id) = MAX(m.left_release_id, m.right_release_id)
      ORDER BY CASE d.status
                   WHEN 'rejected'  THEN 0
                   WHEN 'confirmed' THEN 1
                   ELSE 2
               END,
               d.id
      LIMIT 1);

DROP TABLE merge_candidates;

ALTER TABLE merge_candidates_rebuilt RENAME TO merge_candidates;

CREATE INDEX ix_merge_candidates_status ON merge_candidates(status);

-- ── merge_applications: what a merge did, after the fact ──────────────────
--
-- No foreign keys. candidate_id, absorbed_work_id and absorbed_release_id all
-- name rows that are gone after a successful merge, and a hard reference
-- would either forbid the deletion or cascade this record away with it.
-- The same reasoning gave 0012's acknowledged_through no FK to
-- update_events: the fact being recorded outlives the row it is about.

CREATE TABLE merge_applications (
    id                    INTEGER PRIMARY KEY,

    candidate_id          INTEGER NOT NULL,
    left_release_id       INTEGER NOT NULL,
    right_release_id      INTEGER NOT NULL,

    mode                  TEXT NOT NULL CHECK (mode IN ('work_only', 'release_collapse')),

    surviving_work_id     INTEGER NOT NULL,
    absorbed_work_id      INTEGER,
    surviving_release_id  INTEGER,
    absorbed_release_id   INTEGER,

    applied_at            TEXT NOT NULL,
    summary_json          TEXT,

    -- A release_collapse without two distinct release ids is a contradiction:
    -- the mode says "collapse" but the row cannot say what was collapsed into
    -- what. A work_only merge that already shared a work records NULL for the
    -- absorbed side; one that unified two works records both, and they must
    -- differ.
    CHECK (mode <> 'release_collapse'
        OR (surviving_release_id IS NOT NULL
            AND absorbed_release_id IS NOT NULL
            AND surviving_release_id <> absorbed_release_id)),
    CHECK (absorbed_work_id IS NULL OR absorbed_work_id <> surviving_work_id)
);

CREATE INDEX ix_merge_applications_candidate ON merge_applications(candidate_id);
CREATE INDEX ix_merge_applications_absorbed ON merge_applications(absorbed_release_id);
