# Hoard.Recommend — the scoring core

**Status:** built and tested, not yet wired into the UI or composition root.
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

## 3. Signal inventory

Weights are on positive signals in [0,1] value space; penalties subtract. The score is a
plain weighted sum — transparent, inspectable, no renormalisation (a missing signal
contributes zero and the gap is *visible*, which is the honest way to degrade).

| Signal | Tier | Weight (default) | What it says in one sentence |
|---|---|---|---|
| **Patch after dormancy** | 0 | +0.40 | "A major update landed since you stopped playing." Bucket `stale_but_patched` — the app's headline fact, computed by the §6.1 query from the correlated build-push + announcement pair. Retroactive, so available on day one. |
| **Commitment shape** | 0 | +0.25 | Where the playtime sits against §6.1's refund line: bounced-just-past-the-line peaks (they committed and gave up — the highest-value pile), decaying toward the retired floor; sampled (1–119 min) sits above never-opened (they showed intent); never-opened is the wide flat base. |
| **Dormancy** | 0 | +0.15 | How long since last played, ramping from the fresh window to saturation at 2 years and staying flat (see §2 for why it must not decay). Null date beside real minutes reads as "unknown, certainly ancient" = fully dormant, matching the bucket query's reasoning. |
| **Taste affinity** | 0 | +0.10 | The candidate carries a genre/theme/tag that the user's actual hours concentrate in. Explicitly a **tiebreaker** for the 754-row shelfware pile, not the lead — genre similarity is the commodity the charter says loses to incumbents. Profile is playtime-weighted (√minutes, refund line and up), so retired games — excluded as candidates — still testify about taste. |
| **Tried to like it** | 1 | +0.10 | Distinct return episodes (snapshot rises or sessions beyond the first): 40 minutes across six evenings is a different fact from 40 minutes once. Zero until history accrues; a bonus, never a prerequisite. |
| **Installed** | 0 | +0.05 | Zero friction: it is on disk right now. |
| **Bought twice** | 0 | +0.05 | The same work owned on 2+ stores is a purchase made twice — intent money can measure. Fires only after cross-store merges are confirmed (see §2). |
| **Recently played** (penalty) | 0 | −0.60 | Played within the fresh window — not forgotten, so not this feed's business. Sized to sink anything: no combination of positives outruns it into the top of a realistic feed. |
| **Probably done** (penalty) | 0 | −0.30 | Deep in the bounced pile (a fair shake of hours), deeply dormant, and nothing has changed since — the model's way of saying "you were right to drop this" instead of nagging. The contribution's explanation says exactly that, which is the charter's honesty requirement made concrete. |
| **Recently surfaced** (penalty) | 0 | −0.20 | Caller-supplied set of releases the feed showed recently — the anti-"same five games forever" mechanism that needs no storage of ours. |
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
   drop it" verdict, permanent until they change it.
3. Releases in the caller's **snoozed** set — the temporary form of the same thing.
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
| `PenaltyRecentlyPlayed` | 0.60 | Must dominate: max realistic positive sum ≈ 0.55 for a non-stale row. A game played yesterday cannot crack the feed's top even if it is installed, twice-bought and on-taste. |
| `PenaltyProbablyDone` | 0.30 | Sized to drop a qualifying row below the bounced midfield but not to zero — it still appears far down the feed, with a reason that says why it is far down. |
| `PenaltyRecentlySurfaced` | 0.20 | Enough to rotate a shown item behind its unshown near-peers; not enough to bury a strong stale-but-patched hit the user keeps ignoring — if the top item is genuinely the top item, repeating it once or twice is honest. |

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
- **Feed diversity quotas** (no five soulslikes in a row): jitter + recently-surfaced
  demotion cover rotation; genre quotas are a UI-composition question to solve when the
  feed has a UI.
- **Any learned component.** One user's library is not a training set, and the
  explainability contract (§4) is load-bearing.

## 8. Failure modes designed against

| Failure | Defence |
|---|---|
| Same five games forever | Recently-surfaced penalty (caller-fed) + daily-seeded jitter inside score bands. |
| Resurfacing the finished 200-hour game | Retired is a hard exclusion before scoring, patches notwithstanding — same precedence §6.1 encodes. |
| Nagging about correctly-abandoned games | Probably-done penalty with an explanation that *says* "you were probably right"; not-interested set for the user's explicit verdict. |
| Blank feed on day one | Every load-bearing signal is retroactive; tier detection widens confidence instead of gating output; the shelfware base value keeps the pile ranked rather than empty. |
| Unexplainable output | Reasons are composed from the same contributions that produced the score; a signal that cannot be explained in one sentence has nowhere to hide in the API shape. |
| Silent history-shape lies | 86400/1970 sentinel handling is upstream (migration 0008, `SteamTime`); null last-played beside real minutes is read as maximally dormant, never as fresh. |
| Score worship | No stored score column exists; the feed is recomputed per request and the request carries every threshold, so two callers can disagree and both be right. |

## 9. Wiring it in later (not now)

The intended composition: the UI (or a background service) constructs
`RecommendationEngine` from the same DI container as everything else, persists the
not-interested / snoozed / recently-surfaced sets under `settings` or a list, and renders
`RecommendationFeed`. None of that is built here, on purpose — the charter requires this
module to prove itself standalone first.
