-- 0018_identity_links.sql — identity links: the additive replacement for
-- destructive merges.
-- Append-only: never edit this file once shipped; add 0019_*.sql instead.
--
-- Two new tables (identity_acts and identity_links) and one live-data repair
-- on merge_candidates. Nothing existing is rebuilt and nothing is dropped.
-- 0016's merge_candidates canonicality and 0017's undo journal are untouched;
-- they are retired in TASK-70.7, after the replay path runs.
--
-- game-library-design.md SS6.2 says: "Render per-release rows nested under the
-- Work. The unified view is a query, not a stored merge." The destructive
-- executor contradicts that line. Identity links resolve it: the relationship
-- is a LINK at the Work layer, resolved on read, never a deletion of rows.
-- Failure modes are asymmetric. A destructive merge with a bug loses rows
-- permanently. A link that a surface fails to resolve degrades to exactly
-- what the app shows today: two entries for one game. The link is purely
-- additive, so an unresolved link degrades to the status quo, never to
-- corruption.
--
-- Under the link model both igdb_id values survive on their own rows instead
-- of the absorbed one dying with the row, so the child keeps being enriched
-- and can still fill the group. Roughly 2,300 lines of executor, journal,
-- journal writer and undo repository are deleted rather than maintained
-- (TASK-70.7, not this migration).
--
-- ── Shape and properties ────────────────────────────────────────────────────
--
-- Append-only. Retracting stamps retracted_at rather than deleting, so the
-- history is the table itself and merge_applications becomes unnecessary.
-- Re-linking after an unlink is a fresh row: no terminal state and no
-- re-confirmation affordance to build, which is the direct fix for the
-- user's complaint that undo made a pair permanently unmergeable.
--
-- act_id groups one-to-many links under a single act with a single undo.
-- Linking a base game and six expansions is one row in identity_acts and
-- seven in identity_links, and one retraction reverses all seven.
--
-- The partial unique index ux_identity_links_live makes "at most one live
-- parent per work" a fact about the database rather than a convention held
-- by the repository. That single index is what makes resolution one LEFT
-- JOIN. Depth is fixed at one: a parent may not be a child and a child may
-- not be a parent. SQLite cannot express that as a CHECK (it would need to
-- look at other rows), so the repository asserts it and a database-integrity
-- query in IdentityLinkTests proves it after every write. Two-cycles fall
-- out of depth one, not out of the index: the index permits A-child-of-B
-- alongside B-child-of-A because those are two different children; depth one
-- refuses the second link.
--
-- ── Departure from the TASK-70 design sketch ────────────────────────────────
--
-- identity_links carries retracted_by_act_id (INTEGER REFERENCES
-- identity_acts(id) ON DELETE SET NULL) plus CHECK ((retracted_at IS NULL) =
-- (retracted_by_act_id IS NULL)), neither of which the sketch had. Reason:
-- acceptance criterion #3 requires that retracting an act restores every
-- child to the parent it had IMMEDIATELY BEFORE that act. Without this
-- column, "which live links did act N displace" would have to be recovered
-- by matching retracted_at against the act's timestamp, a heuristic that
-- breaks the moment two acts share a second. With it, it is a foreign key.

-- ── identity_acts ───────────────────────────────────────────────────────────
--
-- One write, whatever its size. The unit of undo.

CREATE TABLE identity_acts (
    id           INTEGER PRIMARY KEY,
    kind         TEXT NOT NULL CHECK (kind IN ('link', 'unlink')),
    performed_at TEXT NOT NULL,
    note         TEXT
);

-- ── identity_links ──────────────────────────────────────────────────────────
--
-- One child-points-at-parent row. A work cannot be its own parent (CHECK).
-- retracted_at and retracted_by_act_id are always set or cleared together
-- (CHECK), so a retracted link always names the act that displaced it.

CREATE TABLE identity_links (
    id                  INTEGER PRIMARY KEY,
    act_id              INTEGER NOT NULL REFERENCES identity_acts(id) ON DELETE CASCADE,
    child_work_id       INTEGER NOT NULL REFERENCES works(id) ON DELETE CASCADE,
    parent_work_id      INTEGER NOT NULL REFERENCES works(id) ON DELETE CASCADE,
    kind                TEXT NOT NULL CHECK (kind IN ('same_game', 'expansion_of')),
    source              TEXT NOT NULL CHECK (source IN ('user', 'hard_id')),
    evidence_json       TEXT,
    applied_at          TEXT NOT NULL,
    retracted_at        TEXT,
    retracted_by_act_id INTEGER REFERENCES identity_acts(id) ON DELETE SET NULL,

    CHECK (child_work_id <> parent_work_id),
    CHECK ((retracted_at IS NULL) = (retracted_by_act_id IS NULL))
);

-- At most one live parent per child. The partial unique index is what makes
-- resolution one LEFT JOIN and what prevents conflicting live links without
-- application-level coordination.
CREATE UNIQUE INDEX ux_identity_links_live
    ON identity_links(child_work_id) WHERE retracted_at IS NULL;

-- The expansion grouper reads all live children of a parent.
CREATE INDEX ix_identity_links_parent
    ON identity_links(parent_work_id) WHERE retracted_at IS NULL;

-- Retracting an act and restoring what it displaced are both reads keyed by
-- act, so this index serves both the write path and the undo path.
CREATE INDEX ix_identity_links_act ON identity_links(act_id);

-- ── merge_candidates repair ─────────────────────────────────────────────────
--
-- The one live-database fix that is safe now: any row at status 'undone'
-- whose merge_applications row has undone_at set goes back to 'pending'.
-- Those rows were already restored by the undo, so the pair is a genuinely
-- open question again, and 'undone' is precisely what made re-merging
-- terminal (the user's complaint #5).
--
-- The NOT EXISTS guard holds back any candidate that also has an application
-- still standing: for that pair the merge has not been reversed, so the
-- question is not open. The guard is defensive: a candidate with a standing
-- application normally reads 'confirmed', not 'undone'. Defensive is right,
-- because a repair that reopens an answered question is worse than one that
-- leaves a row alone.

UPDATE merge_candidates
SET status = 'pending'
WHERE status = 'undone'
  AND EXISTS (
      SELECT 1 FROM merge_applications a
      WHERE a.candidate_id = merge_candidates.id
        AND a.undone_at IS NOT NULL)
  AND NOT EXISTS (
      SELECT 1 FROM merge_applications a
      WHERE a.candidate_id = merge_candidates.id
        AND a.undone_at IS NULL);
