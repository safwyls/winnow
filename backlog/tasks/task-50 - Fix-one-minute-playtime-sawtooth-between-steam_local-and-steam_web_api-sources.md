---
id: TASK-50
title: Fix one-minute playtime sawtooth between steam_local and steam_web_api sources
status: Done
assignee:
  - '@claude'
created_date: '2026-08-30 00:17'
updated_date: '2026-08-30 01:43'
labels:
  - resolve
  - data
dependencies: []
priority: medium
ordinal: 50000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The Steam local VDF reader and the Web API return playtime figures that disagree by exactly one minute for the same ownership (VDF stores in seconds and rounds differently than the API's whole-minute figure). On alternating sync passes, each source writes its own value, producing phantom rise/fall pairs in `playtime_snapshots` and `play_records`. Observed on the live database: 9 phantom playtime rises across ownerships 6, 46, and 47. These fabricate episode signal for the recommender. The `LowerBound` clamp does not catch it because each value alternately sits above and below the other by one minute.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Playtime changes of one minute or less between two sources for the same ownership do not produce new play records or snapshot rows
- [x] #2 The 9 phantom rises observed on the live database do not recur after the fix on a fresh sync cycle
- [x] #3 A test demonstrates that a one-minute disagreement between sources is absorbed rather than recorded
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Diagnose first, against the live database read-only. DONE - recorded in the notes. The 1-minute symptom is Steam's own figure; the sawtooth is real and is what this task fixes.

2. Add a tolerance constant to ExternalIdResolver: PlaytimeToleranceMinutes = 1. It lives beside the clamp because that is the only place a cross-pass disagreement is judged.

3. Err low in the clamp. Today 'if (floor > minutes) { minutes = floor; source = Carried(source); }' resolves every disagreement upward. Gate the raise on the gap exceeding the tolerance: a figure within one minute below the stored floor keeps its own lower reading and its own source label. A drop of two minutes or more still clamps and still marks '+carried', unchanged.

4. Absorb within the band in change detection. AppendPlayRecordIfChangedAsync and AppendSnapshotIfChangedAsync currently short-circuit on exact equality; widen both to a tolerance band so a figure within one minute of the newest stored row is not a change and writes nothing. Pass the tolerance in as a parameter, zero under PlaytimeView.Complete, so Complete's documented contract - a lower figure is a genuine correction and is recorded - is untouched.

5. Leave CandidateOwnershipMerge alone. Within one pass no row exists yet, and the higher of two same-instant readings carries the newer last-played date in its coherent play tuple; flipping the merge to prefer the lower figure would drag a stale date backwards (Max_protects_whichever_source_happens_to_be_the_stale_one) and buys nothing for the reported symptom, where both sources agree on 1. Err-low is enforced across passes, where the phantom signal actually arises.

6. Rounding on the ingest path: audited, none found. Every conversion is already integer division or verbatim minutes. Recorded in the notes with file and line; no change needed, so no test for a defect that does not exist.

7. Tests in ExternalIdResolverTests: a one-minute disagreement is absorbed (no play record, no snapshot); alternating steam_local/steam_web_api passes over many cycles produce no rows after the first; a rise of two minutes or more still records; a drop of two minutes or more still clamps and marks '+carried'; the boundary is exercised at exactly 1 and exactly 2 in both directions.

8. Scoped tests, then the full suite across all three test projects.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
<!-- SECTION:NOTES:BEGIN -->
Decision (2026-08-30, user-directed). Many games display 1 minute of playtime despite never having been played. When playtime sources disagree at sub-minute or one-minute magnitude, the resolved value must err on the low side: take the lower figure, not the higher. Any rounding along the playtime path must floor, never round up. The cost of reporting zero on a game played for thirty seconds is negligible; the cost of reporting one minute on a game never launched is a false signal that contaminates the feed. The 1-minute-on-never-played symptom needs diagnosis during this task. Three candidate causes exist: the sawtooth itself (TASK-50), the reconstruction clamp's minutes-vs-seconds mismatch (TASK-51), or a distinct rounding-up defect on the ingest path that promotes a sub-minute or zero reading to one minute. Whichever path is responsible, the fix falls under the same err-low principle: sub-minute disagreements resolve to the lower figure and rounding floors.

DIAGNOSIS (2026-08-29, live database read-only, Mode=ReadOnly, zero writes).

The 1-minute-on-never-played symptom is NOT our rounding. Steam itself reports 1.

Evidence, end to end:
1. Ingest path audit. No rounding-up exists anywhere on the playtime path.
   - LocalConfigReader.cs:78 stores localconfig.vdf 'Playtime' verbatim; the key is
     already in MINUTES (spike 3), so there is no seconds-to-minutes step to round.
   - SteamOwnedGame.cs:64 passes playtime_forever through verbatim (Web API minutes).
   - EpicOwnedLibrary.cs:66 'total / 60' is long integer division, i.e. floor.
   - PlaytimeSeriesReconstruction.cs:99,124,128 'running / 60' is long division, floor.
   - JournalPromptViewModel.cs:316 'Math.Max(0, seconds) / 60', floor.
   - Repo-wide sweep for Math.Ceiling / Math.Round / '+ 59' idioms on the playtime path:
     no hits. Every Math.Round hit is colour maths or UI prose.

2. Live database. 21 of 1059 ownerships currently read 1 minute. Every one of them was
   written by source 'steam_local', in a single row, at the first-ever sync
   (observed_at 2026-08-23 21:55:22.450713). None is '+carried', so the LowerBound clamp
   did not produce any of them. Zero rows in play_records carry the '+carried' suffix at
   all, so the clamp has never raised a figure on this database.

3. Raw VDF, five traced games (read-only):
   userdata/<steam3-account-id>/config/localconfig.vdf
     appid 6030  (Jedi Outcast)      Playtime '1'   LastPlayed 1527217586
     appid 12810 (Overlord II)       Playtime '1'   LastPlayed 1483935954
     appid 22370 (Fallout 3 GOTY)    Playtime '1'   LastPlayed 1445099842
     appid 37410 (Time Gentlemen)    Playtime '1'   LastPlayed 86400 (sentinel, read as null)
     appid 63600 (realMyst)          Playtime '1'   LastPlayed 1439617051
   Steam's own file says 1. Winnow reads 1. Nothing in between rounds.

4. Corroboration that these were genuinely opened. steam_first_played rows from
   ClientGetLastPlayedTimes sit seconds before each last_played:
     ownership 16: first 2018-05-25 03:06:16 -> last 03:06:26  (10 seconds)
     ownership 35: first 2015-10-17 16:36:20 -> last 16:37:22  (62 seconds)
     ownership 70: first 2015-08-15 05:36:01 -> last 05:37:31  (90 seconds)
   These are real launches of 10-90 seconds that Steam credits as 1 minute. They read as
   Active rather than Never played because 6.1 defines Never played as zero minutes AND
   no last-played date, and both a nonzero figure and a real date exist.

   Cause is (b), Steam reporting 1. Not (a) rounding, not (c) the clamp, not (d)
   first-played semantics (those rows carry playtime_minutes 0). Per instruction the
   never-played bucket rule is NOT touched; if the user wants these off the feed that is
   a presentation-level threshold decision, reported and left alone.

SEPARATE, REAL, AND STILL WORTH FIXING - the sawtooth this task names.
Confirmed exactly as described. Ownerships 6 (Portal, appid 400), 46 (Arma 2, 33900),
47 (ArmA 2 OA, 33930): steam_local and steam_web_api alternate by one minute on every
pass - 280/279, 3/2, 154/153. localconfig.vdf says Playtime 280 for appid 400 while
GetOwnedGames says 279, so the two Valve endpoints genuinely disagree by a minute.
A LAG() window over playtime_snapshots counts 14 rises of <=1 minute; 9 of them belong
to 6, 46 and 47 (3 each) and are the phantom pairs. The other 5 (ownerships 263, 277,
452, 496, 555) are month-to-month Year-in-Review reconstruction points and one genuine
+1 of real play, and must not be absorbed - they are written by
SteamPlaytimeBackfillService straight through the repositories, never through the
resolver, so the resolver-level fix cannot touch them.

Timing note: all 9 phantom rows predate 01b1558 (2026-08-28 19:48), the commit that
introduced PlaytimeView.LowerBound. The clamp already stops the DOWNWARD half of the
sawtooth, which is why no pair has appeared since 2026-08-26. What is still unfixed is
(i) the clamp resolves the disagreement UPWARD, against the err-low decision, and
(ii) a within-a-minute RISE is still recorded, which is the half that actually
fabricates episode signal for the recommender.

IMPLEMENTED (not finalized - left for orchestrator review).

src/Winnow.Resolve/ExternalIdResolver.cs
- New 'public const long PlaytimeToleranceMinutes = 1'.
- New local 'var tolerance = playtime is PlaytimeView.LowerBound ? PlaytimeToleranceMinutes : 0',
  so PlaytimeView.Complete is byte-for-byte unchanged: its documented contract is that the
  pass sees the whole truth and a lower figure is a genuine correction that is recorded.
- Clamp guard changed from 'if (floor > minutes)' to 'if (floor - minutes > tolerance)'.
  Inside the band the source keeps its own LOWER reading under its own source label instead
  of being carried up to the higher stored figure. That is the err-low decision applied to
  the clamp. Outside the band the clamp and its '+carried' label are untouched.
- AppendPlayRecordIfChangedAsync and AppendSnapshotIfChangedAsync take a tolerance parameter
  and their short-circuits widen from '==' to 'Math.Abs(stored - minutes) <= tolerance'.

THE ABSORB-VS-RECORD BOUNDARY, as implemented:
- |new - stored| <= 1 minute and the last-played date unchanged: absorbed. No play record,
  no snapshot. The stored figure stands.
- |new - stored| <= 1 minute and the last-played date genuinely moved: a play record IS
  written, and it carries the LOWER figure under its own source rather than the higher one
  under '+carried'. No snapshot row, so playtime_snapshots never falls and stays monotonic.
- new - stored >= +2: recorded. Genuine progress lands.
- stored - new >= +2: clamped to the floor and marked '+carried', exactly as before. Note
  this writes no row when the date is also unchanged, because the clamped figure then equals
  the stored one - pre-existing behaviour, unchanged.
- Drift cannot accumulate: the band is measured against the STORED figure, which does not
  advance while absorbing, so a game accruing a minute at a time records as soon as it is
  two minutes clear. The stored figure lags reality by at most one minute, which is the
  accepted cost named in the decision.

CandidateOwnershipMerge deliberately NOT changed. Within one pass no row exists yet, and the
higher of two same-instant readings carries the newer last-played date in its coherent play
tuple; flipping the merge to prefer the lower figure would drag a stale date backwards - the
case Max_protects_whichever_source_happens_to_be_the_stale_one pins - and buys nothing for
the reported symptom, where both sources agree on 1. Err-low is enforced across passes,
which is where the phantom signal actually arises.

ROUNDING AUDIT: no change made, because no rounding-up exists. Every conversion on the
ingest path is already verbatim minutes or long integer division. Files and lines are in the
diagnosis note above. There is no defect to write a test against.

Tests added to tests/Winnow.Tests/ExternalIdResolverTests.cs (all under LowerBound):
- A_one_minute_disagreement_across_passes_is_absorbed (AC 3)
- Alternating_sources_write_nothing_after_the_first_pass - six alternating passes, one row
  total (AC 1, AC 2 in simulation)
- The_band_is_one_minute_wide_in_both_directions - 281/282/279/278
- Genuine_progress_still_lands_under_the_tolerance - 280 to 331 records
- Inside_the_band_the_lower_figure_is_kept_rather_than_carried_up - the err-low proof
- Outside_the_band_a_lower_figure_is_still_clamped_and_marked_carried
- Complete_records_a_one_minute_correction_as_before - the Complete contract is intact

AC 2 caveat for review: verified by simulation, not by an actual sync. Running a fresh sync
would write to the live database, and the brief was read-only. The six-pass alternating test
reproduces the exact live figures (280/279 for appid 400) and writes one row.

All comment and XML-doc prose in the changed files was authored by the docs-writer agent.
Not committed.

Full suite green: 2034 + 74 + 70 passed, 0 failed, 0 warnings under TreatWarningsAsErrors.

FINALIZED (2026-08-30). Diagnosis outcome: the 1-minute-on-never-played symptom is Steam's own data, not Winnow arithmetic. The raw localconfig.vdf stores Playtime 1 for those games, and the first/last-played timestamps prove real 10-90 second launches (one game opened for exactly 10 seconds). No rounding-up exists anywhere on the ingest path, verified line by line and by a repo-wide sweep for Math.Ceiling, Math.Round, and the +59 idiom. The LowerBound clamp has never raised a figure on the live database: zero rows carry the +carried suffix. These games read as played because the never-played bucket rule, deliberately set by the user, requires zero minutes and no last-played date, and both a nonzero figure and a real date exist. Any change for those games is a presentation-threshold decision, deferred to the user. The sawtooth itself: 9 sub-minute alternations across ownerships 6, 46, and 47 predate the LowerBound clamp commit. The clamp had already stopped the downward half; the remaining upward half is what this task fixed. Fix shape: a one-minute absorb band under LowerBound only (Complete unchanged). Disagreements within one minute with an unchanged last-played date are absorbed entirely; no play record, no snapshot. Within one minute with a genuinely moved date, a record is written carrying the LOWER figure per the err-low ruling, with no snapshot row so the series never falls. Two minutes or more of genuine progress records normally. The band measures against the stored figure so creep cannot accumulate silently, at the documented cost that the stored figure can lag reality by at most one minute. CandidateOwnershipMerge was deliberately left alone. Within one pass the higher same-instant reading carries the newer date in its coherent tuple, and flipping it would drag dates backwards for no benefit to the symptom.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Absorb band implemented in ExternalIdResolver under LowerBound; verified by 7 new resolver tests including a six-pass alternating-source simulation reproducing the exact live figures (280/279) that writes one row, plus the full suite at 2,178 passing. AC 2 was verified by simulation rather than a live sync, since verification was read-only against the live database.
<!-- SECTION:FINAL_SUMMARY:END -->

<!-- SECTION:NOTES:END -->
