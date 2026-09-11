# Winnow.Recommend — the scoring core

**Module:** `src/Winnow.Recommend`, depends on `Winnow.Core` only.
Bucket definitions: [build specification §6.1](../game-library-design.md#61-derived-buckets).

This document defines the scoring signals, tuning defaults, evidence requirements, shelves
and explanation contract. `RecommendationTuning` carries scoring parameters; changing a
default does not require a database migration. Flat and shelf feeds share the scoring core,
feedback, undo and surfacing memory. Desktop and fullscreen use their own presentation state.

---

## 1. What the module does, and does not

Provider plugins can add independent recommendation shelves through the App-layer
`PluginFeedService`. They do not modify this scoring model. The host supplies eligible owned
game groups, validates their returned handles, scores and explanations, and uses the same
feedback commands on both presentations. Dismissal and snooze suppression apply before data
reaches a plugin and before returned cards can appear. Plugin scores are not compared with
built-in scores; each provider owns its named shelf. The plugin contract and lifecycle live in
`docs/plugins.md` and `game-library-design.md` §5.1.

Built-in shelves publish independently of optional providers. Their bounded supplement
appends only to the generation that requested it, preserving existing cards and viewport
impressions. A feedback change retires an outstanding supplement so it cannot reintroduce
a game evaluated before the verdict. Fullscreen retains the focused game when a shelf arrives.

`RecommendationEngine.GetFeedAsync(request)` reads the library through `Winnow.Core`
repository interfaces and returns a ranked list of **owned** games worth surfacing, each
carrying a one-sentence human-readable reason and a full per-signal breakdown. It writes
nothing, caches nothing, and decides no identity questions. Scores are derived values in
exactly the build specification §6.1 sense: computed on every read, comparable **within one feed**, never
stored, never trusted by anything else.

`GetShelvesAsync(request)` serves the scoring pass as themed shelves with their own pitches
and membership rules available at Tier 0. It also adds a separate Derelict shelf when
external lifecycle evidence warrants review (§6a); this shelf does not recommend playing.

Both built-in and plugin feeds use Core's `RecommendationGame` projection. The library
snapshot carries complete live identity state separately from its visible bucket rows:
changing account scope cannot erase a verdict on a hidden linked copy. Visible resolved
games are the population for installation, store counts, taste and mode evidence. Hidden
copies do not contribute those facts. Each game testifies once about taste and facet
prevalence, using its grouped playtime and the union of its visible descriptors.

The kept entry supplies the title. A viable installed copy supplies the action subject,
with the existing identity order breaking ties. Impressions, verdicts and undo retain that
explicit release even when the library tile's header names another copy. Explanations also
carry the contributing release ids and the release behind their quoted patch title.
History probes read all visible members. Overlapping session intervals count as one play
episode; snapshot rises contribute their largest per-ownership lower bound, since their
coarse intervals cannot safely be added across storefronts. Patch counts use correlated
pushes and the largest per-release count, matching the library's duplicate-store policy.
An unobserved sibling prevents a claim that the whole game's update history is quiet.

Unowned/store recommendations are explicitly out of scope (charter: priority 1 is
owned-but-unplayed; catalog data for anything else does not exist yet).

## 2. What the real data says (measured 2026-08-26, ~1,027 ownerships)

The design mandate is empirical over clever, so the model was designed against a read-only
copy of the author's live database, not against imagined data. The findings that shaped it:

| Fact | Measured value | Consequence for the model |
|---|---|---|
| Snapshot depth | 955 of 960 snapshot-bearing ownerships have exactly **one** snapshot; 5 have real deltas | This capture was Tier 0. Snapshot-shape signals must be bonuses, never prerequisites. |
| Sessions | **Zero** rows | Every session signal is Tier 1+. Cadence gating is deferred entirely. |
| `acquired_at` | 13 of 1,027 non-null (GOG only) | At this measurement, Steam/Epic lack acquisition dates. Winnow backfills Steam first-played dates and imports acquisition dates from the account licenses page; shelf time needs both facts for the same ownership. Dormancy remains the substitute where either date is absent. |
| `last_played_at` | 603 dated, spanning 2012–2026; 357 null; 12 null-with-minutes | Dormancy is the one longitudinal fact that IS retroactively available, because Steam's local files carry it. Lean on it. |
| Bucket counts at capture | never_played 754, bounced 244, stale_but_patched 20, retired 9, active 0 | The candidate pool is ~1,018 rows and 74% of it is `never_played`. Ranking *within* the shelfware pile needs a tiebreaker (taste affinity + deterministic jitter); nothing about the pile itself differentiates its members. |
| Dormancy distribution (dated, non-retired) | median **6.9 years**, p25 2.4y, p75 9.6y; 125 rows ≥10y | A dormancy score that decays after N years would suppress the older *half* of the library — the exact pile the app exists to surface. Dormancy therefore **saturates and stays flat**, and "too old to bother" is expressed only by the narrow probably-done penalty (§5). |
| Update events | announcements on 246 releases (retroactive to 2014); build pushes only since the poller started; 28 releases have a correlated major-update pair | The patch signal is real but its *coverage* is poller-recency-bound. Bucket membership (`stale_but_patched`) is the scoring input; per-release event detail decorates reasons only. |
| Cross-store ownership | works:releases are 1:1 (1,027:1,027) — duplicates live in `merge_candidates`, unconfirmed | The bought-it-twice signal exists in the schema but fires only after the user confirms merges. Kept, cheap, and honest about rarely firing yet. |
| Provisional names | 6 works | Excluded from the feed: a tile named "App 1203620" cannot carry an explainable recommendation. |
| Recently played | 13 ownerships touched in the last 14 days | The fresh-play suppression removes real rows, not hypothetical ones (Witchspire, played this morning, must not be "surfaced"). |

Re-measured 2026-08-27 (1,059 ownerships: 946 Steam / 99 Epic / 14 GOG, and the first 7
real sessions, a **Settling** capture). These measurements informed taste and shelf defaults:

| Fact | Measured value | Consequence |
|---|---|---|
| Affinity saturation | Without the prevalence cut, **266 of 427** never-opened rows scored a *perfect* taste match, because the profile's peak facets were "Action" (~⅔ of releases), "Singleplayer", "Adventure" | A max-shared-facet affinity over raw facets measures "carries a common tag", not taste. The **prevalence cut** (§5) excludes common descriptors: facets carried by >25% of the facet-carrying library stop counting as taste. The profile's peaks become Survival / Sandbox / Crafting — this user's actual, distinctive taste — and the never-opened pool at the 0.6 affinity floor becomes ~200 rows: months of rotation, not three favourites, not the whole pile. |
| Franchise clusters | 14 unplayed "Infinity Blade" entries; 5 "Star Wars", 5 "Civilization IV", 5 "X-COM", 3 "Half-Life" among the unplayed | Rank honestly by score and a shelf becomes one franchise five times — a broken feed even when every score is right. Hence the one-per-franchise shelf cap (§6a), which is grouping for *display variety*, never an identity decision (that stays Resolve's). |
| Mode mismatch | 261 committed games carry mode facets; **243 (93%) are single-player**. The never-opened pile holds **12 multiplayer-only** titles (Team Fortress Classic, Deathmatch Classic, H1Z1 Test Server…) | A genre-matched MMO in a solo player's feed is a false positive the facets can catch at Tier 0. Hence the mode-mismatch penalty (§3) — fired against real rows, with the sentence that says why. |
| Facet coverage by store | Steam 861/946 releases carry genres; Epic 42/99; GOG 9/14 | Facet-driven signals (taste shelf, mode mismatch, genre caps) reach all three stores, but Epic coverage is the thinnest — an Epic game absent from the taste shelf may be missing metadata, not missing appeal. |

## 3. Signal inventory

Weights are on positive signals in [0,1] value space; penalties subtract. The score is a
plain weighted sum — transparent, inspectable, no renormalisation (a missing signal
contributes zero and the gap is *visible*, which is the honest way to degrade).

| Signal | Tier | Weight (default) | What it says in one sentence |
|---|---|---|---|
| **Patch after dormancy** | 0 | +0.40 | "A major update landed since you stopped playing." Bucket `stale_but_patched` — the app's headline fact, computed by the build specification §6.1 query from the correlated build-push + announcement pair. Retroactive, so available on day one. |
| **Commitment shape** | 0 | +0.25 | Where the playtime sits against build specification §6.1's refund line: bounced-just-past-the-line peaks (they committed and gave up — the highest-value pile), decaying toward the retired floor; sampled (1–119 min) sits above never-opened (they showed intent); never-opened is the wide flat base. |
| **Dormancy** | 0 | +0.15 | How long since last played, ramping from the fresh window to saturation at 2 years and staying flat (see §2 for why it must not decay). Null date beside real minutes reads as "unknown, certainly ancient" = fully dormant, matching the bucket query's reasoning. |
| **Taste affinity** | 0 | +0.10 | The candidate carries a genre/theme/tag that the user's actual hours concentrate in. Explicitly a **tiebreaker** for the 754-row shelfware pile, not the lead — genre similarity is the commodity the charter says loses to incumbents. Profile is playtime-weighted (√minutes, refund line and up — with one exception: a feed-**endorsed** release testifies below the line with whatever √minutes it has, §6b), so retired games — excluded as candidates — still testify about taste. Facets above the **prevalence cut** (carried by >25% of the facet-carrying library) are excluded from the profile entirely: measured, they saturate the metric into meaninglessness (see §2's re-measurement). |
| **Tried to like it** | 1 | +0.10 | Distinct return episodes (snapshot rises or sessions beyond the first): 40 minutes across six evenings is a different fact from 40 minutes once. Zero until history accrues; a bonus, never a prerequisite. |
| **Installed** | 0 | +0.05 | Zero friction: it is on disk right now. |
| **Bought twice** | 0 | +0.05 | The same work owned on 2+ stores is a purchase made twice — intent money can measure. Fires only after cross-store merges are confirmed (see §2). |
| **Recently played** (penalty) | 0 | −0.60 | Played within the fresh window — not forgotten, so not this feed's business. Sized to sink anything: no combination of positives outruns it into the top of a realistic feed. |
| **Probably done** (penalty) | 0 | −0.30 | Deep in the bounced pile (a fair shake of hours), deeply dormant, and nothing has changed since — the model's way of saying "you were right to drop this" instead of nagging. The contribution's explanation says exactly that, which is the charter's honesty requirement made concrete. |
| **Recently surfaced** (penalty) | 0 | −0.20 | Caller-supplied set of releases the feed showed recently — the anti-"same five games forever" mechanism. The caller loads it from the `feed_surfacings` log via `FeedbackSets` (§6b); the engine still stores nothing. |
| **Mode mismatch** (penalty) | 0 | −0.10 | The candidate sits entirely on the wrong side of the single-player/online line for how this user demonstrably plays (93% single-player by committed game count, measured). Fires only under dominance (≥85% share over ≥20 mode-carrying committed games) and only against a candidate that is *exclusively* the other side; co-op without versus is a maybe, not a mistake. Sized to cancel a perfect taste match, not to bury — mode facets can be missing or wrong. |
| **Shuffle jitter** | 0 | +0.03 max | Deterministic per (seed, release) noise, seeded by the day by default. Big enough to rotate near-ties inside the shelfware pile, small enough to never reorder games a real signal separates. |

### Update coverage: an input, not an assumption

**Update coverage** is a named input (`CandidateFacts.UpdateCoverage`). Winnow polls Steam for update
signals, and coverage of a release begins when polling of that release begins, so an empty
update history means one of two indistinguishable things: nothing shipped, or Winnow was
never watching.

One recorded **announcement** for a release is the proof of coverage, because Steam's news
endpoint serves a release's whole history rather than a window — one stored announcement
means Winnow has seen that release's update history and would have recorded anything
later. Until that proof exists the coverage is `Unknown`, and no negative claim about the
release may be made by any signal or any sentence.

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
- One feed entry per **work**. Two ownerships of one game are one recommendation, not two
  slots, and the collapse happens before any shortlist capacity is spent (§4a).
- **The probably-done penalty requires observed update coverage.** "A fair shake of hours,
  deeply dormant, and nothing has changed since" is three claims, and only the first two are
  computable from bulk facts. The third needs proof that Winnow has read the release's
  announcement history (§3); without it the penalty is withheld entirely and the sentence
  never claims silence.
  `RecommendationScorer.HasProbablyDoneShape` is the coverage-free half, kept separate so
  the engine can decide which rows are worth reading update history for at all.

### Hard exclusions from play recommendations

1. **Retired**, including games with a newer patch. Games above the configured retired
   floor remain excluded from play recommendations.
2. Releases in the caller's **not-interested** set — the user's explicit "you were right,
   drop it" verdict, permanent until they change it. Widened to the release's **work**:
   after a cross-store merge, dismissing the Steam card must not let the GOG copy
   resurface the same game. The stored fact stays the clicked release (§6b); the widening
   is recomputed per request.
3. Releases in the caller's **snoozed** set — the temporary form of the same thing, same
   work-widening.
4. Works with **provisional names** — unexplainable tiles (6 rows in the initial measurement).
5. Everything the build specification §6.1 query already dropped upstream: consolidated demos/betas, and
   non-game entries (tools, soundtracks) under the default setting.
6. **Derelict** lifecycle groups: cancelled, offline, delisted, abandoned or dead. These
   enter only the dedicated review shelf, with no recommendation score or history probe.
   Inactive and unknown evidence do not justify exclusion. The game's derived bucket is
   authoritative, so a viable linked store copy can keep a game in the ordinary pool.

## 4a. Shortlist bounding and work collapse

Scoring is cheap; history is not. Sessions and snapshots are read per ownership, so only a
shortlist gets probed, and the rule choosing that shortlist has to be safe or the ranking is
a guess dressed as a ranking.

Unprobed history can add return-episode bonuses or enable the probably-done penalty by
establishing update coverage. Shortlisting therefore bounds both possible score changes.

`ScoreBounds` bounds both directions per candidate:

- `Upper` = preliminary score + the largest bonus a probe could still reveal.
- `Lower` = preliminary score − the largest penalty a probe could still reveal.

A candidate is dropped only when its `Upper` cannot reach the `Lower` of the k-th ranked
candidate. A dropped row provably could not have placed, whatever its history turns out to
say.

`MaxHiddenBonus` is **exactly** zero for a row with no minutes and no play date, because
sessions and snapshot rises both imply playtime. That exactness is what keeps the safe
shortlist cheap rather than merely correct: never-opened shelfware is most of a real library
and none of it can hold a surprise. Measured on a 200-work library shaped like the real one,
the safe bound probed **60 works** — the comfort floor — so correctness cost nothing.

**Resolved games are assembled before scoring or spending shortlist capacity.**
`RecommendationGame.Build` groups the visible bucket rows and chooses one action subject:
a viable installed copy first, then the existing identity order. Each resulting candidate
carries the group's play facts, visible ownership/release IDs and unioned descriptors;
history probes read those members as described in §1. Store counts use this same visible
population. The action subject is not chosen by comparing independently scored store copies.
`ScoreBounds.CollapseByWork` remains a defensive uniqueness pass; its highest-`Upper` tie
rule applies only if a caller supplies duplicate work candidates. The production assembly
already supplies one candidate per resolved game. `RecommendationFeed.WorkCount` reports
the resulting candidate-game count after eligibility exclusions.

**The shelf pass unions the shelf shortlists, interleaved rank by rank.** Each shelf produces its
own score-bound-safe shortlist; the union (`RecommendationEngine.ProbeUnion`) admits every
shelf's best candidate before any shelf's second best, and so on until the probe limit is
reached. Duplicates are dropped by ownership id, since one ownership can be shortlisted by
several shelves and is only ever probed once. Interleaving shares a bounded budget across
shelves and trims their tails rather than starving later shelves. The pass runs off the UI
thread inside `Task.Run`. In the 2026-09-01 capture (990 candidates, 966 works), 150 probes
cost 57.5 ms median and 376 probes cost 60.4 ms median.

## 5. Thresholds, and why each is that number

All live on `RecommendationTuning` with these defaults. The build specification §6.1 refund line
(120 minutes) is inherited from `BucketThresholds` and is the standard each of these tries
to meet: a number that means something.

| Parameter | Default | Justification |
|---|---|---|
| `FreshPlayWindowDays` | 14 | Steam's own definition of current activity: `playtime_2weeks` is the storefront's window for "playing it now". The one non-arbitrary recency number available. |
| `DormancySaturationYears` | 2.0 | p25 of the measured dormancy distribution is 2.4 years — a ramp that saturates at 2 treats roughly the older three-quarters of the dated library as fully dormant and stops pretending finer discrimination among 5-vs-9-year-old piles means anything. |
| `DeepDormancyYears` | 4.0 | Gate for the probably-done penalty. Past ~4 years the person who bounced is, in gaming-taste terms, a different player, and the median bounced game (5.7y dormant) sits beyond it — the penalty is *meant* to reach the middle of that pile, but only jointly with the fair-shake gate below, which is what keeps it narrow (6 rows in the initial measurement). |
| `FairShakeMinutes` | 2,000 | ~33 hours: about the published aggregate main-story-plus-extras completion time for story-driven games. Past it, "abandoned" usually means "finished with it", not "forgot it". Explicitly provisional — per-game expected-commitment data remains unverified, and this parameter is where per-game numbers would plug in. |
| `CommitmentFloorValue` | 0.15 | The bounced curve's value as playtime approaches the retired floor: near-retired games are near-finished, not forgotten, but stay above shelfware's floor because a 90-hour game someone left IS more interesting than a game never opened. |
| `ShelfwareBaseValue` | 0.35 | Never-opened base. Each individual shelfware row is weak evidence of intent (the pile is 412 rows of zero-and-dateless); the base keeps the pile in the feed without letting it outrank anyone with an actual history. |
| `SampledBaseValue` / `SampledSpanValue` | 0.50 / 0.20 | 1–119 minutes ramps 0.50→0.70: launching at all shows intent shelfware lacks, while remaining below the bounced peak — and the deliberate jump at 120 (0.70→1.00) *is* the refund line's semantics: crossing it is a different fact, not more of the same one. |
| `TriedToLikeSaturationEpisodes` | 3 | Coming back twice after the first taste is already "trying to like it"; requiring more before full credit would gate the signal on history depth the measured library will not have for months. |
| `Tier2MinSessions` / `Tier2MinSpanDays` | 50 / 56 | "Months in" made concrete: ~50 sessions across two months is when cadence/seasonality claims stop being anecdotes. |
| `HistoryProbeLimit` / `RecentProbeLimit` | 60 / 25 | The repository interfaces read history per-ownership, so the engine probes a shortlist rather than issuing 2,000 queries per feed. `HistoryProbeLimit` is the cap on the shortlist's **comfort floor**, not the shortlist's justification — the shortlist itself is score-bound safe and may exceed the floor when the bound says it must (§4a). 60 is where the measured bound landed anyway. `RecentProbeLimit` is tier detection only: the most recently played rows are where history concentrates (the 5 real multi-snapshot ownerships are all recent), and what they hold is directly observed, so they floor the estimate without entering the uniform draw that would be biased by them. |
| `TierSampleOwnerships` | 120 | Ownerships drawn uniformly from every row that could hold history, for the sampled tier estimate (§6). Roughly a third of the measured library's history-bearing rows: enough that the scale-up is not carried by a handful of rows, and cheap at two indexed point reads apiece. Only used when no global aggregate is available. |
| `TierSampleSeed` | `0x5715_0F5E` | Fixed salt for that draw. Deterministic so one library always samples the same rows: a tier that flickered between refreshes because the sample moved would be a worse answer than a slightly stale one, and a fixed seed makes the estimate reproducible when someone disputes it. Not the shuffle seed — the tier must not change because the day did. |
| `ReasonCharacterBudget` | 180 | Longest reason sentence a card may carry. One sentence is the contract (§6c), and 180 is the length at which one sentence stays one sentence: it fits the longest primary/secondary pair the selection rules can produce, quoted update title included, so the honesty clauses are never truncated away. Lower it and truncation starts deciding what the user is told. |
| `JitterAmplitude` | 0.03 | Below the smallest deliberate weight gap (0.05), so jitter can only reorder rows no real signal separates. |
| `PenaltyModeMismatch` | 0.10 | Equal to the taste weight on purpose: a perfect genre match on a game the user will never launch with strangers should net to zero, not to a recommendation. A demotion, never an exclusion — facets can be missing or miscoded, and a demotion is recoverable. |
| `ModeEvidenceMinGames` | 20 | Committed mode-carrying games before the profile may claim a dominant mode. Below it, a handful of purchases could fake dominance; at 20+ games with an 85% share, chance is off the table. The measured library has 261. |
| `ModeDominanceShare` | 0.85 | Seventeen-in-twenty: past it the minority mode is occasional experimentation, not a second taste the model should serve. Measured library: 0.93 single-player. |
| `TasteFacetMaxPrevalence` | 0.25 | The prevalence cut. "Action" sits on ~two-thirds of the real library and saturated the affinity metric (266 of 427 never-opened rows at a perfect score); at a quarter, the profile's peaks become the user's distinctive tastes rather than the library's furniture. A descriptor most of the library wears describes the library, not the user. |
| `TasteFacetPrevalenceFloor` | 8 | Carriers below which a facet is never generic regardless of share — in a 20-game library, five carriers of one genre is a small collection, not genericity. Protects small libraries (and test fixtures) from the cut. |
| `OnTasteMinAffinity` | 0.6 | Floor for the "right up your alley" shelf: the candidate must carry a descriptor at least 60% as loved as the user's most-loved *distinctive* one. Measured: admits a rotating pool of ~200 of 427 never-opened rows. |
| `ShelfFranchiseCap` | 1 | One franchise entry per shelf, hard, never relaxed — 14 unplayed Infinity Blades is the measured alternative. The rest of a franchise rotates through later days. Grouping key: title before the first colon, slugified, trailing numeral dropped (`Half-Life 2: Deathmatch` → `half_life`). Conservative on purpose: a false split costs a samey shelf; a false merge silently suppresses a valid recommendation. |
| `ShelfGenreCap` | 3 | Entries sharing one genre per 6-card shelf — half, so no genre can majority a shelf. Coupled to how many cards a shelf shows: 4 of 6 would be a two-thirds majority, so the cap moved with the shelf size. Soft: the relaxation pass refills when the eligible pool genuinely is that narrow. |
| `ShelfOverfetchFactor` / `ShelfProbeLimit` | 3 / 2,000 | Per-shelf shortlists are 3× the shelf size (slack for caps and cross-shelf claims). The probe union is the interleave of those shortlists (rank by rank, not shelf by shelf) capped at 2,000 ownerships. 2,000 is derived from cost, not shelf geometry: the shelf pass costs 46.6 ms median with zero probes (dominated by bulk reads) and 23 microseconds per probe (measured 2026-09-01, 990 candidates, 966 works), so 2,000 probes is where per-row history reading would equal the bulk reads, i.e. where the pass would double. What holds the union well below that on a real library is the score bound (`ScoreBounds.SafeShortlist`), not the cap; on the measured library the natural union is 376 of 966 works and the feed stops changing at ~300. The cap is the backstop for a library where the bound stops discriminating. |
| `PenaltyRecentlyPlayed` | 0.60 | Must dominate: max realistic positive sum ≈ 0.55 for a non-stale row. A game played yesterday cannot crack the feed's top even if it is installed, twice-bought and on-taste. |
| `PenaltyProbablyDone` | 0.30 | Sized to drop a qualifying row below the bounced midfield but not to zero — it still appears far down the feed, with a reason that says why it is far down. |
| `PenaltyRecentlySurfaced` | 0.20 | Enough to rotate a shown item behind its unshown near-peers; not enough to bury a strong stale-but-patched hit the user keeps ignoring — if the top item is genuinely the top item, repeating it once or twice is honest. |
| `SurfacedWindowDays` | 3 | Days back the recently-surfaced set reaches, **excluding today** (a set that included this morning's picks would penalise them on the afternoon refresh — dealing the new hand the day-seed exists to prevent). Sized from the smallest real pool: with S slots and a window of W days, W×S releases carry the penalty, and rotation requires W×S < pool. The smallest measured shelf pool is `stale_but_patched` at ~20 against 6 slots; 3 is the largest whole window under it (18 < 20). |
| `EndorsementWindowDays` | 3 | Days after a surfacing within which a Winnow-launched session still counts as answering the feed's pitch. Matches the surfacing window, and small for the same reason: past the rotation cycle the launch is the user's own idea, and crediting the feed would be the feed grading its own homework. |

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

**The tier measures the whole library.** Recommendation candidates exclude many actively
played games, so their history alone cannot establish the library tier.

Where a global aggregate is available (`ILibraryHistoryStatsRepository`, §9) it is used
verbatim. Where it is not, the fallback is a deterministic uniform draw of
`TierSampleOwnerships` rows over every ownership that could hold history, scaled back up to
the library, with the directly observed count as the floor under the estimate — a count of
rows actually read can never exceed the truth. Rows with no minutes and no play date are
excluded from the draw; that is exact stratification rather than bias, since they can hold
no history at all. `LibraryHistoryStats.IsEstimate` says which of the two answered, and a
scaled figure may gate behaviour but must never be shown to a user as a total.

The registered aggregate makes session count and span exact, so `Tier2MinSessions` and `Tier2MinSpanDays` compare against real totals. The normal App path avoids the fallback sample cost of up to 120 ownerships × 2 point reads per feed.

**Cold-start backfill** (design doc §5.4): Steam Replay adds monthly playtime snapshots from 2022 onward, ClientGetLastPlayedTimes adds first-played dates, and the account licenses and purchase-history importers add acquisition facts. Real snapshot deltas can establish Tier 1 on the first run. These sources do not reconstruct sessions or session-length distributions, so they cannot establish Tier 2 from an invented session count or recover exact return latency. Winnow records those facts while it runs.

## 6a. The shelf surface

One ranked list is not a feed: it buries every story below the first. The product surface
is **several shelves with different reasons** — each a different query over the same scored
pool, each stating its pitch in one line. Shelf membership is available at Tier 0;
each shelf appears only when the library has evidence for its pitch. Some overlap what a
storefront could show (a taste-matched backlog rail), and that is fine — ours runs on the
same feed that keeps getting better with history the storefronts never keep, so parity on
day one compounds into a lead.

Shelves, in claim order (which is also presentation order — strongest story first):

| Shelf | Membership rule (Tier-0 facts only) | The pitch |
|---|---|---|
| `patched_while_away` | bucket `stale_but_patched` | "Major updates landed after you stopped playing." The headline; the moat fact leads. |
| `worth_another_look` | bucket `bounced`, probably-done NOT fired | "You committed real hours past the refund line, then drifted." The build specification §6.1 highest-value pile as its own rail. |
| `ready_to_play` | installed, minutes < refund line, not stale | "Already on disk, nothing sunk." Install state is a Tier-0 fact and zero friction is a real argument. |
| `barely_touched` | 1 ≤ minutes < refund line, not stale | "Under 2 hours in — you never really tried it." The scorer's sampled stratum: played, but below the refund line. |
| `on_your_taste` | never-opened, affinity ≥ 0.6, no mode mismatch | "Sealed, and it matches where your hours actually go." The only shelf the taste tiebreaker *leads*; the prevalence cut is what makes its sentence honest. |
| `waiting_to_be_opened` | never-opened, uninstalled, not stale, without a qualifying taste match | "Already in your library, with no recorded play yet." Cold imports can populate it without invented taste or history. |

The final shelf adds no score or threshold. Existing shelfware scores and daily rotation
order it. Installed and taste-qualified games retain their stronger shelf stories, and
the normal visibility, feedback, diversity and reserve rules still apply.

Rules that make it a feed:

- **One work, one shelf, reserves included.** A work is claimed by the earliest shelf whose
  rule it meets and cannot appear again that day, whether it is on screen or held as a
  reserve. Two rails fronting the same game is the same-five-games failure sideways, and a
  reserve item that duplicates a visible card is not a replacement.
- **Visible slices are filled before any reserve.** The pass runs twice over the shelves:
  first filling every shelf's visible slice, then filling every shelf's reserve. Without
  that ordering, an early shelf holding spare cards could claim works a later shelf's
  visible slice needed, and a deeper ask would shrink the feed the reader sees.
- **Shelves own their stories.** The sub-refund shelves exclude the stale bucket: a patched
  game that missed the patched shelf's six slots waits for that shelf's rotation rather
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
  that memory comes from the `feed_surfacings` log, loaded through
  `FeedbackSets`, and measured on the real library it rotates four of the five shelves
  **completely** day over day with the jitter seed pinned.
- **Empty shelves are omitted**, never rendered blank. `CandidateCount` still says how big
  the scored pool was, so a UI can tell "quiet feed" from "empty library".

What each shelf gains as history accrues (the tiering is not flattened by Tier 0 being
good — that is the whole argument):

| Shelf | Tier 1 (weeks: snapshot deltas, sessions) | Deeper observed history and M5 backfill |
|---|---|---|
| `patched_while_away` | Update polling accrues coverage; bounce-vs-single-session shape sharpens which stale rows lead | Return-latency learns whether *this user* ever answers patch calls, and after how long |
| `worth_another_look` | Tried-to-like-it separates "six attempts" from "one evening" — already firing on the real library's five multi-snapshot rows | Replay snapshots recover monthly playtime shape where available; future session-length fit needs recorded sessions and expected-commitment data |
| `ready_to_play` | Sessions reveal installs that get launched but not logged by stores | Cadence says *when* a ready game actually fits (the Tuesday-night gate) |
| `barely_touched` | Distinguishes "sampled once" from "sampled five times and bounced off the door" | First-played backfill recovers the initial sampling date; later attempts require observed sessions or snapshot deltas |
| `on_your_taste` | Every new committed game re-weights the profile | Taste evidence grows with play; shelf-time (acquired→first-played) becomes available where account-license dates and first-played dates both exist |

Verified on the real library (2026-08-27 copy, feed run end-to-end): all five shelves
populate, reasons read as intended ("You put 2.4 hours into this in 2017 and it has had an
update since, most recently 'SPOTREP #00121'. Sandbox is where your hours go, and this is
one."), the mode-mismatch sentence appears exactly where it should (Star Wars: The Old
Republic demoted to the bottom of the patched shelf, saying why), and consecutive seeds
rotate the taste shelf completely.

### The reserve and `VisiblePerShelf`

A caller that holds replacement cards behind each shelf asks one pass for more than it
shows. `RecommendationRequest.VisiblePerShelf` declares how many of each shelf's
`MaxPerShelf` items actually reach the screen; the rest are a reserve. The engine cannot
infer the surface size from the depth alone — asking for ten and showing six is the same
`MaxPerShelf` as asking for ten and showing ten — so the caller states both, and two
properties of the feed depend on it.

First, every shelf's visible slice is filled before any shelf's reserve (see the
"Visible slices are filled before any reserve" rule above). Second, the reason ledger's
variety caps are sized to the surface, not to the depth — asking for twelve to show six
must not double how many of those six may cite the same supporting fact. The ledger is
allocated once per shelf and spans its entire depth, visible and reserve alike, which is
what makes a reserve card's sentence one the surface has already checked.

The screen is responsible for refusing to promote a card whose sentence a card on the
shelf is already saying, because a deep enough shelf reaches the end of its distinct
phrasings before it reaches the end of its candidates. Observed at a depth of ten on the
real library's `on_your_taste` shelf: two cards sharing a sentence at that depth
(2026-09-03).

When `VisiblePerShelf` is null (the default), the caller shows everything it asked for,
there is no reserve, and the pass behaves exactly as it did before this property existed.

Measured on a read-only copy of the real library (968 candidates, 968 works, tier
Settling, 2026-09-03): asking for ten per shelf and showing six produced the same five
shelves each showing six cards, in the same time as asking for six. The pass is dominated
by bulk reads, not by the per-row probes a deeper ask adds. Four of five shelves filled a
four-card reserve; `ready_to_play` filled none, its eligible pool being only four games.

The reserve is topped up continuously. After each swap, a backfill scores the feed again,
discards the visible slices and everything already on screen or queued, and merges the
remainder into the live queues. No shelf is rebuilt and no card on screen moves, which is
what makes it safe to run under a receipt whose undo the reader might still reach. The
pass returns fresh games because the dismissal that emptied the slot is already stored as
a verdict and hard-excluded from the next pass; every shelf shifts up by one, and what
arrives at the bottom is a game no queue has held. One backfill reads at a time; a second
request waits behind it, and a backfill from a pass the feed has since replaced is
discarded.

### Derelict: lifecycle review

Derelict follows the five recommendation shelves when eligible evidence exists. Its
membership comes from the same scoped, hidden-filtered, same-game bucket rows as the
library. Dismissal and snooze widen to the resolved game before either pool is assembled.
Each card carries the classifier's one-sentence reason and confidence, retained as
`ReasonEvidence.Lifecycle`; no taste or playtime score pretends to explain a shutdown.
The score is zero and the scoring-signal list is empty. Candidate, work and history-probe
counts describe ordinary recommendations only. Library maturity still measures the whole
library, including these games' real historical sessions.

Recently surfaced entries follow unseen entries, then the existing daily deterministic
shuffle rotates each group. No confidence cutoff is added here: classification owns its
evidence gates, and a second cutoff would conceal cases the library already explains.
The same `MaxPerShelf` depth holds visible cards and reserves; a deeper request leaves
the visible prefix unchanged. Genre and franchise caps do not hide lifecycle evidence.
Delisting and abandonment need not mean unplayability, so the shelf says some games may
still be playable. No local launch failure or low single-player population alone can
justify calling a game dead.

Lifecycle confidence expresses the strength of the available evidence. Its defaults are
conservative policy choices, not probabilities calibrated against a labelled dataset;
the percentage on the card must be read alongside its source reason. Play-history tiers
do not increase lifecycle confidence, and the feed suppresses its playtime-confidence
note when Derelict is the only shelf.

Lifecycle gates use `LifecycleTuning`, separate from the weighted play model. These are
initial conservative defaults requiring later evaluation against labelled real libraries;
they are not measurements of universal multiplayer population or development cadence.

Dead requires released status and explicit multiplayer-only metadata. Abandoned requires
unfinished status. Both need known old patch-note and announcement dates; missing feeds
cannot establish silence. A newer activity signal vetoes the corresponding quiet inference.

| Parameter | Default | Argument |
|---|---|---|
| `EvidenceFreshDays` | 30 days | A monthly recheck bounds how long mutable catalog assertions can exclude a game; older observations remain evidence history, not a current verdict. |
| `ActivityFreshDays` | 7 days | Player and review activity changes faster than catalog status, so a week-old activity snapshot cannot assert current engagement. |
| `PlayerHistoryDays` | 30 days | A month brackets the repeated population observations; distant samples cannot manufacture sustained current inactivity. |
| `LowPlayerCeiling` | 5 | A small lobby-sized population is a warning worth corroborating, never proof of shutdown; multiplayer mode, repeated measurements and other activity gates remain mandatory. |
| `LowReviewCeiling` | 2 | At most two recent reviews is sparse supporting testimony, not an independent measure of viability; Steam supplies both population and reviews. |
| `MinimumPlayerSamples` / `MinimumPlayerSpanDays` | 3 / 14 days | At least three observations across a fortnight resist a single off-peak reading; even these require separate development and communication evidence for dead. |
| `DeadQuietDays` | 365 days | A full annual cycle allows seasonal development and communication before silence can support the multiplayer inference. Missing dates do not pass. |
| `AbandonedQuietDays` | 730 days | Two annual cycles give unfinished projects a wider allowance than released multiplayer services; development and communication need known old dates. Known recent store activity vetoes abandonment; an unknown store-change date is not a prerequisite. |
| `CancelledConfidence` / `OfflineConfidence` / `DelistedConfidence` | 0.99 / 0.98 / 0.93 | Explicit source assertions outrank inference; delisting has weaker implications because availability and ownership differ. None guarantees that a particular installed copy cannot run. |
| `DeadConfidence` / `AbandonedConfidence` | 0.80 / 0.75 | Corroborated behavioral inference remains below explicit status; unfinished projects have particularly uncertain schedules. |
| `InactiveConfidence` / `ActiveConfidence` / `UnknownConfidence` | 0.55 / 0.65 / 0 | Low activity is weak negative evidence; observed activity supports a modest positive claim; absence of usable evidence contributes no confidence. |

The review shelf does not vary these gates by play-history tier. A cold library can carry
explicit catalog evidence immediately, while behavioral classifications wait for dated
external observations. Shared source silence, store failures and low single-player counts
cannot be promoted into proof of death. Identical source reasons may repeat on Derelict
and in its replacement reserve: varying prose must never conceal a lifecycle fact.

## 6b. The feedback loop

Feedback remains inspectable and reversible. Winnow stores verdicts, visible impressions
and launch-attributed sessions, then derives exclusions, rotation and taste evidence from
them. It does not train an opaque model from clicks.

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
records, with no UI and no asking, that the user clicked Play *inside Winnow* — and a
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
(`created_at <= asOf AND (revoked_at IS NULL OR revoked_at > asOf) AND
(expires_at IS NULL OR expires_at > asOf)`), never stored, so
there is no cached state to drift. The surfacing log is equally inspectable: any
recommendation's "why am I seeing this again / why did this vanish" has a row to point
at.

### The plumbing (who reads, who writes)

- `Winnow.Core`: `FeedVerdict` / `FeedSurfacing` / `FeedEndorsement` records and
  `IFeedFeedbackRepository`. `Winnow.Data`: the implementation over 0011.
- `Winnow.Recommend.FeedbackSets` is the **read-side bridge**: `LoadAsync(repo, asOf,
  tuning)` computes the four id sets (`NotInterested`, `Snoozed`, `RecentlySurfaced`,
  `Endorsed`), `Apply(request)` stamps them on. The engine still stores nothing and
  never writes. `EndorsedReleaseIds` carries the endorsement evidence into scoring.
- **The App-layer implementation:**
  1. Before computing: `sets = await FeedbackSets.LoadAsync(feedbackRepo, now, tuning)`,
     then `engine.GetShelvesAsync(sets.Apply(request))`.
  2. On visible viewport entry: `FeedView` checks card clipping, the active visible
     window and modal occlusion, then `FeedViewModel` asks `IFeedService.RecordSurfacedAsync`
     to store that release. Generation, backfill and reserve promotion alone record nothing.
     Entries deduplicate per (release, UTC day), including reloads; a card below the fold
     contributes no surfacing until scrolled into view.
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
  the first Winnow-launched session onward; the taste effect is tested end-to-end on
  fixtures.

## 6c. The explanation contract

Every card states its reason in **one sentence**, using the same evidence that produced its
score. The scorer selects facts; the builder chooses their wording and controls repetition
across the visible surface.

### The split

The scorer returns structured evidence. `RecommendationScorer.Explain` produces a
`RecommendationReason`: one primary signal, every supporting fact that fired in
strongest-first precedence order, and the `ReasonEvidence` both clauses may cite, read off
the same facts the score was computed from so a sentence cannot state a figure the ranking
never saw. At most one supporting clause is ever rendered; the list exists so a card whose
strongest fact is already spent on the surface can reach for the next one honestly (see
"The shelf ledger" below). `ReasonBuilder` renders one bounded sentence from the reason,
choosing wording from `ReasonPhrasebook`. What is true and how it reads are separately
changeable, and a caller wanting its own rendering reads `Recommendation.Explanation`
rather than parsing the sentence back apart.

`ReasonSignal` is the vocabulary, and every member is a fact about the game rather than a
phrase:

- **Openings:** patched-since-you-left, bounced, sampled, never-opened, launched-unmeasured,
  probably-done.
- **Supports:** tried-to-like-it, taste match, bought twice, installed, dormant,
  undated dormancy, online-only mismatch, solo-only mismatch, played recently,
  shown recently — or nothing, which is a legitimate answer.

### Selection, and its honesty rules

The honesty rules live in the selection, not in the wording.

**Primary,** in precedence order: a row demoted for being probably-done leads with that,
because the feed is required to be able to say "you were right to drop this"; then the
patched bucket, the headline fact; then the commitment shape (never opened, launched but
unmeasured, sampled, bounced).

**Secondary,** in precedence order: mode mismatch first, then fresh play. Both are demotions
whose effect the user can see, and a demotion the user can see the effect of but not the
reason for is an arbitrary ranking from their side. Then every supporting fact the opening
did not already tell, in one list, strongest first: tried-to-like-it, taste match, bought
twice, installed, dormancy, recently shown. The card takes the first entry the surface has
not already spent (see "The shelf ledger" below); when the list runs out the card says less.

Dormancy sits last because the opening can usually date the game itself, and the builder
additionally forbids the opening from spending `{year}` or `{age}` when the supporting
clause is telling the time story. "You put 5 hours in back in 2019, untouched for seven
years" is one fact told twice, which is the cookie-cutter failure in miniature.

### One sentence, bounded

`ReasonCharacterBudget` (180, §5) is sized above the longest pair the selection rules can
produce, so the clauses the honesty rules put there are never truncated away. The contract
test sweeps every producible primary/secondary combination against several evidence shapes —
including a game that knows everything about itself and one that knows almost nothing — and
asserts exactly one terminator, inside the budget, with no unfilled tokens.

### Variation, deterministically

Each signal carries several phrasings per clause. The variant is chosen by hashing the
game's own **release id**, never the shuffle seed, so a reload renders the identical
sentence while neighbouring cards do not read as siblings, and tomorrow's different hand is
not also a different wording. A variant whose tokens this game cannot fill truthfully is
skipped, which is why every list must carry at least one token-free variant; a variant
citing one of the game's own numbers is preferred over one that would be equally true of any
game, which is what stops a feed of "it's in your library" cards.

Real output, rendered from the live library:

> You have not seen "Reforged Eden", which arrived after you left, and nobody has opened it in 4 years.
>
> 4.3 hours of yours went into this before you drifted off, spread over 5 sittings rather than one.
>
> 15 hours in, well past the refund line, then nothing, quiet for 5 years now.
>
> A brief look, 22 minutes, and nothing after, untouched for 4 years.
>
> This has been waiting since you bought it, and nothing needs downloading first.
>
> 43 hours was your answer 7 years ago, and nothing since has argued with it.
>
> Something held your attention for 10 hours, then stopped, though 2 days is no time at all to have been away.

### Grammar and evidence

A supporting clause must stand after any eligible opening. Use a participle, appositive or
coordinate clause; a bare relative pronoun can attach to the wrong noun or verb.

A card may claim only what the engine can prove about that game. It must not claim a
library-wide rank, maximum, minimum, uniqueness or quantified share. Taste strength uses
normalised affinity from `ReasonEvidence`: `{strongFacet}` resolves only at or above
`OnTasteMinAffinity` (0.6). Below that gate, the descriptor may still be named without a
strength claim. Mode-mismatch wording must describe dominance, not pretend an 85% share
means every recorded hour.

`ReasonHonestyTests` checks all phrasebook variants for unsupported claims and exercises
multiple cards sharing the same taste descriptor.

### Variety across one surface

`ShelfReasonLedger` tracks both wording and supporting facts while cards render in stable
score, then release-ID order. It belongs to one shelf or flat-feed render.

- **Wording:** the release-ID hash selects the initial variant. If that variant is already
  used for the same signal and clause, select the next unclaimed variant. Prefer a fresh
  generic variant over repeating a specific one. Repeat only after the available variants
  are exhausted.
- **Facts:** take the first eligible supporting fact that has citation capacity. If its
  budget is spent, try the next fact that actually fired for this card. If none remains,
  omit the support. Count a citation only after the clause is rendered.
- **Claim identity:** taste citations use the descriptor name, so Sandbox and Roguelike
  have separate budgets. Other supporting facts use their signal, regardless of the
  number cited in the wording.
- **Required disclosures:** online-only mismatch, solo-only mismatch, played recently
  and shown recently are exempt from citation caps because they explain demotions.

The citation cap is `max(FactCitationFloor, visibleCards / FactCitationCards)`, using integer
division and defaults 2 and 3. Six visible cards therefore permit two citations of each
fact. The caller's `VisiblePerShelf` determines the budget; reserve cards do not enlarge it.
Inputs are clamped to at least one to avoid division by zero or a silent surface.

The same library in the same order renders the same wording. Moving a preceding card can
change a later card's choice because the ledger describes the visible surface.

### Quoting a store-authored update title

Update headlines are external text. Before quoting one, strip quotes, collapse whitespace,
cap length and remove sentence terminators. A run of `.`, `!`, `?` or `;` is a terminator
only when followed by whitespace or the end of the title. Internal version punctuation
survives, including `1.4.10.5`, `7.9.1b` and `2.03.a`. Thus `Patch 2.0. Read on!` becomes
`Patch 2.0 Read on`, retaining the version while keeping the card to one sentence.

## 6d. Offline replay and evidence boundaries

`tools/Winnow.Replay` captures a consistent SQLite snapshot and compares named tuning sets
outside the app. It depends on Data and Recommend; Recommend still references Core only.
The capture manifest records its UTC instant and database hash. Replay accepts that instant
only. Changing the request date cannot reconstruct earlier ownership, installation, metadata,
facets, settings or identity from their mutable present-day projections. Old databases must
have been captured when their state was current; a file modification date is not that proof.

The engine passes one instant into the library's dated queries and lifecycle classification,
session/snapshot reads, exact tier aggregate and feedback reads. Verdicts bind only after
creation and before revocation or expiry. Future launches cannot become endorsements. These
boundaries make dated evidence consistent; they do not make a live database a historical one.
The replay tool also refuses captured scoring observations with invalid or future timestamps.

Later outcomes come from a separate captured database and never enter scorer dependencies.
Both tunings rank the same complete frozen population before labels select the judged cohort.
External identifiers map subsequent outcomes to the captured identity groups; missing or
conflicting anchors remain unobserved. Labels require an impression strictly after the replay
day. A launch-attributed session within the outcome window is positive; a standing snooze or
not-interested verdict is negative. Conflicting positive/negative outcomes are excluded.
Because impressions retain only dates, same-day action ordering is ambiguous and excluded.
A matured impression without a qualifying action is a weak negative, reported but excluded
from metrics: legacy rows do not carry proof of which visibility-recording implementation
created them. TASK-10's current viewport behavior does not rewrite that history.

The report computes precision@k and MRR over positive and explicit-negative games in ranked
order, after removing unjudged games. It reports judged coverage and withholds precision
when fewer than k judged games remain. One capture is one query, so MRR equals that query's
reciprocal rank. The outcome window defaults to the model's three-day endorsement window;
the evaluator holds it fixed across tuning comparisons. These are observational, exposure-
biased measurements, not whole-library precision or causal evidence that a change improves
the feed. Tunings, thresholds, seed, capture hashes and assembly hashes accompany each report.
Capture commands, fixtures and measured results live in `docs/spikes/feed-replay.md`.

## 7. Deliberately deferred (and where each would plug in)

- **Session-length fit** ("a 60-hour CRPG is not a Tuesday-night suggestion"): needs both
  a session cadence (Tier 2) and per-game expected-commitment data (HLTB, unresolved
  [VERIFY]). Would become a Tier-2 value on `RecommendationScorer`.
- **Return latency** as a scoring input (how long this user's round trips take): needs
  months of recorded sessions and update responses; monthly Replay snapshots cannot supply exact return times.
- **Session-note ratings** as taste/verdict evidence: the table is empty and the journal
  prompt is opt-in; wire it into the probably-done gate when real rows exist.
- **Genre-conditional thresholds** (2h in a roguelike vs. 2h in a CRPG): an open data-source question; arrives with HLTB or per-genre config, lands on `FairShakeMinutes`/bucket floors.
- **"Short enough for tonight"** as a shelf: needs per-game expected-commitment data
  (HLTB, unresolved [VERIFY]) — the Steam "Short" tag is too sparse and too voted-on to
  carry a shelf's honesty. Same plug-in point as session-length fit.
- **Any learned component.** One user's library is not a training set, and the
  explainability contract (§4) is load-bearing.

## 8. Failure modes designed against

| Failure | Defence |
|---|---|
| Same five games forever | Recently-surfaced penalty — loaded from the persisted `feed_surfacings` log, so rotation does not depend on the jitter seed happening to change — + daily-seeded jitter inside score bands + one-work-one-shelf claims and the franchise/genre caps (§6a). |
| Feedback becomes a black box | Feedback facts are append-and-revoke rows the user can list and undo (`GetAllVerdictsAsync` / `RevokeVerdictsAsync`); every effect is a query over them, so "what have I told it and what is that doing" always has an exact answer (§6b). |
| Three dismissals collapse the feed into a monoculture | Dismissals are exclusion-only — they never touch the taste profile (§6b's stated non-effect) — and endorsements pay for taste testimony in √minutes, the same currency as played hours, so no handful of clicks can outvote the library's history. |
| A shelf that is one franchise five times | `ShelfFranchiseCap` = 1, hard, measured against the 14-entry Infinity Blade pile. |
| "Matches your taste" via a tag half the library wears | The prevalence cut: facets carried by >25% of the library cannot testify. Without it, 266 of 427 never-opened rows scored a perfect match — a metric measuring nothing. |
| Recommending games the user will never play with strangers | Mode-mismatch demotion, evidence-gated, with the sentence said out loud where the row does surface. |
| Resurfacing the finished 200-hour game | Retired is a hard exclusion before scoring, patches notwithstanding — same precedence build specification §6.1 encodes. |
| Nagging about correctly-abandoned games | Probably-done penalty with an explanation that *says* "you were probably right"; not-interested set for the user's explicit verdict. |
| Blank feed on day one | Every load-bearing signal is retroactive; tier detection widens confidence instead of gating output; the shelfware base value keeps the pile ranked rather than empty. |
| Unexplainable output | Reasons are composed from the same contributions that produced the score; a signal that cannot be explained in one sentence has nowhere to hide in the API shape. |
| The same frame with the nouns swapped | The scorer returns structure and the builder renders it, so a card avoids concatenation of the same fragments in the same order (§6c). Several phrasings per signal, selected from the release id. The contract test masks every number and proper noun and requires ten genuinely different histories to leave at least eight distinct sentence *skeletons* — distinct wording is not enough to pass. |
| Absence of evidence read as evidence of absence | Update coverage is a named input (§3): an unpolled release is `Unknown`, not quiet, and the probably-done penalty is withheld until one stored announcement proves Winnow has seen the release's history. Same rule elsewhere: `ReturnEpisodes` is null when unprobed and 0 when probed and empty, kept apart so "no evidence" is never reported as "never returned", and the tier's sampled estimate is flagged `IsEstimate` rather than passed off as a count. |
| Silent history-shape lies | 86400/1970 sentinel handling is upstream (migration 0008, `SteamTime`); null last-played beside real minutes is read as maximally dormant, never as fresh. |
| Score worship | No stored score column exists; the feed is recomputed per request and the request carries every threshold, so two callers can disagree and both be right. |

## 9. Wiring

`RecommendationEngine` is constructed from the DI container and rendered by
`FeedViewModel`. `FeedFeedbackRepository` stores the loop's facts (§6b), and `FeedService`
loads `FeedbackSets`, applies them and computes shelves. Actual visible viewport entry records surfacings through the App layer (§6b); generation alone records none. `FeedService` also routes dismiss, snooze and undo commands to the repository. `FeedCardViewModel` carries the two verdicts, undo and receipt countdown.

`LibraryHistoryStatsRepository` implements `ILibraryHistoryStatsRepository` in `Winnow.Data` and is registered in the App composition root. One SQL statement returns exact whole-library session count and span plus the count of ownerships with snapshot rises. The engine uses this aggregate when registered (`IsEstimate=false`) and retains the tested sampled fallback for callers that omit it (`IsEstimate=true`). Tier-detection sampling reads are skipped on the registered path; candidate history probes still serve scoring.
