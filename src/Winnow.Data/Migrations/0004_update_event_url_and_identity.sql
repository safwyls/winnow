-- 0004_update_event_url_and_identity.sql — the patch-notes link, and one row per event.
-- Append-only: never edit this file once shipped; add 0005_*.sql instead.
--
-- Two changes, both required by M2's update-signal poller
-- (`src/Winnow.Enrich.Updates`, docs/spikes/update-signals.md).
--
-- 1. `url`. design-system.md §5.2: "Clicking the badge opens the patch notes for
--    the updates you missed." The link arrives on the `ISteamNews/GetNewsForApp`
--    item that produced the announcement row and is NOT cheaply recoverable
--    afterwards — the endpoint pages backwards by date with no lookup-by-gid, so
--    recovering one url later costs a re-walk of that app's whole feed. Capture
--    it at detection time or lose it. NULL on every build_push row: a depot push
--    has no reader-facing page.
--
-- 2. A natural key. The poller re-reads the same feeds every sweep and sees the
--    same newest item until the next patch lands; steamcmd.net's `timeupdated`
--    likewise persists until the next push. Without a uniqueness constraint an
--    idempotent re-poll appends a duplicate row per pass, and §6.1's
--    "stale but patched" bucket — which only asks whether a correlated pair
--    EXISTS — would keep answering correctly while the table grew without bound.
--    A silent, slow leak behind a correct-looking feature is exactly the kind of
--    bug that ships.
--
--    The key is (release_id, kind, occurred_at): "this release changed in this
--    way at this instant". It is uniform across both signal kinds, needs no new
--    column, and is the same conflict target the writer's ON CONFLICT names.
--
--    Not the news `gid`: it exists only for announcements, so a gid-based key
--    would leave build pushes unconstrained and need a second, different rule
--    for them. Two events of the same kind for one release in the same SECOND
--    are collapsed by this key; that is a deliberate trade, since the correlation
--    heuristic asks about days, not seconds.
--
-- As in 0003, SQLite cannot ALTER TABLE ... ADD CONSTRAINT, and a UNIQUE INDEX
-- is the same guarantee and the same ON CONFLICT target in one statement that
-- cannot lose data.

ALTER TABLE update_events ADD COLUMN url TEXT;

-- Any duplicates already on disk would fail the index. On every database that
-- exists today this is a no-op — nothing has written update_events yet outside
-- --seed-sample, which emits one row per (release, kind) — but a migration that
-- can fail on real data is a migration that will.
DELETE FROM update_events
WHERE id <> (
    SELECT MIN(d.id)
    FROM update_events d
    WHERE d.release_id  = update_events.release_id
      AND d.kind        = update_events.kind
      AND d.occurred_at = update_events.occurred_at
);

CREATE UNIQUE INDEX ux_update_events_identity
    ON update_events(release_id, kind, occurred_at);
