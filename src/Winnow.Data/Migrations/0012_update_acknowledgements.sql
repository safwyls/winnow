-- 0012_update_acknowledgements.sql — "I've seen this patch": the user
-- dismissing design-system.md §5.2's unread dot on one release, until a
-- genuinely newer update arrives.
-- Append-only: never edit this file once shipped; add 0013_*.sql instead.
--
-- ── Why a watermark, and not a boolean ───────────────────────────────────────
--
-- The obvious column for "dismiss this badge" is a flag, and a flag cannot say
-- the thing the feature is for. "Until something real comes in" is only
-- meaningful if the system knows what counts as newer, and a flag knows
-- nothing: it either suppresses the badge forever — a mute, which is not what
-- the user asked for — or it has to be CLEARED by a writer, which means every
-- poll that lands a build push must decide whose flag to reset, and the day it
-- resets one it should not have, the user's dismissal is silently undone with
-- nothing on disk to show it ever happened.
--
-- So the row records not "dismissed" but the instant that was dismissed:
-- acknowledged_through is the occurred_at of the correlated major-update build
-- push that was flagging the release at the moment the user clicked. A later
-- correlated push, one strictly greater, clears the bar by itself and the badge
-- comes back WITH NO WRITE ANYWHERE — the same read-time-evaluation discipline
-- that lets 0011's lapsed snooze re-admit its game for free.
--
-- The BUILD PUSH, not the announcement: the bucket query compares the push
-- against last-played, because the push is the moment the user's game actually
-- changed. The watermark has to be measured on the same axis the flag is, or
-- "strictly newer" answers a different question than the badge asked.
--
-- Not "now", either. Stamping the clock instead of the push's own timestamp
-- would swallow any push that landed between the read that drew the badge and
-- the click that dismissed it — a genuinely newer update, lost to a race, with
-- nothing afterwards able to notice.
--
-- ── Why it is a table at all ─────────────────────────────────────────────────
--
-- §6.1's rule is that derived things stay queries. This is not derived: "the
-- user dismissed this patch on this date" is an event that happened once and is
-- reconstructible from nothing — exactly the storage test playtime_snapshots
-- and 0011's verdicts pass. What stays derived stays derived: whether a
-- dismissal currently suppresses the badge is a comparison against the
-- release's newest correlated push, made inside the bucket query at read time
-- and never written down.
--
-- Nothing here duplicates update_events, and nothing here mutates them. §4.5
-- requires both raw signals be stored so the heuristic can be retuned without
-- re-fetching; the acknowledgement is a separate fact LAYERED OVER those rows,
-- so retuning the correlation window still re-derives everything from the same
-- untouched signals. update_events rows are never deleted or edited by this
-- feature, and a dismissal must never be implemented by pruning one.
--
-- ── Append and revoke, never update in place, never delete ───────────────────
--
-- 0011's argument, unchanged: the history of what the user told the system is
-- the inspection surface, and undo is a revocation stamp rather than an
-- erasure. Dismiss, undo, dismiss again is two rows — the first carrying its
-- revocation — with only the second standing. A second dismissal of a later
-- patch is likewise a second row; the query takes MAX(acknowledged_through), so
-- the newer watermark wins without the older row having to be destroyed to say
-- so.
--
-- "Active" is deliberately NOT a column and not a partial index. It is
-- revoked_at IS NULL, asked in the query — and note that STANDING is still not
-- the same as SUPPRESSING: a standing row stops suppressing the moment a newer
-- correlated push outranks it, which no column here could know.
--
-- ── Why there is no expiry ───────────────────────────────────────────────────
--
-- 0011's snooze lapses because "not now" is a statement about the calendar.
-- This is a statement about a specific build: it is answered by the next real
-- patch, or by nothing. An acknowledgement that timed out would re-raise the
-- badge for an update the user has already read, which is the one thing §5.2's
-- dot must never do — it marks unread updates and nothing else. There is
-- therefore no expires_at and no CHECK pairing one to a kind; the two exits are
-- revoked_at and the next push.
--
-- There is also no `kind`. 0011 has one because "stop showing me this" and "not
-- now" are genuinely different intents; here there is exactly one intent, and a
-- vocabulary of one is a column that only invites a second meaning later.
--
-- ── Where it is applied, and why in exactly one place ────────────────────────
--
-- design-system.md §5.2 states the badge IS stale_but_patched bucket
-- membership. So the exclusion lives once, in LibraryQueryRepository's
-- major_update CTE, and the tile badge, the rail's "Patched since" count, the
-- library filter chip, the recommender's bucket bonus and the feed's
-- patched_while_away shelf all agree at once without any of them learning that
-- acknowledgements exist.
--
-- The exclusion is UNCONDITIONAL. It is a stored user fact, not a heuristic, so
-- it takes no parameter and does not belong in BucketThresholds beside the
-- floors and windows — those exist to be retuned, and no amount of retuning may
-- put back a badge the user personally dismissed.
--
-- ON DELETE CASCADE matches 0011: an acknowledgement is meaningless without the
-- release it acknowledges.

CREATE TABLE update_acknowledgements (
    id                   INTEGER PRIMARY KEY,
    release_id           INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,

    -- The dismissed build push's occurred_at (UTC, 'YYYY-MM-DD HH:MM:SS').
    -- Deliberately NOT a foreign key to update_events.id: the fact being
    -- recorded is an INSTANT the user caught up to, not a row. Correlation is
    -- re-derived at read time with a tunable window (§4.5), so the push that
    -- justified the badge today may not be the same row tomorrow, and a hard
    -- reference would either forbid retuning or cascade a user's dismissal away
    -- with a re-ingested event.
    acknowledged_through TEXT NOT NULL,

    -- When the user clicked. Kept separate from the watermark: one is the
    -- clock, the other is the build, and they answer different questions.
    created_at           TEXT NOT NULL,

    -- Set on undo; NULL while the acknowledgement stands.
    revoked_at           TEXT
);

CREATE INDEX ix_update_acknowledgements_release ON update_acknowledgements(release_id);
