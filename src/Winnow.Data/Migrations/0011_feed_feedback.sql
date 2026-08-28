-- 0011_feed_feedback.sql — the feedback loop's two facts: what the user told the
-- feed, and what the feed showed the user.
-- Append-only: never edit this file once shipped; add 0012_*.sql instead.
--
-- ── Why these are tables at all ──────────────────────────────────────────────
--
-- The recommender's rule is §6.1's: derived things stay queries, and no score
-- is ever stored. Neither of these rows is derived. "The user dismissed this
-- game on this date" and "the feed put this game on this shelf on this date"
-- are events that happened once and are reconstructible from nothing — exactly
-- the storage test playtime_snapshots passes. What remains derived stays
-- derived: "currently excluded" is a query over verdicts (revocation and
-- expiry evaluated at read time), and "the user launched a game off the feed"
-- is a JOIN between sessions and surfacings, never a stored flag.
--
-- ── feed_verdicts: what the user said ────────────────────────────────────────
--
-- Two kinds, because "stop showing me this" and "not now" are different
-- intents and collapsing them loses the difference:
--
--   'not_interested'  Durable. The user's "you were right, I'm done with this"
--                     — never expires, holds until explicitly revoked.
--
--   'snoozed'         Lapses. expires_at is REQUIRED (the CHECK below), because
--                     a snooze with no expiry IS a dismissal wearing a
--                     different name, and the writer should have to say which
--                     one it means.
--
-- CHECK-constrained like 0010's attributed_by, and for the same reason: the
-- vocabulary is ours and closed, written by one repository. A third kind (an
-- explicit "more like this", say) is a schema change and should have to be one
-- — a kind that silently starts meaning something else later is the failure
-- this constraint exists to prevent.
--
-- Rows are appended and revoked, never updated in place and never deleted by
-- the app: the history of what the user told the system is the inspection
-- surface that makes the loop auditable ("here is everything you've told the
-- feed, and when"), and undo is a revocation timestamp rather than an erasure.
-- Dismiss, undo, dismiss again is therefore two rows — the first carrying its
-- revocation stamp — with only the second active.
-- "Active" is deliberately NOT a column or a partial index — a lapsed snooze
-- has revoked_at NULL and is inactive anyway, so activeness is a function of
-- the asking moment and stays a query.
--
-- ── feed_surfacings: what the feed said ──────────────────────────────────────
--
-- One row per release per day it appeared on the feed, with the shelf that
-- claimed it. This is the cross-day memory the engine's caller-fed
-- recently-surfaced set was always specified to need (the engine itself stores
-- nothing): thirty patched games rotating through six slots requires someone
-- to remember yesterday, and until now nothing did — only the day-seeded
-- jitter rotated the feed.
--
-- The PRIMARY KEY (release_id, surfaced_on) is the engine's own one-work-one-
-- shelf rule expressed as schema — a release appears at most once per day — and
-- it makes re-recording after a same-day refresh a no-op (INSERT OR IGNORE)
-- rather than a growing pile of duplicates.
--
-- surfaced_on is a DATE ('YYYY-MM-DD'), not a timestamp, on purpose: the feed
-- is stable within a day by design (the shuffle seed is the date), so the day
-- is the event's real resolution and a finer timestamp would imply precision
-- the fact does not have.
--
-- No retention cap and no pruning: ~30 rows a day is ~11k a year, and the log
-- is load-bearing twice over — the recently-surfaced window reads its tail,
-- and the launch-endorsement JOIN (sessions.attributed_by = 'launch' within a
-- few days of a surfacing) reads its history. Pruning it would silently erase
-- endorsement evidence.

CREATE TABLE feed_verdicts (
    id          INTEGER PRIMARY KEY,
    release_id  INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
    kind        TEXT NOT NULL CHECK (kind IN ('not_interested', 'snoozed')),
    created_at  TEXT NOT NULL,
    expires_at  TEXT,
    revoked_at  TEXT,
    CHECK ((kind = 'snoozed' AND expires_at IS NOT NULL)
        OR (kind = 'not_interested' AND expires_at IS NULL))
);

CREATE INDEX ix_feed_verdicts_release ON feed_verdicts(release_id);

CREATE TABLE feed_surfacings (
    release_id   INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
    surfaced_on  TEXT NOT NULL,
    shelf_id     TEXT NOT NULL,
    PRIMARY KEY (release_id, surfaced_on)
);

CREATE INDEX ix_feed_surfacings_day ON feed_surfacings(surfaced_on);
