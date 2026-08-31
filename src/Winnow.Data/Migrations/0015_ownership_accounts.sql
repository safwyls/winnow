-- 0015_ownership_accounts.sql — which accounts hold a game, rather than which
-- one of them played it most.
-- Append-only: never edit this file once shipped; add 0016_*.sql instead.
--
-- ── What went wrong ─────────────────────────────────────────────────────────
--
-- Winnow ingests every Steam account signed in on the machine and collapses
-- them to one ownership per (release_id, store). `account_ref` sits OUTSIDE
-- that identity and holds a single value: the account that won the play tuple
-- (`SteamLibrarySource.ResolvePlaytimeWinner` — highest minutes, ties broken by
-- the later last-played date).
--
-- That column can answer "who played this the most". It cannot answer "does
-- account A own this", which is the question a per-account visibility filter is
-- made of. On a shared PC a game both people own carries exactly one of them,
-- and asking about the other returns a confident, wrong "no". Filtering on that
-- column would hide the user's own games because a housemate played them more —
-- the failure TASK-53 acceptance criterion #2 exists to forbid.
--
-- ── One row per (ownership, account) ────────────────────────────────────────
--
-- The fix is to stop collapsing. `ownerships` keeps its shape and its columns
-- keep their meaning — the household answer is still a real and useful fact,
-- and it is what every unfiltered surface shows. This table sits beside it and
-- records the same observation un-collapsed: one row per account, holding only
-- what that account did.
--
-- `playtime_minutes` and `last_played_at` are nullable and NULL is not zero. A
-- source can know an account holds a game without knowing for how long
-- (GetOwnedGames answers `playtime_forever` for the key's own account and
-- nothing for a third party's), and a never-launched owned game genuinely
-- reports zero. Conflating the two would put a fabricated figure on screen the
-- moment the filter starts substituting these values for the household ones.
--
-- ── Append and update only; nothing deletes ─────────────────────────────────
--
-- A row means "this account was observed holding this game". A later scan that
-- cannot see the account has not unsaid it: the other user signed out, the
-- `userdata/` directory became unreadable, the profile went private. Deleting
-- on absence would make games vanish from the filtered library every time the
-- second user logged out.
--
-- `first_seen_at` is therefore write-once — the earliest moment Winnow could
-- prove the membership — while `last_seen_at` and `source` move with each new
-- observation. Minutes take the max and last-played the later date, both WITHIN
-- one account, for the reason `PlaytimeView.LowerBound` exists: every source
-- sees a floor of a cumulative counter, so the larger figure is the closer one.
-- Merging is never performed ACROSS accounts; that is the collapse this table
-- undoes.
--
-- ── The seed, and why it is marked ──────────────────────────────────────────
--
-- Existing installs get one row per non-blank `ownerships.account_ref`, carrying
-- that ownership's newest play record so the table is useful before the first
-- sync of the new build rather than after it.
--
-- Those rows are stamped `source = 'ownerships.account_ref'` and the bucket
-- query treats them as NOT evidence about who does NOT own a game. This is the
-- load-bearing half of the seed. A seeded row inherits the whole single-winner
-- ambiguity described above — it names the account that won, which on a shared
-- game is routinely not the only owner — so a filter that trusted it would ship
-- the exact bug this migration exists to fix, for the window between the
-- migration and the first sync. Requiring one non-seed row before hiding
-- anything closes that window, and costs nothing afterwards: every sync pass
-- rewrites `source` to the reader that reported it.
--
-- The seed reads the newest play record rather than a stored ownership column
-- because `ownerships` carries no playtime at all — the figures live in
-- `play_records`, newest-wins by (observed_at, id), the same rule
-- `PlayRecordRepository.GetLatestAsync` and the bucket query's `latest_play`
-- CTE already use. Attributing that figure to the winning account is honest by
-- construction: it IS the winner's figure, because the winner is who it came
-- from.
--
-- ── ON DELETE CASCADE ───────────────────────────────────────────────────────
--
-- The one deletion path that is right: a membership row is a statement about an
-- ownership, and it cannot outlive the row it is about. Nothing in the app
-- deletes ownerships today; the constraint is there so nothing has to remember
-- this table if something ever does.

CREATE TABLE ownership_accounts (
    ownership_id     INTEGER NOT NULL REFERENCES ownerships(id) ON DELETE CASCADE,
    account_ref      TEXT    NOT NULL,
    playtime_minutes INTEGER,
    last_played_at   TEXT,
    source           TEXT    NOT NULL,
    first_seen_at    TEXT    NOT NULL,
    last_seen_at     TEXT    NOT NULL,
    PRIMARY KEY (ownership_id, account_ref)
);

-- "Which ownerships does account A hold" — the direction the filter asks in,
-- and the opposite of the primary key's.
CREATE INDEX ix_ownership_accounts_account ON ownership_accounts(account_ref);

INSERT INTO ownership_accounts (
    ownership_id, account_ref, playtime_minutes, last_played_at,
    source, first_seen_at, last_seen_at)
SELECT o.id,
       TRIM(o.account_ref),
       lp.playtime_minutes,
       lp.last_played_at,
       'ownerships.account_ref',
       -- The seed observed nothing itself; it re-states what the newest play
       -- record already said, so that record's own observation time is the
       -- honest stamp. An ownership with no play record at all has no
       -- observation to borrow, and the migration's own moment is the only
       -- truthful answer left. `datetime('now')` renders UTC as
       -- `YYYY-MM-DD HH:MM:SS`, which is the shape Microsoft.Data.Sqlite writes
       -- and reads DateTime columns in.
       COALESCE(lp.observed_at, datetime('now')),
       COALESCE(lp.observed_at, datetime('now'))
FROM ownerships o
LEFT JOIN (
    SELECT ownership_id, playtime_minutes, last_played_at, observed_at
    FROM (
        SELECT ownership_id,
               playtime_minutes,
               last_played_at,
               observed_at,
               ROW_NUMBER() OVER (
                   PARTITION BY ownership_id
                   ORDER BY observed_at DESC, id DESC) AS rn
        FROM play_records
    )
    WHERE rn = 1
) lp ON lp.ownership_id = o.id
WHERE o.account_ref IS NOT NULL
  AND TRIM(o.account_ref) <> '';
