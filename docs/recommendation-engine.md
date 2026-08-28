# Hoard.Recommend — the scoring core

**Status:** built and tested — flat feed, shelf surface, and the feedback loop (§6b:
verdict storage, cross-day surfacing memory, launch endorsements) — the loop's UI
affordances are not yet wired (the App layer owns them; the contract is in §6b).
**Module:** `src/Hoard.Recommend`, depends on `Hoard.Core` only.
**Charter:** `.claude/agents/recommendation-engine.md`. Vocabulary: `game-library-design.md` §6.1.

This document is the argument for the model: every signal, its tier, its weight, and every
threshold with the reason it is that number and not another. If you disagree with a number,
this is the file to argue with — and every number here is a parameter on
`RecommendationTuning`, so losing the argument costs a default, never a migration.

---

## 1. What the module does, and does not

`RecommendationEngine.GetFeedAsync(request)` reads the library through `Hoard.Core`
repository interfaces and returns a ranked list of **owned** games worth surfacing, each
carrying a one-sentence human-readable reason and a full per-signal breakdown. It writes
nothing, caches nothing, and decides no identity questions. Scores are derived values in
exactly the §6.1 sense: computed on every read, comparable **within one feed**, never
stored, never trusted by anything else.

`GetShelvesAsync(request)` is the second entry point and the one a feed UI should use: the
same scoring pass served as **several themed shelves**, each with its own one-line pitch and
its own membership rule, every one of them fully populated at Tier 0. §6a is the argument.

Unowned/store recommendations are explicitly out of scope (charter: priority 1 is
owned-but-unplayed; catalog data for anything else does not exist yet).

## 2. What the real data says (measured 2026-08-26, ~1,027 ownerships)

The design mandate is empirical over clever, so the model was designed against a read-only
copy of the author's live database, not against imagined data. The findings that shaped it:

| Fact | Measured value | Consequence for the model |
|---|---|---|
| Snapshot depth | 955 of 960 snapshot-bearing ownerships have exactly **one** snapshot; 5 have real deltas | The library is at Tier 0 *today*. Snapshot-shape signals must be bonuses, never prerequisites. |
| Sessions | **Zero** rows | Every session signal is Tier 1+. Cadence gating is deferred entirely. |
| `acquired_at` | 13 of 1,027 non-null (GOG only) | **Shelf time (acquired → first played) is dead on arrival** for Steam/Epic. The charter lists it as a headline signal; the data says it cannot ship until the GDPR importer lands. Dormancy (time since last played) is the degraded substitute. |
| `last_played_at` | 603 dated, spanning 2012–2026; 357 null; 12 null-with-minutes | Dormancy is the one longitudinal fact that IS retroactively available, because Steam's local files carry it. Lean on it. |
| §6.1 buckets (defaults) | never_played 754, bounced 244, stale_but_patched 20, retired 9, active 0 | The candidate pool is ~1,018 rows and 74% of it is `never_played`. Ranking *within* the shelfware pile needs a tiebreaker (taste affinity + deterministic jitter); nothing about the pile itself differentiates its members. |
| Dormancy distribution (dated, non-retired) | median **6.9 years**, p25 2.4y, p75 9.6y; 125 rows ≥10y | A dormancy score that decays after N years would suppress the older *half* of the library — the exact pile the app exists to surface. Dormancy therefore **saturates and stays flat**, and "too old to bother" is expressed only by the narrow probably-done penalty (§5). |
| Update events | announcements on 246 releases (retroactive to 2014); build pushes only since the poller started; 28 releases have a correlated major-update pair | The patch signal is real but its *coverage* is poller-recency-bound. Bucket membership (`stale_but_patched`) is the scoring input; per-release event detail decorates reasons only. |
| Cross-store ownership | works:releases are 1:1 (1,027:1,027) — duplicates live in `merge_candidates`, unconfirmed | The bought-it-twice signal exists in the schema but fires only after the user confirms merges. Kept, cheap, and honest about rarely firing yet. |
| Provisional names | 6 works | Excluded from the feed: a tile named "App 1203620" cannot carry an explainable recommendation. |
| Recently played | 13 ownerships touched in the last 14 days | The fresh-play suppression removes real rows, not hypothetical ones (Witchspire, played this morning, must not be "surfaced"). |

Re-measured 2026-08-27 (1,059 ownerships: 946 Steam / 99 Epic / 14 GOG, and the first 7
real sessions — the library is genuinely **Settling** now), while designing the shelf
surface. Three findings reshaped the model a second time:

| Fact | Measured value | Consequence |
|---|---|---|
| Affinity saturation | With the original metric, **266 of 427** never-opened rows scored a *perfect* taste match, because the profile's peak facets were "Action" (~⅔ of releases), "Singleplayer", "Adventure" | A max-shared-facet affinity over raw facets measures "carries a common tag", not taste. Fixed by the **prevalence cut** (§5): facets carried by >25% of the facet-carrying library stop counting as taste. The profile's peaks become Survival / Sandbox / Crafting — this user's actual, distinctive taste — and the never-opened pool at the 0.6 affinity floor becomes ~200 rows: months of rotation, not three favourites, not the whole pile. |
| Franchise clusters | 14 unplayed "Infinity Blade" entries; 5 "Star Wars", 5 "Civilization IV", 5 "X-COM", 3 "Half-Life" among the unplayed | Rank honestly by score and a shelf becomes one franchise five times — a broken feed even when every score is right. Hence the one-per-franchise shelf cap (§6a), which is grouping for *display variety*, never an identity decision (that stays Resolve's). |
| Mode mismatch | 261 committed games carry mode facets; **243 (93%) are single-player**. The never-opened pile holds **12 multiplayer-only** titles (Team Fortress Classic, Deathmatch Classic, H1Z1 Test Server…) | A genre-matched MMO in a solo player's feed is a false positive the facets can catch at Tier 0. Hence the mode-mismatch penalty (§3) — fired against real rows, with the sentence that says why. |
| Facet coverage by store | Steam 861/946 releases carry genres; Epic 42/99; GOG 9/14 | Facet-driven signals (taste shelf, mode mismatch, genre caps) now reach all three stores, but Epic coverage is the thinnest — an Epic game absent from the taste shelf may be missing metadata, not missing appeal. |

## 3. Signal inventory

Weights are on positive signals in [0,1] value space; penalties subtract. The score is a
plain weighted sum — transparent, inspectable, no renormalisation (a missing signal
contributes zero and the gap is *visible*, which is the honest way to degrade).

| Signal | Tier | Weight (default) | What it says in one sentence |
|---|---|---|---|
| **Patch after dormancy** | 0 | +0.40 | "A major update landed since you stopped playing." Bucket `stale_but_patched` — the app's headline fact, computed by the §6.1 query from the correlated build-push + announcement pair. Retroactive, so available on day one. |
| **Commitment shape** | 0 | +0.25 | Where the playtime sits against §6.1's refund line: bounced-just-past-the-line peaks (they committed and gave up — the highest-value pile), decaying toward the retired floor; sampled (1–119 min) sits above never-opened (they showed intent); never-opened is the wide flat base. |
| **Dormancy** | 0 | +0.15 | How long since last played, ramping from the fresh window to saturation at 2 years and staying flat (see §2 for why it must not decay). Null date beside real minutes reads as "unknown, certainly ancient" = fully dormant, matching the bucket query's reasoning. |
| **Taste affinity** | 0 | +0.10 | The candidate carries a genre/theme/tag that the user's actual hours concentrate in. Explicitly a **tiebreaker** for the 754-row shelfware pile, not the lead — genre similarity is the commodity the charter says loses to incumbents. Profile is playtime-weighted (√minutes, refund line and up — with one exception: a feed-**endorsed** release testifies below the line with whatever √minutes it has, §6b), so retired games — excluded as candidates — still testify about taste. Facets above the **prevalence cut** (carried by >25% of the facet-carrying library) are excluded from the profile entirely: measured, they saturate the metric into meaninglessness (see §2's re-measurement). |
| **Tried to like it** | 1 | +0.10 | Distinct return episodes (snapshot rises or sessions beyond the first): 40 minutes across six evenings is a different fact from 40 minutes once. Zero until history accrues; a bonus, never a prerequisite. |
| **Installed** | 0 | +0.05 | Zero friction: it is on disk right now. |
| **Bought twice** | 0 | +0.05 | The same work owned on 2+ stores is a purchase made twice — intent money can measure. Fires only after cross-store merges are confirmed (see §2). |
| **Recently played** (penalty) | 0 | −0.60 | Played within the fresh window — not forgotten, so not this feed's business. Sized to sink anything: no combination of positives outruns it into the top of a realistic feed. |
| **Probably done** (penalty) | 0 | −0.30 | Deep in the bounced pile (a fair shake of hours), deeply dormant, and nothing has changed since — the model's way of saying "you were right to drop this" instead of nagging. The contribution's explanation says exactly that, which is the charter's honesty requirement made concrete. |
| **Recently surfaced** (penalty) | 0 | −0.20 | Caller-supplied set of releases the feed showed recently — the anti-"same five games forever" mechanism. Since migration 0011 the caller loads it from the `feed_surfacings` log via `FeedbackSets` (§6b); the engine still stores nothing. |
| **Mode mismatch** (penalty) | 0 | −0.10 | The candidate sits entirely on the wrong side of the single-player/online line for how this user demonstrably plays (93% single-player by committed game count, measured). Fires only under dominance (≥85% share over ≥20 mode-carrying committed games) and only against a candidate that is *exclusively* the other side; co-op without versus is a maybe, not a mistake. Sized to cancel a perfect taste match, not to bury — mode facets can be missing or wrong. |
| **Shuffle jitter** | 0 | +0.03 max | Deterministic per (seed, release) noise, seeded by the day by default. Big enough to rotate near-ties inside the shelfware pile, small enough to never reorder games a real signal separates. |

Signals deliberately **not** scored, and why, are in §7.

## 4. The model

```
score = Σ (weight_s × value_s) − Σ penalties + jitter
```

- Every `value_s` is clamped to [0,1] and produced by a pure function in
  `RecommendationScorer` — no IO, unit-testable to the decimal.
- Every contribution is returned on the result (`SignalContribution`: name, weight, value,
  contribution, one-sentence explanation). The `Reason` string is composed from the top
  contributions, so a reason can never drift from the arithmetic that ranked the item.
- Missing evidence contributes 0 and is absent from the breakdown. Nothing is renormalised:
  a Tier-0 library simply produces lower absolute scores, which is true.
- One feed entry per **work** (best-scoring ownership wins). Two ownerships of one game are
  one recommendation, not two slots.

### Hard exclusions (never scored, never surfaced)

1. **Retired** (§6.1 precedence: retired outranks everything, patches included). The
   200-hour game does not come back, ever.
2. Releases in the caller's **not-interested** set — the user's explicit "you were right,
   drop it" verdict, permanent until they change it. Widened to the release's **work**:
   after a cross-store merge, dismissing the Steam card must not let the GOG copy
   resurface the same game. The stored fact stays the clicked release (§6b); the widening
   is recomputed per request.
3. Releases in the caller's **snoozed** set — the temporary form of the same thing, same
   work-widening.
4. Works with **provisional names** — unexplainable tiles (6 rows today).
5. Everything the §6.1 query already dropped upstream: consolidated demos/betas, and
   non-game entries (tools, soundtracks) under the default setting.

## 5. Thresholds, and why each is that number

All live on `RecommendationTuning` with these defaults. The §6.1 refund line
(120 minutes) is inherited from `BucketThresholds` and is the standard each of these tries
to meet: a number that means something.

| Parameter | Default | Justification |
|---|---|---|
| `FreshPlayWindowDays` | 14 | Steam's own definition of current activity: `playtime_2weeks` is the storefront's window for "playing it now". The one non-arbitrary recency number available. |
| `DormancySaturationYears` | 2.0 | p25 of the measured dormancy distribution is 2.4 years — a ramp that saturates at 2 treats roughly the older three-quarters of the dated library as fully dormant and stops pretending finer discrimination among 5-vs-9-year-old piles means anything. |
| `DeepDormancyYears` | 4.0 | Gate for the probably-done penalty. Past ~4 years the person who bounced is, in gaming-taste terms, a different player, and the median bounced game (5.7y dormant) sits beyond it — the penalty is *meant* to reach the middle of that pile, but only jointly with the fair-shake gate below, which is what keeps it narrow (6 rows today). |
| `FairShakeMinutes` | 2,000 | ~33 hours: about the published aggregate main-story-plus-extras completion time for story-driven games. Past it, "abandoned" usually means "finished with it", not "forgot it". Explicitly provisional — §6.1's HowLongToBeat [VERIFY] item is the real answer, and this parameter is where per-game numbers would plug in. |
| `CommitmentFloorValue` | 0.15 | The bounced curve's value as playtime approaches the retired floor: near-retired games are near-finished, not forgotten, but stay above shelfware's floor because a 90-hour game someone left IS more interesting than a game never opened. |
| `ShelfwareBaseValue` | 0.35 | Never-opened base. Each individual shelfware row is weak evidence of intent (the pile is 412 rows of zero-and-dateless); the base keeps the pile in the feed without letting it outrank anyone with an actual history. |
| `SampledBaseValue` / `SampledSpanValue` | 0.50 / 0.20 | 1–119 minutes ramps 0.50→0.70: launching at all shows intent shelfware lacks, but §6.1 says sub-refund-line minutes are still "never played it", so the whole ramp stays strictly below the bounced peak — and the deliberate jump at 120 (0.70→1.00) *is* the refund line's semantics: crossing it is a different fact, not more of the same one. |
| `TriedToLikeSaturationEpisodes` | 3 | Coming back twice after the first taste is already "trying to like it"; requiring more before full credit would gate the signal on history depth the measured library will not have for months. |
| `Tier2MinSessions` / `Tier2MinSpanDays` | 50 / 56 | "Months in" made concrete: ~50 sessions across two months is when cadence/seasonality claims stop being anecdotes. |
| `HistoryProbeLimit` / `RecentProbeLimit` | 60 / 25 | The repository interfaces read history per-ownership, so the engine probes the shortlist (3× a 20-item feed) plus the most recently played rows (where history concentrates — the 5 real multi-snapshot ownerships are all recent) rather than issuing 2,000 queries per feed. |
| `JitterAmplitude` | 0.03 | Below the smallest deliberate weight gap (0.05), so jitter can only reorder rows no real signal separates. |
| `PenaltyModeMismatch` | 0.10 | Equal to the taste weight on purpose: a perfect genre match on a game the user will never launch with strangers should net to zero, not to a recommendation. A demotion, never an exclusion — facets can be missing or miscoded, and a demotion is recoverable. |
| `ModeEvidenceMinGames` | 20 | Committed mode-carrying games before the profile may claim a dominant mode. Below it, a handful of purchases could fake dominance; at 20+ games with an 85% share, chance is off the table. The measured library has 261. |
| `ModeDominanceShare` | 0.85 | Seventeen-in-twenty: past it the minority mode is occasional experimentation, not a second taste the model should serve. Measured library: 0.93 single-player. |
| `TasteFacetMaxPrevalence` | 0.25 | The prevalence cut. "Action" sits on ~two-thirds of the real library and saturated the affinity metric (266 of 427 never-opened rows at a perfect score); at a quarter, the profile's peaks become the user's distinctive tastes rather than the library's furniture. A descriptor most of the library wears describes the library, not the user. |
| `TasteFacetPrevalenceFloor` | 8 | Carriers below which a facet is never generic regardless of share — in a 20-game library, five carriers of one genre is a small collection, not genericity. Protects small libraries (and test fixtures) from the cut. |
| `OnTasteMinAffinity` | 0.6 | Floor for the "right up your alley" shelf: the candidate must carry a descriptor at least 60% as loved as the user's most-loved *distinctive* one. Measured: admits a rotating pool of ~200 of 427 never-opened rows. |
| `ShelfFranchiseCap` | 1 | One franchise entry per shelf, hard, never relaxed — 14 unplayed Infinity Blades is the measured alternative. The rest of a franchise rotates through later days. Grouping key: title before the first colon, slugified, trailing numeral dropped (`Half-Life 2: Deathmatch` → `half_life`). Conservative on purpose: a false split costs a samey shelf; a false merge silently suppresses a valid recommendation. |
| `ShelfGenreCap` | 4 | Entries sharing one genre per 10-item shelf — below half, so no genre can majority a shelf. Soft: the relaxation pass refills when the eligible pool genuinely is that narrow. |
| `ShelfOverfetchFactor` / `ShelfProbeLimit` | 3 / 150 | Per-shelf shortlists are 3× the shelf size (slack for caps and cross-shelf claims); the probe union is capped at 150 ownerships — five shelves legitimately probe more than one list does, but never the whole library. |
| `PenaltyRecentlyPlayed` | 0.60 | Must dominate: max realistic positive sum ≈ 0.55 for a non-stale row. A game played yesterday cannot crack the feed's top even if it is installed, twice-bought and on-taste. |
| `PenaltyProbablyDone` | 0.30 | Sized to drop a qualifying row below the bounced midfield but not to zero — it still appears far down the feed, with a reason that says why it is far down. |
| `PenaltyRecentlySurfaced` | 0.20 | Enough to rotate a shown item behind its unshown near-peers; not enough to bury a strong stale-but-patched hit the user keeps ignoring — if the top item is genuinely the top item, repeating it once or twice is honest. |
| `SurfacedWindowDays` | 3 | Days back the recently-surfaced set reaches, **excluding today** (a set that included this morning's picks would penalise them on the afternoon refresh — dealing the new hand the day-seed exists to prevent). Sized from the smallest real pool: with S slots and a window of W days, W×S releases carry the penalty, and rotation requires W×S < pool. The smallest measured shelf pool is `stale_but_patched` at ~20 against 6 slots; 3 is the largest whole window under it (18 < 20). |
| `EndorsementWindowDays` | 3 | Days after a surfacing within which a Hoard-launched session still counts as answering the feed's pitch. Matches the surfacing window, and small for the same reason: past the rotation cycle the launch is the user's own idea, and crediting the feed would be the feed grading its own homework. |

## 6. Cold start: tiers and degradation

Tier is detected from evidence, not from install age, and the answer rides on the feed
(`RecommendationFeed.Tier`) so the UI can calibrate its confidence copy:

- **Tier 0 (ColdStart):** no sessions and no multi-reading snapshot history among the
  probed rows. Everything above marked Tier 0 still works, because the load-bearing signals
  (patch-after-dormancy, buckets, last-played, facets) are all retroactive.
- **Tier 1 (Settling):** any session, or any ownership whose snapshots show a real delta.
  Adds the tried-to-like-it bonus.
- **Tier 2 (Established):** ≥50 sessions spanning ≥8 weeks. Enables (future) cadence and
  return-latency work; today it only labels the feed.

Detection is a bounded sample (shortlist ∪ most-recently-played), which is where history
physically accrues first — an approximation, documented here, cheap by construction.

**The GDPR importer is the cold-start lever** (design doc §5.4): when it lands, it
backfills `sessions` with `detection_method='import'` and deep playtime history, which
flips the library to Tier 1/2 retroactively, resurrects the shelf-time signal
(`acquired_at` from `ExternalLicenses`), and makes return-latency computable. This module
needs **no changes** for that: it reads the same tables and the tier detector will simply
find the evidence.

## 6a. The shelf surface

One ranked list is not a feed: it buries every story below the first. The product surface
is **several shelves with different reasons** — each a different query over the same scored
pool, each stating its pitch in one line, and *every one of them fully populated at
Tier 0*. This is deliberate strategy, not scope creep: some of these shelves overlap what a
storefront could show (a taste-matched backlog rail), and that is fine — ours runs on the
same feed that keeps getting better with history the storefronts never keep, so parity on
day one compounds into a lead.

Shelves, in claim order (which is also presentation order — strongest story first):

| Shelf | Membership rule (Tier-0 facts only) | The pitch |
|---|---|---|
| `patched_while_away` | bucket `stale_but_patched` | "Major updates landed after you stopped playing." The headline; the moat fact leads. |
| `worth_another_look` | bucket `bounced`, probably-done NOT fired | "You committed real hours past the refund line, then drifted." The §6.1 highest-value pile as its own rail. |
| `ready_to_play` | installed, minutes < refund line, not stale | "Already on disk, nothing sunk." Install state is a Tier-0 fact and zero friction is a real argument. |
| `barely_touched` | 1 ≤ minutes < refund line, not stale | "Under 2 hours in — you never really tried it." §6.1's *sampled* stratum, told honestly. |
| `on_your_taste` | never-opened, affinity ≥ 0.6, no mode mismatch | "Sealed, and it matches where your hours actually go." The only shelf the taste tiebreaker *leads*; the prevalence cut is what makes its sentence honest. |

Rules that make it a feed rather than five lists:

- **One work, one shelf.** A work is claimed by the earliest shelf whose rule it meets and
  cannot appear again that day. Two rails fronting the same game is the same-five-games
  failure sideways.
- **Shelves own their stories.** The sub-refund shelves exclude the stale bucket: a patched
  game that missed the patched shelf's ten slots waits for that shelf's rotation rather
  than leaking its (stronger) patch story onto a rail telling a different one.
- **Diversity caps** (franchise hard, genre soft with a relaxation refill) — §5's table.
  The passes decide *membership*; display order is still strictly by score, so a
  penalty-carrying row can never sit above a stronger row that was merely genre-capped.
- **Rotation is engine-owned day to day.** The daily-seeded jitter rotates near-ties, and
  the shelfware/taste pools are near-tie-dense by construction (measured spread of the
  taste shelf: ~0.008 across its top ten, vs. jitter amplitude 0.03) — two consecutive days
  produce visibly different tails on the big shelves with no storage anywhere. The
  caller-fed recently-surfaced set is the *cross-day memory* for the small pools
  (twenty stale games rotating through six slots needs someone to remember yesterday) —
  since §6b that memory is real: the `feed_surfacings` log, loaded through
  `FeedbackSets`, and measured on the real library it rotates four of the five shelves
  **completely** day over day with the jitter seed pinned.
- **Empty shelves are omitted**, never rendered blank. `CandidateCount` still says how big
  the scored pool was, so a UI can tell "quiet feed" from "empty library".

What each shelf gains as history accrues (the tiering is not flattened by Tier 0 being
good — that is the whole argument):

| Shelf | Tier 1 (weeks: snapshot deltas, sessions) | Tier 2 (months) / GDPR import |
|---|---|---|
| `patched_while_away` | Update polling accrues coverage; bounce-vs-single-session shape sharpens which stale rows lead | Return-latency learns whether *this user* ever answers patch calls, and after how long |
| `worth_another_look` | Tried-to-like-it separates "six attempts" from "one evening" — already firing on the real library's five multi-snapshot rows | Import backfills the true bounce shape of the whole pile; session-length fit gates the 60-hour entries |
| `ready_to_play` | Sessions reveal installs that get launched but not logged by stores | Cadence says *when* a ready game actually fits (the Tuesday-night gate) |
| `barely_touched` | Distinguishes "sampled once" from "sampled five times and bounced off the door" | Import recovers sampling dates Steam's local files have forgotten |
| `on_your_taste` | Every new committed game re-weights the profile | Genuine taste clusters replace single-facet affinity; shelf-time (acquired→first-played) resurrects with `ExternalLicenses` dates |

Verified on the real library (2026-08-27 copy, feed run end-to-end): all five shelves
populate, reasons read as intended ("You put 2.4 hours into this in 2017 and it has had an
update since, most recently 'SPOTREP #00121'. Sandbox is where your hours go, and this is
one."), the mode-mismatch sentence appears exactly where it should (Star Wars: The Old
Republic demoted to the bottom of the patched shelf, saying why), and consecutive seeds
rotate the taste shelf completely.

## 6b. The feedback loop (2026-08-27)

The user can now steer the model — and the design's first commitment is that steering
stays inspectable and reversible, because feedback is exactly where recommenders turn
into black boxes. Everything below observes one split: **what the user said and what the
feed showed are truth, stored** (migration `0011_feed_feedback.sql`); **everything those
facts do to a score is derived, recomputed per request**, same as every §6.1 bucket.

### The vocabulary: two negatives, no explicit positive

| Kind | Semantics | Storage |
|---|---|---|
| `not_interested` | "You were right, I'm done with this game." Durable; holds until revoked. | `feed_verdicts` row, no expiry (the CHECK forbids one). |
| `snoozed` | "Not now." Lapses by itself. | `feed_verdicts` row, expiry REQUIRED (the CHECK forbids omitting it). Default length `FeedVerdictKinds.DefaultSnooze` = 30 days: "not now" naturally reads at month granularity — shorter is just the rotation the surfacing memory already provides, longer drifts into a dismissal the user didn't give. |
| *(launch endorsement)* | "I answered the pitch by playing." | **Not stored** — derived: a JOIN between `sessions.attributed_by = 'launch'` (M3b) and `feed_surfacings`, within `EndorsementWindowDays`. |

The two negatives are kept apart because they are different intents, and collapsing them
loses the information forever; the CHECK constraint is 0010's argument re-applied — the
vocabulary is ours and closed, so a third kind is a schema change and has to be one.

**There is deliberately no thumbs-up.** The positive signal is behavioural: M3b already
records, with no UI and no asking, that the user clicked Play *inside Hoard* — and a
launch-attributed session while the game was on the feed is the user endorsing the pitch
with their time. An explicit positive affordance would duplicate that with strictly worse
data (a click costs nothing; forty minutes costs forty minutes), and an unpressed
thumbs-up teaches the user the feedback surface is decoration. `attributed_by` is
three-valued and the join honours it: `'inferred'` (started from Steam and merely
detected) and NULL ("not recorded" — every pre-M3b session) never count. If an explicit
positive ever earns its place, it is one new CHECK'd kind away.

### What each fact does to a score — one effect apiece, each one sentence

- **Not-interested / snooze → hard exclusion (§4), widened to the work.** Not a
  penalty: arguing with an explicit verdict is nagging. The stored fact is the release
  the user clicked; the widening to its work (so a merged GOG twin cannot resurface the
  game) is a query, recomputed per request.
- **Surfacing → the recently-surfaced penalty (−0.20), window `SurfacedWindowDays`,
  excluding today.** Today's exclusion is what keeps the feed stable within a day: the
  morning's picks are in the log, and penalising them on the afternoon refresh would
  deal the new hand the day-derived shuffle seed exists to prevent.
- **Endorsement → taste testimony below the refund line.** The profile's evidence floor
  (§3) gets its one exception: an endorsed release testifies with the √minutes it
  actually has. That currency is the anti-overfit argument in arithmetic — three
  feed-driven launches (√40 ≈ 6 each) cannot outvote one committed game (√6000 ≈ 77),
  so a handful of clicks is *incapable* of reshaping a profile built from years of
  hours. Endorsed sub-refund rows stay out of the mode tally: reclassifying how the
  user plays needs commitment, not one answered pitch.

### What dismissals deliberately do NOT do

A dismissal never touches the taste profile. "Not interested in this game" is a verdict
about one game, not about its genre: at n = 3 dismissals, inferring facet-distaste is
noise, and because facets are shared by dozens of games, punishing a facet for three of
its carriers would suppress a 50-game pool — the monoculture failure arriving through
the feedback door. Exclusion-only is the conservative answer, and it is stated here so
nobody "improves" it casually: the feed's diversity survives any number of dismissals
because each one removes exactly one work from a ~1,000-row candidate pool.

### Reversibility and inspection

Verdicts are **append-and-revoke, never edited, never deleted**: undo stamps
`revoked_at` on the active rows (`RevokeVerdictsAsync`), a lapsed snooze needs no write
at all, and `GetAllVerdictsAsync` returns the entire history — dismissed → undone →
dismissed again is two rows and a stamp, all visible. "Active" is computed at read time
(`revoked_at IS NULL AND (expires_at IS NULL OR expires_at > asOf)`), never stored, so
there is no cached state to drift. The surfacing log is equally inspectable: any
recommendation's "why am I seeing this again / why did this vanish" has a row to point
at.

### The plumbing (who reads, who writes)

- `Hoard.Core`: `FeedVerdict` / `FeedSurfacing` / `FeedEndorsement` records and
  `IFeedFeedbackRepository`. `Hoard.Data`: the implementation over 0011.
- `Hoard.Recommend.FeedbackSets` is the **read-side bridge**: `LoadAsync(repo, asOf,
  tuning)` computes the four id sets (`NotInterested`, `Snoozed`, `RecentlySurfaced`,
  `Endorsed`), `Apply(request)` stamps them on. The engine still stores nothing and
  never writes; its API is unchanged but for the new `EndorsedReleaseIds` set.
- **The App-layer contract** (for whoever wires the UI; nothing below is built here):
  1. Before computing: `sets = await FeedbackSets.LoadAsync(feedbackRepo, now, tuning)`,
     then `engine.GetShelvesAsync(sets.Apply(request))`.
  2. After computing: `feedbackRepo.RecordSurfacedAsync(FeedbackSets.SurfacingsOf(feed,
     now))` — idempotent per (release, day), so refreshes are free.
  3. On "not interested": `RecordVerdictAsync` with kind `not_interested`, no expiry.
     On "not now": kind `snoozed`, `ExpiresAt = now + FeedVerdictKinds.DefaultSnooze`
     (or a UI-offered duration — the schema stores the explicit expiry).
  4. On undo: `RevokeVerdictsAsync(releaseId, kind, now)`.
  5. The settings/inspection surface renders `GetAllVerdictsAsync` and offers revoke.
  No pruning anywhere: the surfacing log is ~30 rows/day and is load-bearing twice
  (rotation window + endorsement join); pruning it would silently erase endorsement
  evidence.

### Verified on the real library (2026-08-27 copy, 997 candidates)

- **Verdicts:** dismissing Arma 3 + Counter-Strike 2 and snoozing Deep Rock Galactic
  removed exactly those three, same day, same seed; the patched shelf refilled with
  Abiotic Factor / Forever Skies / Stationeers rather than collapsing. Undoing the
  Arma 3 dismissal put it straight back; the history shows all four rows including the
  revocation stamp.
- **Rotation:** with the day-1 seed **pinned** and no memory, day 2 kept 30/30 items —
  the M8 gap made visible (jitter alone rotates nothing unless the seed changes). With
  the memory loaded, day 2 kept **0/6 on four of five shelves**. The fifth,
  `ready_to_play`, kept 6/6 because its eligible pool is exactly six installed
  sub-refund games — the penalty drove its scores down (Fez to −0.06) but there was
  nothing to rotate to, which is the documented honest behaviour: repeating a genuinely
  exhausted pool beats hiding it.
- **Endorsements:** the real library has 8 sessions, none yet `attributed_by =
  'launch'` — and the historical feed was never logged, so there is nothing to
  backfill (inventing surfacings for feeds M8 showed but never recorded would be
  inventing history, 0010's rule). The signal accrues from the first logged feed and
  the first Hoard-launched session onward; the taste effect is tested end-to-end on
  fixtures.

## 7. Deliberately deferred (and where each would plug in)

- **Session-length fit** ("a 60-hour CRPG is not a Tuesday-night suggestion"): needs both
  a session cadence (Tier 2) and per-game expected-commitment data (HLTB, unresolved
  [VERIFY]). Would become a Tier-2 value on `RecommendationScorer`.
- **Return latency** as a scoring input (how long this user's round trips take): needs
  months of sessions or the GDPR backfill; today it would be fit on five data points.
- **Will-it-run / Dead bucket**: §6.1 lists Dead (delisted, no viable platform); nothing
  ingests that fact yet. When it exists it becomes hard exclusion #6.
- **Session-note ratings** as taste/verdict evidence: the table is empty and the journal
  prompt is opt-in; wire it into the probably-done gate when real rows exist.
- **Genre-conditional thresholds** (2h in a roguelike vs. 2h in a CRPG): §6.1's own open
  item; arrives with HLTB or per-genre config, lands on `FairShakeMinutes`/bucket floors.
- ~~**Feed diversity quotas**~~ **Built** (2026-08-27): the shelf surface owns them —
  franchise and genre caps per shelf, §6a. What remains UI territory is only styling.
- **"Short enough for tonight"** as a shelf: needs per-game expected-commitment data
  (HLTB, unresolved [VERIFY]) — the Steam "Short" tag is too sparse and too voted-on to
  carry a shelf's honesty. Same plug-in point as session-length fit.
- **Any learned component.** One user's library is not a training set, and the
  explainability contract (§4) is load-bearing.

## 8. Failure modes designed against

| Failure | Defence |
|---|---|
| Same five games forever | Recently-surfaced penalty — fed since §6b from the persisted `feed_surfacings` log, so rotation no longer depends on the jitter seed happening to change — + daily-seeded jitter inside score bands + one-work-one-shelf claims and the franchise/genre caps (§6a). |
| Feedback becomes a black box | Feedback facts are append-and-revoke rows the user can list and undo (`GetAllVerdictsAsync` / `RevokeVerdictsAsync`); every effect is a query over them, so "what have I told it and what is that doing" always has an exact answer (§6b). |
| Three dismissals collapse the feed into a monoculture | Dismissals are exclusion-only — they never touch the taste profile (§6b's stated non-effect) — and endorsements pay for taste testimony in √minutes, the same currency as played hours, so no handful of clicks can outvote the library's history. |
| A shelf that is one franchise five times | `ShelfFranchiseCap` = 1, hard, measured against the 14-entry Infinity Blade pile. |
| "Matches your taste" via a tag half the library wears | The prevalence cut: facets carried by >25% of the library cannot testify. Without it, 266 of 427 never-opened rows scored a perfect match — a metric measuring nothing. |
| Recommending games the user will never play with strangers | Mode-mismatch demotion, evidence-gated, with the sentence said out loud where the row does surface. |
| Resurfacing the finished 200-hour game | Retired is a hard exclusion before scoring, patches notwithstanding — same precedence §6.1 encodes. |
| Nagging about correctly-abandoned games | Probably-done penalty with an explanation that *says* "you were probably right"; not-interested set for the user's explicit verdict. |
| Blank feed on day one | Every load-bearing signal is retroactive; tier detection widens confidence instead of gating output; the shelfware base value keeps the pile ranked rather than empty. |
| Unexplainable output | Reasons are composed from the same contributions that produced the score; a signal that cannot be explained in one sentence has nowhere to hide in the API shape. |
| Silent history-shape lies | 86400/1970 sentinel handling is upstream (migration 0008, `SteamTime`); null last-played beside real minutes is read as maximally dormant, never as fresh. |
| Score worship | No stored score column exists; the feed is recomputed per request and the request carries every threshold, so two callers can disagree and both be right. |

## 9. Wiring it in later (not now)

The intended composition: the UI (or a background service) constructs
`RecommendationEngine` from the same DI container as everything else and renders the
feed. The feedback sets now have real storage (§6b): the composition root registers
`FeedFeedbackRepository`, and `FeedService` follows the five-step contract in §6b — load
`FeedbackSets`, apply, compute, record surfacings, and route the dismiss / snooze / undo
commands to the repository. The UI affordances themselves (the "not for me" button, the
inspection list) are the remaining unbuilt piece, owned by the App layer.
