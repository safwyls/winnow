# Spike: Update signals — build push + announcement, and what a 616-game poll costs

> **Evidence, not a rule.** This document records how something was measured and is
> never the place to look up what to do. The current rule is in `game-library-design.md` §4.5.

Date: 2026-08-23. Resolves the `[VERIFY]` in `game-library-design.md` §4.5. Feeds M2.

**§1, §3 and §5 were verified live** from this machine on 2026-08-23: read-only `GET`s, descriptive
`User-Agent`, ~140 requests, no auth, no API key. **§2 (local SteamCMD) is documentation-only** —
SteamCMD was never installed or run here. §4 is arithmetic over measured per-request figures.

| Source | Verdict |
|---|---|
| `api.steamcmd.net/v1/info/{appid}` | **VIABLE — alive today.** `timeupdated` present, plausible. One appid/request, no throttle seen |
| Local SteamCMD fallback | **NOT VIABLE — drop it.** 250 MB, open Valve non-TTY output bug, no upside |
| `ISteamNews/GetNewsForApp` | **VIABLE — and needs NO API key.** `tags=patchnotes` beats §4.5's "community announcements" |
| `IStoreBrowseService/GetItems` (batching hope) | **CONFIRMED NEGATIVE.** No update timestamp — only `steam_release_date` |
| Local `appmanifest_*.acf` `buildid` | **VIABLE COROBORATOR, free.** Installed games only, and it lags |
| SteamKit2 PICS `GetChangesSince` | **NOT VIABLE for v1.** Persistent Steam client connection; ~5000-changenumber history cap |

---

## 1. `api.steamcmd.net` — ALIVE. §4.5's outage was transient

```
GET https://api.steamcmd.net/v1/info/570      (User-Agent: Winnow/0.1 …)
```
```json
{"data": {"570": {"_change_number": 38253266, "_missing_token": false, "appid": "570",
  "common": {…}, "config": {…}, "extended": {…}, "ufs": {…},
  "depots": {"branches": {"public": {
      "buildid": "24885732", "timebuildupdated": "1787446377", "timeupdated": "1787446656"}}}}},
 "status": "success"}
```

`depots.branches.public.timeupdated` is present on all four appids tested with plausible values.
§4.5's field path is correct. Verified behaviours:

- **Strictly one appid per request, authoritatively.** `GET /openapi.json` returns the *entire* API
  surface: `/v1/info/{app_id}`, `/v1/version`, `/health`, `/ready`. That is all four routes.
  `/v1/info/570,620` → `422 int_parsing`. Do not design for batching.
- **No rate limiting observed.** 20 back-to-back (~1.3 req/s, latency 0.60–0.97 s) all 200. No
  `Retry-After`, no `429`. **Lower bound only** — a free volunteer service, not a licence to hammer.
- **No caching or compression.** No `ETag`/`Last-Modified`/`Cache-Control`; `--compressed` returns
  identical bytes. Every poll pays full size: 8.9–15.1 KB, **avg ~12.1 KB/app**.
- **Missing app returns 200, not 404:** `{"data": {"999999999": {}}, "status": "success"}`. Branch on
  the empty inner object, never on HTTP status.
- **`timeupdated` ≠ `timebuildupdated`** (279 s apart for 570; 30 days for Elden Ring). §4.5 names
  `timeupdated` — the branch-pointer flip, i.e. when it reached users. Right choice; store both.
- **Ignore non-public branches.** 620 has `beta`/`demo_viewer`/`previous_release`; 413150 has
  `compatibility`/`legacy_1.5.6`/`legacy_1.6.8`/`previous_version`. Read `public` only.

Terms (from steamcmd.net): "No authentication or verification is required"; "made available for free
and without specific restrictions"; "a non-official API … not affiliated with Steam or Valve"; data
arrives "within seconds" (it is a PICS mirror). No `robots.txt` (404). **No SLA** — §4.5's outage
proves it can go dark, so a failed fetch must degrade to "no build signal", never to an error.

## 2. Local SteamCMD fallback — NOT WORTH IT (documentation-only)

- **Footprint.** `steamcmd.zip` is **774,825 bytes** (verified by `HEAD`), but it is a self-updater;
  Valve's own error text is "Steamcmd needs at least 250MB of free disk space to update." A
  quarter-gigabyte second Steam client to read one integer per game is indefensible in a desktop app.
- **Non-interactive output is an open bug.** `+login anonymous +app_info_update 1 +app_info_print <id>
  +quit` is the documented incantation, but the output buffer breaks outside a TTY —
  ValveSoftware/steam-for-linux#9683, Source-1-Games#1929 — plus a reported "nothing on first request,
  works on retry" second pass. Parsing VDF out of a pty on Windows is a second product, not a fallback.
- **Anonymous login works**, but each invocation costs a full Steam connect + appinfo cache refresh —
  seconds per process, worse than the ~0.77 s HTTP call it replaces.

**Recommendation: remove the SteamCMD fallback from M2.** §5's local `appmanifest` read is a better
free corroborator, and a badge appearing a day late is not a failure.

## 3. `ISteamNews/GetNewsForApp` — VIABLE, KEYLESS, better than §4.5 knew

**No API key required** — verified by direct call with no `key=`, HTTP 200. Combined with §1, this
settles it: **M2 needs no user-supplied credentials at all**, and no settings screen for keys.

§4.5 says "filtered to community announcements". `feeds=steam_community_announcements` works (413150:
527 → 74) but still returns merch promos and anniversary posts. **`tags=patchnotes` is much sharper**
(527 → 34, all real patches):

```
GET https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid=413150&count=1&maxlength=1&tags=patchnotes
```
```json
{"appnews": {"appid": 413150, "count": 34, "newsitems": [
  {"gid": "1786573930663336", "title": "Stardew Valley 1.6.15 Patch now available",
   "url": "https://steamstore-a.akamaihd.net/news/externalpost/steam_community_announcements/1786573930663336",
   "is_external_url": true, "author": "ConcernedApe", "contents": "H...",
   "feedlabel": "Community Announcements", "date": 1734718461,
   "feedname": "steam_community_announcements", "feed_type": 1, "tags": ["patchnotes"]}]}}
```

**That `url` is what design-system §5.2's "clicking the badge opens the patch notes" needs** — store it
on the `update_events` row at detection time; it is not cheaply recoverable later.

- **Parameters, authoritatively** from keyless `ISteamWebAPIUtil/GetSupportedAPIList`: `appid` (uint32,
  **required, singular — no batching**), `maxlength`, `enddate`, `count`, `feeds`, `tags`. Valve's own
  description misspells the example tag `'patchnodes'`; the working value is **`patchnotes`** (verified
  live). `v1` lacks `feeds` — use `v2`.
- **No "since" parameter.** `enddate` pages *backwards*. Fetch the newest item and compare `date` to a
  stored high-water mark. `count=1&maxlength=1&tags=patchnotes` makes that **417–454 B (avg ~440 B)**.
- **Top-level `count` is the total matching the filter**, not the number returned — a cheap secondary
  change detector; `date` remains authoritative.
- **CRITICAL — 403 here is per-appid, not rate limiting.** Apps with no news feed (delisted or
  tool appids: 460, 480, 520, 750) return **403 with body `{}`**; an app with a feed but no matching
  items returns **200 with `"newsitems": []`** (e.g. 790). A naive client reads 403 as throttling and
  backs off for hours over a delisted game. **Treat 403 as a permanent per-appid "no feed" and cache
  it; only 429 means slow down.**
- **No rate limiting observed.** 25 rapid requests to known-good appids: 25× 200, ~0.10–0.21 s each.
  §4.2's 429 + `Retry-After` handling still ships from commit one — 25 requests is not a ceiling.

### The correlation works — evidence for §4.5's thesis

| appid | `timeupdated` (build) | latest `patchnotes` | Δ | Verdict |
|---|---|---|---|---|
| 620 Portal 2 | 2026-06-29 | 2026-06-29 | **same day** | **MAJOR** — both fired |
| 413150 Stardew Valley | 2024-12-22 | 2024-12-20 | **2 days** | **MAJOR** — build landed *after* the post |
| 570 Dota 2 | 2026-08-23 | 2026-07-01 | 53 days | build only → correctly suppressed |
| 1245620 Elden Ring | 2026-05-28 | 2025-12-16 | 163 days | build only → correctly suppressed |

§4.5's noise claim is exactly right: Dota 2 and Elden Ring both carry fresh depot pushes with no
patch note. A lone-push heuristic would badge them and be wrong twice out of four.

**Set the correlation window to ±7 days, not hours.** Stardew's build arrived **two days after** its
announcement, so the window must be symmetric and measured in days. ±3 would still catch all four
cases; ±7 gives margin at negligible false-positive cost, since both signals must fire anyway.

## 4. Cost — the number that decides M2's design

Measured: steamcmd.net **~12.1 KB, ~0.77 s**; ISteamNews **~440 B, ~0.13 s**.

**Naive full poll, 616 games, both signals:**

| | Requests | Bytes | Wall clock @ 1 req/s |
|---|---|---|---|
| steamcmd.net | 616 | 7.45 MB | 10.3 min |
| ISteamNews | 616 | 271 KB | 10.3 min |
| **Total** | **1,232** | **7.72 MB** | **~20.5 min serial, ~10.3 min parallel across the two hosts** |

Trivial against Steam's 100k/day nominal budget — but **616 daily requests to a free volunteer service
is the real objection**, not Valve's limits.

### Recommended strategy: eliminate, cascade, stagger

**(a) Eliminate the ineligible.** design-system §5.2: "Never on never-opened games." §6.1's "stale but
patched" rule requires *playtime above threshold*. So `playtime_minutes = 0` games are **structurally
incapable of showing this badge — do not poll them at all.** Drop `Retired` and `Dead` too. Run
`SELECT COUNT(*) … WHERE playtime_minutes > 0` for the real figure; a ~40% never-touched share is
typical for a large Steam library, giving **E ≈ 370** of 616. *This is the model's one assumption —
replace it with the query result before committing.*

**(b) Cascade: announcement gates, build confirms.** Announcements are rare; depot pushes are constant
(Dota 2 above). So sweep the *cheap Valve* signal wide and touch the *volunteer* service only on a hit.
Because `timeupdated` is a persistent point-in-time value, one call on announcement day also sees a
build that landed days *earlier* — the Portal 2 case resolves in a single call. Only when the build has
not landed yet (the Stardew case) does the app stay on a watch list for daily checks until ±7 d closes.

**(c) Stagger the sweep over 7 days.** Up to 7 days' detection latency is irrelevant for a badge about
a game last played six months ago.

**Steady state, E = 370:**

| Job | Requests/day | Bytes/day | Wall clock |
|---|---|---|---|
| ISteamNews sweep, 1/7th of eligible | 53 | ~23 KB | ~53 s @ 1 req/s |
| steamcmd.net watch-list confirmations | ~10 | ~121 KB | ~8 s |
| **Total** | **~63** | **~145 KB** | **~1 min/day, background** |

**~63 requests/day versus 1,232 — a 95% reduction**, and steamcmd.net drops from 616 hits/day to ~10.
The ~10 assumes ~4 patch announcements/day across 370 mixed titles, about half confirming on the first
call and the rest needing 2–3 rechecks; instrument the real rate and tune.

**One-time baseline:** 370 news + 370 build = 740 requests, ~12 min at 1 req/s, spread across the first
few background sessions — never a user-facing path (§5.1). Until a game has a baseline it produces no
event, which is correct: "patched since you last played" is meaningless without a prior observation.

**Optional tiering:** games dormant 3+ months are the badge's real target and deserve the 7-day sweep;
games played within 30 days cannot be "stale" soon and can drop to a 30-day sweep.

## 5. Signals §4.5 missed

**Local `appmanifest_*.acf` — VERIFIED, free, use as a corroborator.** `Winnow.Ingest.Steam` already
reads these; the fields are in `tests/fixtures/steam/` today:

```
"lastupdated"   "1787359073"
"buildid"       "23623172"
"TargetBuildID" "23623172"
```

Casing trap: **lowercase `lastupdated`, lowercase `buildid`, but `TargetBuildID`.** Zero network cost,
and a `buildid` change is a genuine build push. Two caveats keep it secondary: it exists only for
*installed* games, and it records when *this machine downloaded* the update, not when Valve pushed it —
a dormant game with auto-update off lags indefinitely. Use it to skip a steamcmd.net call when local
`buildid` already advanced past the stored value; never to prove a game was *not* updated.

**`IStoreBrowseService/GetItems` — CONFIRMED NEGATIVE.** The hope was that the endpoint
`steam-store-tags.md` recommends (batches 100+ appids) also carries an update timestamp, which would
collapse the cost problem. It does not: a live fetch for 413150 with every `data_request` flag set
returned 16 top-level keys, and the only time-like field anywhere is `release.steam_release_date`
(1456509578 — the 2016 launch date). No build, version, or update field.

**SteamKit2 PICS `GetChangesSince` — NOT VIABLE for v1; reported, not verified.** In principle ideal:
one call returns every app changed since a changenumber. In practice it needs a persistent
authenticated Steam client connection (a second protocol stack inside an Avalonia app), and community
reports say requesting more than ~5,000 changenumbers of history returns only the current changenumber
— Steam's global changenumber is already 38,253,266 and moves fast, so a daily poller would blow past
that window between runs. Revisit only if steamcmd.net dies.

## §4.5 amendments

| §4.5 says | Reality |
|---|---|
| steamcmd.net demo was erroring — `[VERIFY]` | **Alive and correct.** `depots.branches.public.timeupdated` present on all 4 appids |
| Keep local SteamCMD as fallback | **Drop it.** 250 MB + an open non-TTY output bug; degrade to "no build signal" |
| "filtered to community announcements" | Use **`tags=patchnotes`** — 527 → 34 items vs 74 for the feeds filter |
| — | **`GetNewsForApp` needs no API key.** M2 requires no user-supplied credentials |
| — | Both sources are **one appid per request**; steamcmd.net's `/openapi.json` proves no batch route exists |
| — | **403 from `GetNewsForApp` means "no feed for this appid", not throttling.** Cache it; do not back off |
| — | Correlation window must be **±7 days** — Stardew's build landed 2 days *after* its announcement |
| — | Store the news item `url` on the event row; design-system §5.2's badge click needs it |
| — | Never-opened games are ineligible for the badge, so **do not poll them** — the single biggest saving |
