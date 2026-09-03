-- 0013_observation_identity.sql — an observation is a fact, and a fact recorded
-- twice is still one fact.
-- Append-only: never edit this file once shipped; add 0014_*.sql instead.
--
-- ── What went wrong ──────────────────────────────────────────────────────────
--
-- The resolver decides whether to append a play_records or playtime_snapshots
-- row by comparing the candidate against the NEWEST stored row for that
-- ownership. That question — "has anything changed since the last reading?" —
-- is only meaningful for an observation that IS the newest. An older one — a
-- delayed source, a replayed cache entry, or the coming M5 Year in Review
-- backfill inserting historical points — never becomes the newest, always
-- compares as "changed", and is appended again on every pass. Nothing in the
-- schema disagreed, so the history these tables exist to keep could grow
-- without bound while saying nothing new.
--
-- ── Identity is the whole fact, not just its address ─────────────────────────
--
-- play_records: (ownership_id, source, observed_at, playtime_minutes,
-- COALESCE(last_played_at, '')). Two readers that genuinely disagree at the
-- same second are two observations and both survive; the same reader
-- re-presenting the same reading is one observation however often it arrives.
-- The identity includes what was reported, not just who reported when, so a
-- cache replay with stale data is rejected even if its address columns happen
-- to be unique.
--
-- playtime_snapshots: (ownership_id, observed_at, playtime_minutes). The table
-- carries no source column and needs none — it is the longitudinal series
-- itself, and two readers reporting the same figure at the same instant are
-- reporting one data point.
--
-- ── Why COALESCE(last_played_at, '') ─────────────────────────────────────────
--
-- SQLite treats every NULL in a UNIQUE index as distinct from every other NULL
-- (per the SQL standard, NULL ≠ NULL). A plain index over the nullable column
-- would let the commonest case — "played, date unknown" — replay unbounded,
-- which is the exact failure this migration exists to stop. COALESCE maps NULL
-- to the empty string for index purposes only; the stored column stays NULL.
--
-- ── Dedup before constraining ────────────────────────────────────────────────
--
-- Any duplicates already on disk would violate the new UNIQUE index. The
-- DELETE keeps the lowest id for each identity group. On a database that has
-- only ever been written by the resolver's change-detection path this is a
-- no-op; a database that replayed delayed or cached sources may have genuine
-- duplicates to collapse.
DELETE FROM play_records
WHERE id NOT IN (
    SELECT MIN(id)
    FROM play_records
    GROUP BY ownership_id, source, observed_at, playtime_minutes, COALESCE(last_played_at, '')
);

CREATE UNIQUE INDEX ux_play_records_observation
ON play_records(ownership_id, source, observed_at, playtime_minutes, COALESCE(last_played_at, ''));

DELETE FROM playtime_snapshots
WHERE id NOT IN (
    SELECT MIN(id)
    FROM playtime_snapshots
    GROUP BY ownership_id, observed_at, playtime_minutes
);

CREATE UNIQUE INDEX ux_playtime_snapshots_observation
ON playtime_snapshots(ownership_id, observed_at, playtime_minutes);
