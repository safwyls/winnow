# Expected-completion source study — 2026-09-11

IGDB is a candidate for completion-time research. No source has yet passed the copied-library
coverage and edition checks required by TASK-138. No completion provider or new recommendation
signal was implemented. The task stays open; this report records the public-source findings
and a reproducible measurement plan without inventing inaccessible records.

## Sources inspected

| Source | Access and terms evidence | Duration and refresh evidence | Current assessment |
|---|---|---|---|
| IGDB | Documented API; non-commercial access under Twitch's developer agreement; commercial partnership route; FAQ permits local caching | `game_time_to_beats` returns game IDs, average seconds for credits, ordinary play and full completion, total submission count and update time | Best candidate to measure; no per-store coverage result yet |
| HowLongToBeat | Homepage could not be opened by the research browser; no first-party API or reuse terms verified in this run | No authenticated or public dataset inspected | Inaccessible for this study; third-party scrapers do not establish permission or reliability |
| RAWG | Official page requires API keys and attribution, disallows redistribution; commercial pricing and the same page's free-commercial terms differ | Describes average Steam playtime and game update dates | Average played time does not establish completion; no completion data contract verified |

Sources: [IGDB API documentation](https://api-docs.igdb.com/#game-time-to-beat),
[HowLongToBeat](https://howlongtobeat.com/), and [RAWG API/terms](https://rawg.io/apidocs).
All were checked on the date above. Accessibility failures are tool observations, not claims
that a service lacks data or refuses every client.

IGDB documents game/version relationships and a limit of four requests per second. Its
completion schema provides no distribution or confidence interval; submission count alone
cannot calibrate uncertainty. An update timestamp is not a promised contribution cadence.
These are schema observations and resulting limits, not live measurements.
[IGDB documentation](https://api-docs.igdb.com/)

The current English [Twitch agreement](https://legal.twitch.com/en/legal/developer-agreement/)
returned navigation without its substantive terms in this browser. Its full current storage,
termination and redistribution requirements therefore remain unverified here. The IGDB FAQ
is useful access evidence, but this study does not certify that every proposed retention or
distribution model is permitted. No account was created, credential retrieved, source data
redistributed or provider contacted. RAWG's contradictory commercial statements likewise
need clarification before choosing that service.

## Library and edition coverage

Repository inspection confirmed that `src/Winnow.Enrich.Igdb/Apicalypse.cs` requests metadata
and edition relationships but no completion durations, and no completion provider exists.
An existing IGDB mapping is a possible join key, not evidence that a completion record exists
or describes the owned edition.

The attempted local research capture in the acquisition study was rejected by automatic
approval review: a full replay backup includes private account and settings data. That action
was not retried or bypassed. There is no authorized copied library for this study and no
authenticated completion API result. Counts below are deliberately unmeasured.

| Store | Copied ownership denominator | Exact edition matches with completion | Unmatched records | Inaccessible records |
|---|---|---|---|---|
| Steam | Unmeasured | Unmeasured | Unmeasured | Unmeasured |
| Epic | Unmeasured | Unmeasured | Unmeasured | Unmeasured |
| GOG | Unmeasured | Unmeasured | Unmeasured | Unmeasured |

Do not reuse the recommendation model's August library counts as this study's denominator.
There were zero live source queries, which is an execution count rather than zero coverage.

When authorized evidence is available, measure as follows:

1. Freeze a copied library with capture time and schema provenance. Select the same account
   scope and hidden-game policy as the intended recommendation population; record both raw
   per-store ownership counts and deduplicated resolved-game counts.
2. Join each owned release to its explicit external game/edition mapping. Report absent,
   ambiguous, parent-only and exact edition mappings separately. Never inherit a parent's
   duration for DLC, remakes, remasters or bundles solely because a link exists.
3. Request only documented source fields for the resulting IDs. Record request time,
   success/failure, absent record, nonpositive duration, available submission count and source
   update time. Missing records and inaccessible requests are different outcomes.
4. Report usable exact-match coverage per store against the complete denominator, with
   distributions of counts, ages and duration types. Preserve multiple source/edition rows;
   avoid mixing category means or blending platform estimates.
5. Repeat a dated sample fetch to measure observed change and failure behavior. A proposed
   refresh interval needs measured staleness and current terms; it is not a source guarantee.

## Supported uses and limits

These are research hypotheses, not measured recommendation improvements:

- **Whole-game duration context:** an attributed edition estimate may help a user understand
  a game's overall commitment. Display the duration type, count, age and uncertainty.
- **Threshold research:** compare an estimate with local playtime, but do not convert the
  ratio into a completion percentage, retirement verdict or reason to nag an abandoned game.
  Replays, idling and optional content make that inference unreliable.
- **Session fit:** a long game can support short sittings. Whole-game duration says nothing
  about save opportunities, mission lengths or interruption costs. Local session history,
  explicit user preferences or independently sourced cadence evidence remain possible paths.
- **Finishing tonight:** requires a credible remaining-progress estimate and a time budget,
  beyond average total completion. No current source evaluation establishes that promise.

No source is yet validated for production use, so no implementation follow-up was created.
After coverage and terms checks establish viability, scope a separate task around the uses
actually supported; keep availability, edition provenance, caching and truthful uncertainty
visible in its acceptance criteria. Desktop and fullscreen behavior remain unchanged by this
research; any future provider or presentation task must cover both surfaces.
