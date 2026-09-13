# Stats: store breakdowns and gameplay views

Recon date: 2026-09-12. Source baseline: `0125c67`, after the currency dashboard.
Tracked by TASK-243. This is a feasibility assessment, not an implementation specification
or a commitment to build the proposed views.

## Result

A separate **Gameplay** view is feasible without adding an importer. Winnow already holds
scoped library facts, recorded sessions, store attribution and optional journal entries.
The strongest first additions are recorded hours over time, top played games, session-length
distribution and current library composition. They answer different questions from spending.

Use **Stats → Gameplay / Spending**, with a store filter inside each view. Gameplay can
support multiple stores now; spending can only show Steam today. Additional spending stores
need new transaction importers, not just another filter button. Keep currencies separate
within each store, as the current dashboard already does.

This task changes the desktop rail to **STATS** and makes its tooltip store-neutral. The
current content still says **Steam account**, which accurately identifies its source.
Fullscreen already uses the neutral **Activity → Library summary** entry and remains there.
No new store selector or gameplay view is implemented by this spike.

## Method and limits

Inspected current Core records, repository SQL, store adapters, plugin contracts, the
desktop/fullscreen stats paths and existing regression tests. No live account or launcher
files were read, no external APIs were queried, and no personal-library completeness or
performance measurements were made. “Available” below means the code can retain that fact;
it does not mean every user, store or game has supplied it. Effort labels describe relative
implementation complexity, not measured delivery times.

## What each store contributes today

| Fact | Steam | Epic | GOG | Library plugins |
|---|---|---|---|---|
| Currency-bearing purchases, refunds and wallet movements | Captured account pages | No importer | No importer | No transaction contract |
| Cumulative playtime | Local files and optional owned-games API | Optional web playtime; local reader has none | Galaxy database; registry fallback has none | Optional field |
| Last played | Local files and API | Current candidates have no date | Galaxy database; registry fallback has none | Optional field |
| Acquisition date | Account-page imports | Optional web library date | Galaxy purchase date | Optional field |
| Winnow-recorded sessions | Shared monitor | Shared monitor | Shared monitor | Possible when executable/ownership attribution works |
| Achievement progress | Implemented account-aware Steam sync | No fetcher | No fetcher | No achievement contract |
| Genres, release year, publisher and related metadata | Store metadata plus shared enrichment | Catalog plus shared enrichment | Product metadata plus shared enrichment | Optional metadata |

Evidence:

- [Account facts](../../src/Winnow.Core/Domain/AccountFacts.cs),
  [Steam account importer](../../src/Winnow.App/Services/SteamAccountPageImportService.cs)
  and [stats repository](../../src/Winnow.Data/Repositories/AccountStatsRepository.cs):
  the repository accepts a source, but Steam is the only implemented monetary writer.
- [Steam local source](../../src/Winnow.Ingest.Steam/SteamLibrarySource.cs) and
  [owned-games API client](../../src/Winnow.Enrich.SteamWeb/SteamWebApiClient.cs).
- [Epic local source](../../src/Winnow.Ingest.Epic/EpicLibrarySource.cs) and
  [web candidate projection](../../src/Winnow.Ingest.Epic/Web/Model/EpicOwnedLibrary.cs).
  The latter explicitly documents its raw playtime unit as unverified/configurable. Validate
  that unit with a sanitized real fixture before claiming precise cross-store comparisons.
  This limitation does not affect Winnow-recorded session durations.
- [Galaxy reader](../../src/Winnow.Ingest.Gog/GalaxyLibraryReader.cs) and
  [GOG source/fallback](../../src/Winnow.Ingest.Gog/GogLibrarySource.cs): purchase dates are
  distinct from installation dates; the registry fallback cannot supply historical play.
- [Plugin contracts](../../src/Winnow.PluginSdk/PluginContracts.cs) and
  [plugin projection](../../src/Winnow.App/Services/PluginSyncService.cs): optional library
  facts do not imply transaction, achievement or historical-session ingestion.
- [Executable indexing](../../src/Winnow.Monitor/GameExecutableIndexBuilder.cs) operates on
  ownership installation paths across stores; attribution still determines whether a
  particular game's process is recognized.

## Useful views supported by existing facts

“Small” means projection work over an existing bounded snapshot. “Medium” includes a new
aggregate query and shared view model plus desktop/fullscreen presentation and tests.

| Question and visual | Population and computation | Limits to state | Effort |
|---|---|---|---|
| How much have I played recently? Weekly bars, recorded-hours total | Valid completed sessions overlapping a selected period; sum clipped duration | Recorded process time, not active attention or complete lifetime activity | Medium |
| Which games held my attention? Ranked top-games bars | Sum period sessions by resolved game; optional separate current reported-playtime ranking | Do not combine cumulative counters with session totals | Medium |
| What sort of sessions do I have? Duration histogram and median | Valid completed sessions; explicit bins such as under 30 min, 30–60 min, 1–2 h, over 2 h | Open sessions excluded; median describes recorded sessions only | Medium |
| When do I tend to play? Day/hour heatmap | Split session intervals into local-time bins | Time-zone/DST rules required; blank means no recorded play, not proof of absence | Medium; later slice |
| How much of my library have I tried? Current bucket/count bars | One scoped resolved game per group; use shared derived buckets | “Never played” means no recorded play evidence, not verified lifelong non-use; Retired is not Completed | Small |
| Which stores do I use? Recorded-hours bars by store | Sessions join their actual ownership's store before game grouping | Linked games must not be assigned to a preferred launch store | Medium |
| Where do I own games? Store-entry bars | Scoped ownerships grouped by store, with separate unique-game count | A game owned in two stores belongs to both; store counts are not a partition of unique games | Small |
| What is installed or dormant? Counts and recency bands | Scoped installation and last-played facts | Unknown dates are a separate population; Epic local absence is not inactivity | Small |
| What kinds of games fill my library? Genre/theme and release-year bars | Scoped games joined to facets and work metadata | Genres overlap; show coverage/Unknown, and never infer actual co-op play from supported modes | Small–medium |
| How has my collection grown? Acquisition-by-year bars | Ownership acquisition dates, deduplicated for the stated counting unit | Partial acquisition coverage; first import/install date is not purchase date | Medium |
| What did I enjoy? Journal rating distribution and recent highly rated sessions | Explicit session ratings/notes | Ratings describe sessions, not a global game review; unreviewed is not disliked | Small–medium |
| What achievements am I working toward? Steam release progress cards | Existing selected-account achievement summaries | Keep platform/release and freshness/availability; completion of achievements is not finishing the game | Medium; conditional Steam view |

For a first page, prioritize four sections rather than placing every possible chart on one
screen: **recorded hours**, **top games**, **session lengths**, and **your library today**.
Offer 30 days, 90 days and custom period controls for session charts. Label current library
composition explicitly so changing the period does not imply a historical library snapshot.

### Reuse and aggregation boundaries

- [LibrarySnapshot](../../src/Winnow.Core/Queries/LibrarySnapshot.cs),
  [library query](../../src/Winnow.Data/Repositories/LibraryQueryRepository.cs), and
  [GameGrouping](../../src/Winnow.Core/Queries/Buckets.cs) provide the shared library facts.
  Derive allowed ownership IDs from `snapshot.Buckets` or `Library.AllTiles`, then join
  supporting records: `snapshot.Ownerships` and `snapshot.Works` are unfiltered table reads.
  Count each resolved game once. Member rows reference the same `GameGrouping`, so summing
  `row.Game.PlaytimeMinutes` across members multiplies its total; raw row minutes remain
  ownership-specific.
- [CoveragePlaytime](../../src/Winnow.Core/Identity/IdentityCoverage.cs) currently **sums
  store entries** and derives the latest date from those same entries. Reuse those semantics
  for a library-level reported-playtime view; do not silently replace them with a maximum.
  Expansion links do not roll their playtime into a parent. A per-store view must first
  restrict entries to that store, then fold them; the all-store group total is unsuitable.
- [Activity repository](../../src/Winnow.Data/Repositories/ActivityRepository.cs) already
  joins sessions to stores and accepts allowed ownership IDs. Its interface returns pages,
  and the period predicate selects session **starts**. Neither summing the first 50 rows nor
  reusing that start-only filter yields correct interval totals.
- Add a bounded aggregate read taking scoped ownership IDs and half-open UTC bounds. Select
  overlapping sessions, validate endpoints/durations, and proportionally allocate recorded
  duration when an interval crosses boundaries. Keep a separate session-count rule, such as
  sessions started in the period. Overlapping games produce recorded game-hours, not a
  deduplicated measure of time at the computer.
- [ActivityTimelineSeries](../../src/Winnow.App/ViewModels/ActivityTimelineSeries.cs) already
  validates sessions and accepts only qualifying consecutive month-end snapshot deltas,
  rejecting counter resets. It prefers qualifying monthly history over sessions for a month
  to avoid double counting. A future imported-history view should extract/reuse those rules,
  not sum arbitrary snapshot differences or label them individual sessions.
- [LibraryHistoryStatsRepository](../../src/Winnow.Data/Repositories/LibraryHistoryStatsRepository.cs)
  serves recommender maturity across the database, includes unfiltered/open-session evidence,
  and is not an appropriate shortcut for user-facing visible-library statistics.
- [Work fields](../../src/Winnow.Core/Domain/Work.cs),
  [facet provenance](../facet-provenance.md),
  [session notes](../../src/Winnow.Core/Domain/SessionNote.cs), and
  [achievement summaries](../../src/Winnow.Core/Queries/ReleaseAchievements.cs) provide the
  optional descriptive/rating/progress facts. Multi-label genre counts should use bars,
  not pie slices pretending to be mutually exclusive.

### Account and visibility rules

Use the same scoped ownership population as the library: hidden games, maturity settings,
account visibility and non-game filtering must remain consistent. Fullscreen Activity
already derives that population from `Library.AllTiles`. Temporary search text should not
silently filter Stats; use an explicit Stats scope instead.

Selected-account Steam cumulative minutes/date can be substituted together by the library
query. However, [sessions](../../src/Winnow.Core/Domain/Session.cs) and ownership snapshot
history do not carry player account identity. Filtering eligible games to an account does
not prove that account played every historical session on this device. Use “Recorded on
this library” wording and a coverage note; person-specific history needs collection changes.
Retain the current spending safeguard for potentially overlapping known/unknown captures.

## Recommended presentation and first implementation

**Desktop:** STATS opens a page with Gameplay and Spending tabs. Gameplay has an explicit
All stores/store picker and period control. Spending retains store-specific source labeling,
currency totals and currency selection; show only stores with a supported monetary source.
Do not render Epic/GOG spending as zero when there is no importer. Keep the current spending
view as the existing entry until Gameplay is actually built.

**Fullscreen:** retain Activity → Library summary as the entry, with the same two local
sections and explicit store/period controls. Use existing local-section controller patterns,
large labeled charts and shared projections; keep independent selection/scroll state from
desktop. The existing [stats dashboard](../../src/Winnow.App/Views/AccountStatsDashboard.cs)
offers themed labeled bars, responsive layout and text equivalents worth reusing.

A bounded first implementation would:

1. Add a Core gameplay summary contract and a Data aggregate query over scoped sessions.
2. Build a shared Gameplay view model with period/store selection, coverage counts and the
   four initial sections above. No new ingestion or schema is necessary for those sections.
3. Add the two local views to both presentation paths. Keep monetary source selection
   separate from ownership store filtering: account transaction facts are not library rows.
4. Verify linked copies, store filters, hidden/account scope, empty history, open/invalid
   sessions, duplicate session evidence, midnight/DST crossings and concurrent sessions.
   Reuse `ActivityRepositoryTests`, `ActivityTimelineSeriesTests`, `SessionRestartTests`
   and the existing desktop/fullscreen stats tests. Benchmark a large synthetic history
   before claiming latency; aggregate on a worker with cancellation, not through chart-side IO.

## Defer until the evidence exists

- **Epic/GOG spending:** new provenance-bearing monetary importers and fixtures are needed.
  [Ownership.PricePaidCents](../../src/Winnow.Core/Domain/Ownership.cs) has no currency and
  cannot safely stand in for transaction facts. The plugin SDK would also need a monetary
  contract before provider plugins could contribute spending.
- **Cost per hour/per game and inferred savings:** bundles, external keys, missing prices,
  currency and account scope prevent reliable general totals. Keep transaction averages.
- **Complete streaks, days not played and first-ever play:** earliest observation is not a
  lifetime boundary; Winnow does not establish continuous observation coverage.
- **Historical backlog reduction:** current buckets are derived, not historical membership
  snapshots. Imports, linking and hiding can change counts without a play event.
- **Verified active play, actual multiplayer/co-op hours and games completed:** running
  processes and supported game modes do not establish attention, mode used or completion.
- **Cross-platform achievement percentage:** summaries intentionally preserve each release
  and account; merging different achievement sets would invent a metric.

The next useful implementation is Gameplay over recorded sessions and current library facts.
Broader money ingestion is a separate project. This spike creates no follow-up tasks and
does not claim external API availability beyond the adapters already implemented here.
