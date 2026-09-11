# Spike: Activity graph data availability

> **Dated evidence.** Findings describe the builds and services observed on the dates below.
> Current implementation choices are in [the build spec](../../game-library-design.md);
> current interactions and layout are in [the visual spec](../../design-system.md).
> This record is optional background.

**Settles:** whether a per-game activity graph can be drawn on the details modal
(TASK-115). Investigated 2026-09-05.

## Method

No code was run. Each claim below was verified by reading the source files that
implement or consume the endpoint in question, and by cross-referencing the
update-signals spike (`docs/spikes/update-signals.md`) for the steamcmd.net
surface.

**Steam Web API.** `SteamWebApiClient` calls `IPlayerService/GetOwnedGames/v1`
(verified in `src/Winnow.Enrich.SteamWeb/SteamWebApiClient.cs`, line 32).
`SteamHistoryClient` calls `IPlayerService/ClientGetLastPlayedTimes/v1` and
`ISaleFeatureService/GetUserYearInReview/v1` (verified in
`src/Winnow.Enrich.SteamWeb/SteamHistoryClient.cs`, lines 32-38). These are
the only Steam Web API endpoints Winnow calls. None of them returns a
historical player-population series.

**Backfill reconstruction.** `SteamPlaytimeBackfillService`
(`src/Winnow.App/Services/SteamPlaytimeBackfillService.cs`) imports per-month
playtime from Steam Replay for years 2022 onward. It anchors on
`playtime_forever` from `ClientGetLastPlayedTimes` and delegates to
`PlaytimeSeriesReconstructor.Reconstruct`
(`src/Winnow.Enrich.SteamWeb/Model/PlaytimeSeriesReconstruction.cs`) which
walks backwards from the anchor, subtracting each month's delta to produce a
cumulative month-end series. The floor point -- everything before the first
covered month -- is a single value stamped at the end of the preceding month.

**steamcmd.net.** The update-signals spike records four routes (`v1/info/{appid}`
and its OpenAPI spec). The SteamCmdBuildInfoClient
(`src/Winnow.Enrich.Updates/SteamCmdBuildInfoClient.cs`) calls only
`v1/info/{appid}` for build timestamps. No route carries player counts.

**IGDB.** The only mention of popularity in the codebase is a doc comment on
`GamesDbJson` (`src/Winnow.Enrich.GamesDb/Model/GamesDbJson.cs`, line 24)
noting that "popularity ranks" arrive in the body and are not read. Winnow does
not query IGDB's `popularity_primitives` endpoint.

**Session monitoring.** `SessionWatcher`
(`src/Winnow.Monitor/SessionWatcher.cs`) is a polled process watcher that
records sessions only while Winnow itself is running. Sessions begin from the
moment Winnow first observes a game process and are written to the database
when the process exits.

## Findings

### 1. Per-game player-population history is not obtainable

The original request imagined a graph of player activity over the life of a
game. No source Winnow has access to provides that data.

- `GetNumberOfCurrentPlayers` (Steam Web API) returns a single instantaneous
  integer. It has no history parameter and no time-series variant.
- `GetGlobalStatsForGame` (Steam Web API) returns developer-defined
  achievement statistics. It requires a publisher key and does not carry player
  counts.
- `ISteamChartsService` (Steam Web API) is a current top-N leaderboard with no
  per-appid lookup and no historical depth.
- steamcmd.net exposes build metadata only; no route carries player counts.
- IGDB's `popularity_primitives` is a single normalised score updated in place,
  not a time series.
- SteamDB holds historical player-count data but forbids automated access in
  its own FAQ.

The sources examined did not supply an automated historical player-population curve.

### 2. A per-user playtime series does exist for keyed Steam installs

`SteamPlaytimeBackfillService` reconstructs a month-end cumulative playtime
series from Steam Replay, anchored on `playtime_forever`. Coverage begins at
2022, the first year Valve ran Steam Replay. The series is written to
`playtime_snapshots` as month-end cumulative points.

Backfilled installs therefore have multiple monthly points per game; a newly observed
local library can have only one snapshot. Chart coverage must distinguish those cases.

### 3. Two ways to draw this data that would mislead

The backfill data is real but carries structural gaps that a naive
visualisation would hide.

**Drawing a slope across the pre-coverage span invents a shape nobody
measured.** The reconstructor preserves the cumulative AMOUNT of pre-coverage
play but not its distribution over time. The floor point is a single value
stamped at the last second before the first covered month
(`PlaytimeSeriesReconstruction.cs`, line 163). Drawing bars or a slope across
that span would imply a month-by-month pattern that was never observed. The
pre-coverage portion must be rendered as a flat band -- a known total with an
unknown shape.

**Deriving per-month deltas from live snapshots would misattribute play to
wrong months.** Winnow writes a snapshot only while it is running. A user who
closes Winnow for three weeks and reopens it gets three weeks of accumulated
play stamped onto a single observation. A chart built from those deltas would
draw a spike on the day the app happened to reopen, not on the days the play
happened. Only the backfilled month-end deltas, which come from Steam's own
per-month accounting, are genuinely per-month.

**Sessions must not be mixed into the same chart.** Session data exists only
from the moment Winnow is installed and only for processes Winnow observed
while running. Overlaying sessions onto the backfilled series would make a
game that was heavily played for four years before Winnow's install look
dormant for that entire period, because no sessions exist for it. The two data
sets answer different questions and have different coverage windows.

## Reproducibility

No external tool was used. The findings are derived from reading the source
files named in the Method section. Re-reading those files reproduces the
analysis.
