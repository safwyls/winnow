-- 0008_null_placeholder_last_played.sql — nobody played anything in 1970.
-- Append-only: never edit this file once shipped; add 0009_*.sql instead.
--
-- ── What went wrong ──────────────────────────────────────────────────────────
--
-- Steam reports last-played over two transports, and each one carries the same
-- placeholders: `0` for a game never launched, and `86400` — 1970-01-02 — for a
-- game last played before Steam tracked timestamps (verified on disk,
-- docs/spikes/steam-local-files.md §3, trap 1). The §4.1 local reader has always
-- mapped both to NULL, meaning "unknown". The §4.2 `GetOwnedGames` reader did
-- not: it converted `rtime_last_played` of 86400 into a literal 1970-01-02 and
-- wrote it down.
--
-- So on every appid both readers could see, the two disagreed about the same
-- fact and each appended its own `play_records` row — the local one NULL, the
-- web one 1970 — and the pair ping-ponged on every sync. On the author's library
-- that left 45 rows across 3 games (Ricochet, Counter-Strike: Condition Zero,
-- Counter-Strike: Source) claiming a session on 2 January 1970.
--
-- The rule now lives in one place, `Winnow.Core.Domain.SteamTime`, and both
-- readers call it. This migration cleans up what the disagreement already wrote.
--
-- ── Why NULL the column and not delete the row ───────────────────────────────
--
-- The 1970 date is wrong data. The `playtime_minutes` sitting beside it is not:
-- those are real minutes, really observed, and `playtime_snapshots` is a
-- longitudinal series §6 exists to keep precisely because the storefronts throw
-- it away. Deleting the rows would discard a true measurement to correct a false
-- one. NULL is what the local reader would have written for the same
-- observation, so the corrected row is exactly the row the fixed code produces.
--
-- ── The floor ────────────────────────────────────────────────────────────────
--
-- 315532800 = 1980-01-01T00:00:00Z, the same constant as
-- `SteamTime.MinValidEpochSeconds`. Steam did not exist before 2003, so nothing
-- in the 1970s can be a session; 1980 leaves room for whatever other small
-- constants Valve reaches for without ever being able to reject a real date.
--
-- `strftime('%s', …)` is used rather than a string comparison against
-- '1980-01-01' so the test is on the instant rather than on the spelling: rows
-- may be written with either a space or a 'T' between date and time depending on
-- how the value round-tripped, and both parse here. A value SQLite cannot parse
-- yields NULL, the comparison is then unknown, and the row is left alone — this
-- migration only touches timestamps it can positively prove are placeholders.

UPDATE play_records
SET    last_played_at = NULL
WHERE  last_played_at IS NOT NULL
AND    CAST(strftime('%s', last_played_at) AS INTEGER) < 315532800;
