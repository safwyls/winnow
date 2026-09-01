# Winnow — Roadmap (v2)

Supersedes §8 of `game-library-design.md` for sequencing. The hard constraints in §4 and the
module boundaries in §5.1 are unchanged and still binding. Where this document contradicts
§1's non-goals, the amendment is stated explicitly in §3 below — nothing is quietly dropped.

---

## 1. What Winnow is

**Winnow is the library that remembers.**

*Renamed from Hoard on 2026-08-28, mascot included — the dragon is called Winnow now. The
name change is not cosmetic in one respect worth recording here: the app's premise is
**winnowing a hoard**, so the old name named the problem and the new one names the work. The
hoard is still the domain concept and the word survives in the design system on purpose; see
`CLAUDE.md`. The three compatibility shims the rename needed — the data-directory move, the
DbUp journal re-point, and the theme-id alias — are listed there too, and each is
load-bearing for an install that predates the rename rather than tidy-up that can be
deleted.*

Every storefront lists your games. None of them retain the history that makes a library
legible: how long a game sat unopened before you tried it, whether you bounced off it once
or fought with it across six sessions, whether it has been patched three times since you
gave up, whether you are the kind of person who ever comes back.

Storefronts discard that. Winnow keeps it. That is the whole asset.

"Analytics tool" undersells it, correctly — analytics is a panel you open monthly and then
stop opening. But "launcher" oversells it in a different direction: Playnite already is a
mature open-source launcher with a plugin ecosystem, and a straight race against it is one
Winnow loses on maturity alone.

The position that is actually defensible is the intersection.

## 2. Why launcher + recommender, and not either alone

The two halves are not separate ambitions. They are a loop:

```
   launch games through Winnow
            |
   real session data accrues   <- nobody else has this
            |
   the feed gets genuinely good
            |
   the feed is the reason to launch through Winnow
```

This matters for sequencing. A recommender fed only by periodic library syncs sees **one
playtime number per game per sync** — the same impoverished view Steam has. A recommender
fed by session detection sees when you play, for how long, what you abandon mid-session, and
what you return to. The second is a different product.

So the launcher is not a feature bolted on for adoption. **It is the data-acquisition
strategy for the differentiator.** That is the argument for building it, and it is a stronger
one than "launchers get installed."

Two consequences fall out immediately, and both are lucky:

- **M3 was already the launcher.** The unimplemented process-monitor and session-detection
  milestone is the hard 80% of launching games. Actually *starting* a game is a URI handoff
  (`steam://rungameid/440`) and is nearly free; knowing when it stopped is the real work, and
  it was specified a year ago in §5.2. The launcher pivot costs far less than it appears to,
  provided M3 lands first.
- **M5 was already the cold-start fix.** Historical playtime backfill (mechanism revised
  2026-08-28; see §4) gives the recommender a real longitudinal series on install day. A
  recommender on a library synced this morning has one snapshot per game and no sessions,
  so nearly every interesting signal is degenerate. M5 is the single biggest lever against
  that.

## 3. Amendments to the design doc

| §1 non-goal | Status | Rationale |
|---|---|---|
| Recommendation engine (phase 2) | **Promoted to core** | It is the differentiator. Phase-2 placement assumed it needed a server; it does not — all inference is local over the user's own database. |
| Any hosted service, user accounts, multi-user | **Unchanged** | Still no server, still no Winnow account, still no telemetry. **Winnow has no accounts; Winnow links yours.** Epic/Steam OAuth authenticates you to *their* service and stores the token locally under DPAPI. That is third-party linking, not account creation, and the distinction is load-bearing. |
| 3D "games on a shelf" view (cut, §11) | **Still cut** | Full-screen gamepad mode (M10) is a 10-foot UI, not the 3D shelf. Avalonia handles it natively; §11's framework reasoning does not apply and is not reopened. |
| Shipping storefront client credentials | **Decided 2026-08-26: ship them built-in** | A sign-in button cannot ask the user for credentials, and there is no version where they supply their own: Epic issues no client that can read a personal library (an EOS portal app is rejected with `invalid_client`), and GOG has no public dev portal for this. So the only alternatives were "embed the launcher credentials" or "the feature does not exist". Heroic, Legendary and the Playnite plugins all embed them. Winnow is the party distributing them and that is a real cost; the realistic failure mode is Epic or GOG rotating a client and sign-in breaking until updated, not bans. The published Epic pair was verified live on 2026-08-26 rather than trusted. |
| PSN / Xbox (§4.6) | **Unchanged — still excluded** | See the note under M4.5. Epic OAuth is not a precedent for these. |
| §4.7 no-scraping rule | **Amended 2026-08-28** | M5's saved-HTML importer gains an embedded-WebView peer route: user-present, ephemeral session, two pages only. The manual save-the-pages route remains a first-class equal, not a fallback. Binding conditions below. |
| §4.7 no-scraping rule | **Amended again 2026-08-30** | Condition 1 of the 2026-08-28 amendment (ephemeral session, cookies never persisted) is superseded: the refresh token is now persisted under DPAPI for unattended renewal. Conditions 2 and 3 narrowed and extended; condition 4 unchanged. Eight binding conditions below. |

## 4. Phases

Numbering continues from §8. M0–M2 and M4 are shipped.

| # | Deliverable | Exit criteria | State |
|---|---|---|---|
| M4.5 | Epic OAuth ownership source + local fallback | Entitlements resolve when authed; unauthed degrades silently to local files with no loss of install state | **shipped** — sign-in since verified end to end |
| M7 | Recommendation core (`Winnow.Recommend`) | Standalone scoring module, explainable output, sensible ranking on a cold library; not yet wired to UI | **shipped** (unwired by design) |
| M3a | Session detection (§5.2 mechanism A) | Process watching records sessions with true start/end; poll for discovery only, events for exit | **shipped** |
| M4.6 | Store sign-in UI (Epic) | A sign-in button in the app runs an embedded-browser OAuth flow that captures the code automatically; console flow survives as a documented fallback | **shipped** |
| M11 | Appearance system | Four themes, a transparency slider with a chosen backdrop, an optional island layout, a drop-in JSON theme format, and an application icon | **shipped** — unplanned, see below |
| — | GOG sign-in | **Held, on evidence.** Nothing to gain today; see below | not scheduled |
| M3b | Launch + journal prompt | Launching from Winnow records a session; journal prompt opt-in (§9 pitfall 7) | **shipped** — `winnow-wrap` (§5.2 B) deliberately deferred |
| M8 | The Feed | Recommender surfaced as the app's primary view; every card states its reason in one sentence | **shipped** — no dismiss/snooze yet; nothing remembers yesterday |
| M5 | Historical playtime backfill (redefined 2026-08-28) | Historical playtime backfills; feed measurably improves on a cold library | **built** — reviewed 2026-08-29, 2,111 tests passing; exit criterion half-proven (backfill tested, feed improvement awaiting live validation with user's key) |
| M6 | Export (JSON + CSV) | JSON is complete and re-readable; CSV covers a defined set of views | **deferred** 2026-08-31; exit criterion to be restated |
| M9 | Install / uninstall management | Install and uninstall delegate to the owning store client and reflect state back | after M6 |
| M10 | Full-screen mode + gamepad navigation | Whole app navigable on a controller at 10 feet | last |

### M11 was not planned, and is recorded rather than back-justified

The appearance work — four themes, the transparency slider, the Acrylic/Mica choice, the
island layout, the JSON theme engine and the app icon — appears in no earlier version of this
document. It was directed turn by turn by the user while reviewing the running app, and it
grew from "add a Mica effect" into a system.

Recorded as a milestone because it is a system now and someone will otherwise wonder where it
came from, **not** because it was planned. Two things it produced that outlast it:

- **A measurement discipline for colour.** `Colorimetry` walks AA ceilings per theme, per
  layout, per slider position, and the Appearance screen prints the number live. That was
  built to settle arguments about transparency and it now validates user-authored themes.
- **A named cost every time contrast was traded.** §14–§16 record what each step gave up,
  including four costs the island layout could not pay for and the reasons two decisions were
  reversed. The doc argues with itself where the code changed its mind.

The cost is equally plain: **it is polish shipped ahead of M3b and M8**, which are the two
milestones §2's flywheel actually depends on. Nothing in M11 collects a single session or
surfaces a single recommendation.

### Why M4.6 jumped the queue

The console sign-in shipped in M4.5 is fragile in a way that is structural, not a bug: the
authorization code is single-use and expires in minutes, so every misstep between issuing it
and spending it — an environment variable that did not propagate, a prompt that hung, a
terminal that swallowed input — burns the code and needs a fresh one. Each of those cost a
real debugging round. An embedded browser reads the code the instant the provider issues it,
which removes the window entirely rather than making it easier to hit.

**GOG is held, and the reason corrects an error made when this section was first written.**
"GOG ingest found only 14 games" was recorded here as a shortfall. It is not one. Galaxy's
database holds **45 owned GOG releases, of which 31 are DLC** — verified directly against
`LibraryReleases` joined to `ReleaseProperties`, the same join `GalaxyLibraryReader` uses.
`GogLibrarySource` drops DLC by design, so 45 − 31 = the 14 base games it reports. The local
reader is reading the entire library correctly, and the missing-games premise was invented
rather than measured.

The authenticated GOG endpoint then carries **no playtime, no last-played, no title and no
DLC flag** — all four of which the local reader already has. So a GOG sign-in button would
add a login, a stored credential and an embedded browser in exchange for nothing.

One thing could reopen it: `GET gameplay.gog.com/.../sessions` exists and accepts GET
(PUT/DELETE answer 405), but no known client reads it and its payload is unverified. If it
carries session history, that is longitudinal data worth having and this gets rescheduled.
Until someone looks, it stays held.

### Why M5 was redefined (2026-08-28)

The original M5 assumed a downloadable GDPR export archive containing a per-session playtime
breakdown. That premise came from a single unreliable source (takeoutday.org) and was never
verified against Valve's own documentation. `docs/spikes/steam-gdpr-export.md` measured it:
there is no downloadable archive. Valve's Privacy Dashboard is a set of login-gated live
pages, and its playtime page carries cumulative totals only, the same shape Winnow already
ingests from `IPlayerService/GetOwnedGames` and `localconfig.vdf`. The export-file mechanism
cannot backfill historical playtime because the data it was supposed to contain does not exist
in that form.

The spike also found where the historical data actually is. The replacement scope, approved
the same day:

1. **`IPlayerService/ClientGetLastPlayedTimes`** for `first_playtime` per app. One call,
   existing key. Converts every ownership from a point into a span, which is the
   bounced-vs-retired discrimination the feed turns on.
2. **`ISaleFeatureService/GetUserYearInReview`** for years 2022 onward. Per-game per-month
   playtime seconds and session counts, backfilling `playtime_snapshots` with a real
   longitudinal series on install day. This is the actual cold-start fix. Auth verified
   2026-08-28: both endpoints accept the stored Web API key (recorded in the spike).
3. **A saved-HTML importer** for the account licenses and purchase-history pages only
   (`acquired_at`, `license_type`, `price_paid_cents`). The user saves the pages from their
   own browser; Winnow parses local files. This preserves §4.7's no-scraping rule: the
   distinction is who fetches. Explicitly not building a general importer over the ~100
   dashboard pages.

The exit criterion is unchanged: historical playtime backfills and the feed measurably
improves on a cold library. The mechanism changed, not the goal.

**Prerequisite landed the same day.** The observation-identity foundations (review findings
F10/F19) made play records and snapshots idempotent on their full fact. A historical backfill
can now insert out-of-order points safely and re-running an import is a no-op. Without this,
M5's new mechanism would have duplicated rows on every re-import.

### Why §4.7 was amended, not violated (2026-08-28)

§4.7 of the design doc prohibits scraping authenticated Steam account pages. M5's saved-HTML
importer (item 3 above) honored that rule by having the user save the pages themselves;
Winnow only parsed local files.

The user has now approved an embedded alternative. Winnow may open an ephemeral WebView
session in which the user logs into Steam and, while the user is present, harvest the
rendered HTML of exactly two pages: `store.steampowered.com/account/licenses/` and
`store.steampowered.com/account/history/`. The harvested HTML goes to the same parser the
manual route uses.

Four conditions make this an amendment rather than a violation, and all four are binding:

1. **Ephemeral session.** The WebView uses an in-private, in-memory profile. Cookies are
   never persisted to disk. The profile is torn down after harvest. Winnow never sees the
   password; it is typed into Steam's own page, and Steam Guard works normally.
2. **Two-page allowlist.** Only the two named pages are harvested. The origin and navigation
   allowlisting discipline established by the OAuth hardening (review finding F05) applies.
3. **Manual route is an equal peer.** The save-the-pages route is presented in the UI as an
   equal option with a transparent explanation of what each route does, not as a fallback
   footnote. A user who declines to type their Steam password near a third-party app loses
   nothing but convenience.
4. **One parser.** Both routes feed the same parser. The embedded path is a fetch strategy,
   not a separate importer.

The spirit of §4.7 is that Winnow must never hold or exfiltrate the user's session or
impersonate their browser. A user-present, user-authenticated, ephemeral, two-page harvest
honors that spirit. The ecosystem precedent is the same class of risk already accepted for
the Epic embedded sign-in (M4.6): Playnite's Steam integration and the Heroic/Legendary
family both operate this way. ToS exposure is user-driven and low-volume.

### Why §4.7 was amended a second time (2026-08-30)

The first amendment (above) permitted an ephemeral WebView harvest of two Steam account
pages, with four binding conditions. Condition 1 was categorical: "Cookies are never
persisted to disk. The profile is torn down after harvest."

TASK-56 (`docs/spikes/steam-web-session-auth.md`) then established that a WebView sign-in
can mint a `webapi_token`, a JWT usable against all three Steam Web API endpoints Winnow
depends on (`ClientGetLastPlayedTimes`, `GetOwnedGames`, `GetUserYearInReview`). The token
resolves the signed-in account exactly via its `sub` claim, and it fails honestly: a bad
token returns a hard 401, where a bad API key returns a silent 200 with an empty envelope
(verified 2026-08-30, recorded in the spike's section 2 table). The token lives about a day
(24h 22m, measured 2026-08-30). Renewing it without the user present requires persisting
Steam's `steamRefresh_steam` refresh token, roughly 207 days with remember-me, and spending
it against `/jwt/finalizelogin`.

The decision is to persist that refresh token under DPAPI CurrentUser scope, the same
protection the Epic refresh token already gets, so that a signed-in user's scheduled syncs
keep working without a daily re-sign-in. That is exactly what condition 1 was written to
forbid. Hence a second amendment rather than a quiet reinterpretation.

Condition 1 of the 2026-08-28 amendment is **superseded**. Conditions 2 and 3 are
**narrowed**: made more specific and extended to cover the new surface. Condition 4 survives
intact.

Eight conditions make this an amendment rather than a violation, and all eight are binding:

1. **User-present sign-in, ephemeral off-the-record browser.** Unchanged in substance from
   the first amendment. The user types their password into Steam's own page inside an
   in-memory, off-the-record WebView profile; Winnow never sees the password; Steam Guard
   works normally; the profile is torn down afterwards.
2. **Exactly two secrets at rest.** The minted access token and the refresh token, and
   nothing else, written as one DPAPI-encrypted blob. No cookie jar. No `steamLoginSecure`.
   No `sessionid`. No browser profile persisted. No page content. A host that cannot encrypt
   refuses to store rather than degrading to plaintext; the failure mode of refusing is a
   sign-in the user repeats after a restart; the failure mode of a plaintext fallback is
   silent and permanent.
3. **A closed list of three unattended request kinds.** With nobody watching, Winnow may
   issue only: the `finalizelogin` call, the `transfer_info` POSTs that call returns, and
   one token mint. That list is closed. No authenticated HTML page is ever fetched without
   the user present.
4. **Reading is bounded by what, not by how much.** With the user present: the two named
   account pages in full, plus three named fields read from any non-login store document by
   one fixed script that cannot return arbitrary DOM. The script is fixed at build time; it
   is not a general query interface.
5. **Purchase history needs its own permission.** Capturing purchase history during a sign-in
   requires an explicit, separate prompt. Declining leaves the sign-in fully functional for
   account identity and playtime backfill.
6. **Peers, on both axes.** The Web API key and the WebView sign-in are peer connection
   methods, neither a fallback for the other. And both routes to the account pages, the user
   saving the pages themselves and the embedded harvest, remain peers, as the first amendment
   required.
7. **One parser, one importer, one credential seam.** Sign-in is a credential source, not a
   second Steam integration. Everything it produces flows into the same parser, the same
   importer, and the same credential seam the key path already uses.
8. **Legibility.** A session that cannot renew must say so before it dies. Silent degradation
   to no-remote-data is a defect, not a graceful fallback. The UI surfaces a failing renewal
   promptly, offers one-click re-sign-in, and explains that adding an API key makes scheduled
   syncs unconditionally reliable.

**What this costs, stated plainly.** A refresh token is not as reliable as an API key. It
can be invalidated by signing in elsewhere; the long lifetime only applies if the user chose
remember-me; and one contrary community report exists against the `finalizelogin` route
(node-steam-session issue #56, 2026-05-20, unresolved). That fragility is exactly why
condition 8 exists: the user must know when their session is dying, and must know that a key
would not have this problem.

§4.7 exists so Winnow never holds or exfiltrates the user's browser session or impersonates
their browser. Two named secrets, encrypted at rest, spent only against a closed list of API
calls, is not a session hijack and is not a browser impersonation. It is the same shape of
credential the Epic integration already stores, and it is narrower than the cookie jar the
first amendment's ephemeral profile held in memory.

### Why that order

**M3 before M8.** The feed's quality is bounded by its input data. Shipping the feed before
session detection means shipping it at its worst and teaching users it is mediocre. Session
data starts accruing the moment M3 lands, so M3 should land as early as possible even though
the feed is the visible prize — every week M3 is late is a week of history not collected.

**M9 delegates, never reimplements.** Winnow hands installation to the store's own client
(`steam://install/`, Galaxy, the Epic launcher). Writing our own downloaders —
Legendary-style — means owning CDN auth, chunked delivery, patching, and the support burden
for corrupted installs, for the sole benefit of avoiding a window appearing. Not worth it,
possibly not ever.

**M10 last, deliberately.** Full-screen gamepad mode is a second complete UI: focus
management, controller input, its own navigation model, its own layouts. It is the largest
surface area of the three asks and serves the narrowest slice of users (couch/HTPC). M3, M7
and M8 deliver to everyone. This is the right feature and the wrong thing to build third.

## 5. Phase 3 — recommending games you do not own

Deferred, and worth stating why beyond "needs catalog data" (it does — store catalogue
access we have not built).

There is a product-integrity tension to resolve first. Winnow's premise is *you own a thousand
games and have played forty*. An app that opens with that diagnosis and then sells you more
games is incoherent, and users will read it — correctly — as the moment the tool started
working for someone else.

The version that survives that objection is **wishlist intelligence**, not a purchase feed:
acting on titles the user has already flagged interest in, and being honest about sales and
taste-fit when asked. The core loop stays *play what you own*. Anything that inverts that
ratio is a mistake regardless of how well it converts.

## 6. Carried-over debt

Tracked so none of it silently becomes permanent:

- **Merge execution is not built.** The queue proposes and stores confirmations; nothing
  applies them. 23 cross-store pairs are pending on the user's library right now. The
  `ON DELETE CASCADE` hazard on collapsing two releases is documented and unresolved.
- **Cross-store dedup via `gamesdb.gog.com`** — **built for METADATA, not for dedup.**
  `Winnow.Enrich.GamesDb` now routes Epic titles to a Steam appid so they can be enriched
  (62 of 67). It deliberately writes no `external_ids` and no merge candidates: that table is
  keyed `(provider, provider_id)` globally, so putting a Steam appid on an Epic release would
  collide with the Steam release that already owns it. gamesdb also resolves *games*, not
  editions, so an Epic "Gold Edition" can land on the base game's record — right for the Work
  columns enrichment writes, wrong for a Release, and therefore never a merge. The original
  note follows.
- *(original)* — spiked and verified (`steam/224760` and
  `epic/Bluebird` resolve to the same `game_id`; 67/67 Epic titles resolved, 62 carrying
  Steam ids). Not built. This would collapse most of the merge queue automatically via hard
  ids rather than fuzzy title, which is exactly what §5.3 wants.
- **`SteamSyncService` — settled 2026-08-28.** Split, not just renamed: the old type
  awaited the Steam Web API and Epic OAuth behind a "filesystem-only" doc comment, which
  caused F04 (network calls blocking first paint and repeating on the 15-minute timer).
  `LocalLibrarySyncService : ILocalLibrarySync` handles the three local scans;
  `RemoteOwnershipSyncService : IRemoteOwnershipSync` handles entitlement backfill at
  6 hours. Both live in `Winnow.App.Services`, not `Winnow.Core.Ingest` as originally
  intended — `LibrarySyncReport` carries `ResolveResult`, and Core cannot reference
  Resolve. The no-network guarantee is enforced by `LocalLibrarySyncContractTests`.
- **Session detection is Windows-only in practice.** `GameExecutableIndexBuilder` matches
  `*.exe`, so off Windows the index is empty and nothing is ever recorded — it warns once
  rather than failing silently. Widening the glob is NOT the fix: under Proton the resolved
  executable is the wine loader inside the runtime directory, not a path under the game's
  install root, so the install-prefix join cannot work there at all. Attribution would need
  `STEAM_COMPAT_DATA_PATH` from `/proc/<pid>/environ` — a different design.
- **IGDB cache has no `payload_version`.** Adding a field to the cached shape silently yields
  empty results for 30 days rather than refetching. A latent trap, not yet a bug.
- **Account stats query surface exists with no UI in front of it.** The raw page rows are
  stored in `account_transactions` / `account_licenses` (migration 0014) and read by
  `IAccountStatsRepository`, but the account stats page is not built, so the query surface
  has no screen. The ownership columns `acquired_at` / `license_type` / `price_paid_cents`
  are still read by nothing; M6 export remains their intended first consumer. The fact
  tables cannot distinguish two identical same-day transactions, so an exact repeat purchase
  on one day is undercounted by one.
- **`OwnershipRepository.UpsertAsync` could overwrite an imported `acquired_at`** if a Steam
  candidate source ever starts supplying `AcquiredAt`. Today both hard-code null, so the
  safety is incidental. Worth an enforcing test when the field is next touched.
- **$0.00 purchase rows are skipped, not recorded as zero.** The importer drops them; whether
  to store a zero or omit entirely is a user decision, untested either way.
- **Saved-file licenses route captures one page per file.** The embedded route paginates
  automatically; the manual route inherently gets one page per saved HTML file. Multi-file
  merge in the loader is the fix if coverage ever matters.
- **Single-entry rail sections (deferred 2026-08-29).** With SETTINGS replaced by a
  gear-opened pane (2026-08-29, sibling change), the rail below the bucket divider is:
  ACCOUNT (one row, STATS), REVIEW (one row, SAME GAME?), LISTS, gear. Both ACCOUNT and
  REVIEW spend a section heading on a single destination; each reads as structural overhead
  for one row. No better shape is known; deferred until further feature development shows
  where these rows best land. Constraint: whatever replaces them must preserve the rail's
  stated grammar. Everything above the divider is a subset of ALL GAMES; below it, content
  precedes work queue precedes configuration.
- **Account stats presentation is a first pass; cleanup shelved (deferred 2026-08-29).**
  The screen is functional and its figures are correct. Presentation polish is low ROI until
  core functionality (M5, M6) is complete. What cleanup means concretely, so the future pass
  has a starting point: layout beyond the uniform card/StatRow table (visual hierarchy,
  grouping, spacing), and derived figures the stored facts already support that the first
  pass omitted. Candidates: per-transaction averages, per-year averages, percentage
  breakdowns across the spend-by-kind slices, cost per hour played and spend on games never
  launched (both crossing account data with the playtime M5 backfills).
- **Account-scope filter has two deliberate under-reaches (accepted 2026-08-30).** The
  Stores-panel toggle narrows the library to a single Steam account via `ownership_accounts`
  (migration 0015), and the filter propagates through `LibraryQueryRepository` to every
  surface. Two limits were accepted rather than solved. First, two surfaces still read
  the ownership-level `playtime_snapshots` series, which has no per-account form: the
  recommender's episode signal (what the feed scores on) and the details modal's
  snapshot history (what the playtime chart shows when a tile is opened). For a game
  two accounts play, both can diverge from what the filtered tile displays. The fix
  is a yours-versus-household episode distinction, not yet built. Second, the filter hides a game only when at least one
  non-seed `ownership_accounts` row exists and none names the owned account; a game with no
  per-account evidence stays visible. Epic and GOG entries pass the filter (it is
  Steam-scoped), as do any Steam appids no reader has attributed. Erring visible is the
  decision: hiding a game the user owns is worse than showing one they do not. Migration
  0015's seed rows are stamped `source = 'ownerships.account_ref'` and excluded from absence
  evidence because they inherit the single-winner ambiguity the table replaces; the first
  real sync supplies authoritative rows and the caveat retires itself.

## 7. The risk

This roadmap roughly triples Winnow's surface area. The realistic failure mode is not
technical — it is becoming a worse Playnite with an unfinished recommender attached.

The mitigation is ordering, and it is the reason M7 and M3 run ahead of everything visible:
**the feed must always be further along than the launcher.** If Winnow ever ships launcher
parity before the recommender is genuinely good, it has spent its differentiation budget on
catching up to a mature incumbent and has nothing left to be chosen for.
