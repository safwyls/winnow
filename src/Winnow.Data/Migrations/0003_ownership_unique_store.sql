-- 0003_ownership_unique_store.sql — one ownership per (release, store).
-- Append-only: never edit this file once shipped; add 0004_*.sql instead.
--
-- The resolver treated (release_id, store) as a key but enforced it with a
-- read-then-insert: SELECT the ownerships for the release, look for a matching
-- store, INSERT if absent. Nothing in the schema agreed, so an interrupted or
-- concurrent pass could leave two ownership rows for the same game on the same
-- store, splitting its play history in half. With this constraint in place the
-- resolver upserts with ON CONFLICT, exactly as session_notes and list_items
-- already do.
--
-- SQLite cannot ALTER TABLE ... ADD CONSTRAINT: a table-level UNIQUE needs the
-- 12-step rebuild (new table, copy, drop, rename, recreate indexes and every FK
-- that points at it). A UNIQUE INDEX is the same guarantee and the same
-- conflict target for ON CONFLICT, in one statement that cannot lose data —
-- that is what this uses. The rebuild buys nothing here.

-- Any duplicates already on disk would fail the index, so collapse them first,
-- keeping the lowest id and re-pointing its children rather than letting the FK
-- cascade delete play history. On a healthy database every one of these is a
-- no-op.
CREATE TEMP VIEW ownership_canonical AS
SELECT o.id AS id,
       (SELECT MIN(c.id)
        FROM ownerships c
        WHERE c.release_id = o.release_id
          AND c.store      = o.store) AS canonical_id
FROM ownerships o;

UPDATE play_records
SET ownership_id = (SELECT canonical_id FROM ownership_canonical WHERE id = play_records.ownership_id);

UPDATE playtime_snapshots
SET ownership_id = (SELECT canonical_id FROM ownership_canonical WHERE id = playtime_snapshots.ownership_id);

UPDATE sessions
SET ownership_id = (SELECT canonical_id FROM ownership_canonical WHERE id = sessions.ownership_id);

DELETE FROM ownerships
WHERE id <> (SELECT canonical_id FROM ownership_canonical WHERE id = ownerships.id);

DROP VIEW ownership_canonical;

CREATE UNIQUE INDEX ux_ownerships_release_store ON ownerships(release_id, store);
