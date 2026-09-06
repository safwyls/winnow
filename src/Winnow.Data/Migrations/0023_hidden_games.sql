-- 0023_hidden_games.sql — "never show me this game again": a persisted,
-- reversible exclusion of one work from every library surface.
-- Append-only: never edit this file once shipped; add 0024_*.sql instead.
--
-- ── Append and stamp, never update in place, never delete ───────────────
--
-- The same discipline 0012 (update_acknowledgements) and 0018 (identity
-- links) use. Unhiding stamps unhidden_at rather than deleting, so
-- "is this game hidden" stays a query and the row is the history.
-- Re-hiding after an unhide is a fresh row: no terminal state and no
-- re-confirmation question to build, because "I changed my mind" is
-- allowed to be told twice.
--
-- ── Why it survives re-ingest ───────────────────────────────────────────
--
-- Nothing in an ingest pass writes this table, and the works rows it
-- references are never deleted or recreated by ingest. The resolver
-- joins on an exact (provider, provider_id) external id and mints a new
-- work only when there is no such id; there is no prune step anywhere
-- in the pipeline. So a hidden game stays hidden, and unhiding it does
-- not race with a sync.
--
-- ── Where the exclusion is applied, and why in exactly one place ────────
--
-- The bucket query in LibraryQueryRepository. The grid, the list view,
-- the feed, the rail counts, the filter chips and the recommender all
-- read that one query, so one WHERE clause makes them agree. The
-- acknowledgement watermark is the precedent: it was the first stored
-- user fact applied there once rather than checked by each consumer.
-- A second consumer answering the question separately is how they would
-- start to disagree.
--
-- ON DELETE CASCADE matches 0012: a hidden-game row is meaningless
-- without the work it hides.

CREATE TABLE hidden_games (
    id           INTEGER PRIMARY KEY,
    work_id      INTEGER NOT NULL REFERENCES works(id) ON DELETE CASCADE,
    hidden_at    TEXT NOT NULL,
    unhidden_at  TEXT
);

-- At most one live hidden row per work. HiddenGameRepository.HideAsync
-- guards the insert with WHERE NOT EXISTS, so a second hide inserts
-- nothing and the repository returns false without the index ever being
-- reached. The partial unique index makes the invariant a fact about
-- the database rather than a convention held by that guard: a future
-- writer who forgets the guard is stopped, and a reader of the schema
-- can see the constraint without reading C#.
CREATE UNIQUE INDEX ux_hidden_games_live
    ON hidden_games(work_id)
    WHERE unhidden_at IS NULL;

CREATE INDEX ix_hidden_games_work ON hidden_games(work_id);
