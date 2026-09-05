# Decisions

Why things in Winnow are the way they are, what was reversed, and what a document used to
say before it was corrected.

## How this file works

**Append-only.** Entries are added at the end of the relevant section and never edited or
deleted. If a decision is itself reversed, the reversal is a new entry that names the one it
reverses; the old entry stays where it is.

**Nothing here binds.** Every rule an agent must obey lives in a domain document named by
`AGENTS.md`. This file holds the reasoning that was removed from those documents so they
could state rules without arguing for them, and the text of claims that were deleted because
they had become false. No document says "see `decisions.md`" for a rule, and no agent is
instructed to read it.

**It exists so that deleting rationale from a spec is not deleting it from the repository.**
A human asking "why is this like this" reads this file. An agent asking "what must I do"
does not.

**Entry shape.** A dated heading, the rule or document it concerns, and the reasoning. Where
an entry records a correction, it quotes the superseded text verbatim so the change is
legible without a `git log`.

---

## Naming and the 2026-08-28 rename

### 2026-08-28 — The product was renamed from Hoard to Winnow

The product, the assembly, the binary and the mascot were called Hoard until this date. The
rename covers anything hyphenated or possessive: `Winnow-launched`, `Winnow's own`,
`Winnow-owned`, "a Winnow theme".

### 2026-08-28 — Why "hoard" survives as a common noun

The premise of the app is *winnowing a hoard*, the dragon's pile of a thousand unplayed
games, so the common noun is load-bearing rather than a leftover. Four places use it
deliberately and a search-and-replace over them is a regression. The rule is in `AGENTS.md`;
this entry is the reason for it.

### 2026-08-28 — Why the DbUp journal has to be re-pointed

`DatabaseInitializer.RenameLegacyJournalEntries` re-points DbUp's `SchemaVersions` rows from
`Hoard.Data.Migrations.*` to `Winnow.Data.Migrations.*`. DbUp keys applied scripts by
embedded-resource name, and that name carries the root namespace. Without the re-point every
shipped migration replays against a populated database and `0001` dies on `table works
already exists` before the window opens.

### 2026-08-28 — Why the data-directory move falls back rather than failing

`WinnowDataLocation` moves `%LOCALAPPDATA%\Hoard` to `%LOCALAPPDATA%\Winnow` once. If the
move cannot be completed it reads the legacy directory in place instead. An install that
predates the rename holds the user's only copy of their library, so a half-completed move
that leaves the app pointing at an empty new directory is worse than not moving at all.

---

## Working practice

### 2026-08-31 — Why every clickable run needs `--data-dir`

Clicks in the running app write to the real library. This has already happened once during
development. The rule that a run you might click in passes `-- --data-dir <path>` exists
because of that incident, not as a precaution.

`Environment.GetFolderPath` uses the Windows shell API, so setting `%LOCALAPPDATA%` in the
environment does not redirect anything. The override had to be a command-line flag.

---

## Product scope

### 2026-08-26 — Storefront client credentials ship built-in

A sign-in button cannot ask the user for client credentials, and there is no version where
they supply their own: Epic issues no client that can read a personal library (an EOS portal
app is rejected with `invalid_client`), and GOG has no public developer portal for this. The
alternatives were "embed the launcher credentials" or "the feature does not exist". Heroic,
Legendary and the Playnite plugins all embed them. Winnow is the party distributing them and
that is a real cost; the realistic failure mode is Epic or GOG rotating a client and sign-in
breaking until Winnow is updated, not bans. The published Epic pair was verified live on
2026-08-26 rather than trusted.

This was recorded for a while in a table of "amendments to the design doc's §1 non-goals",
under a non-goal that had never been written. It amends nothing. It is a decision, and it
lives here; the rule it produced, that built-in credentials sit at the lowest priority in the
credential chain so a user-supplied pair always wins, is in `game-library-design.md` §4.8.

### 2026-08-28 — The recommender was promoted from phase 2 to core

Its phase-2 placement assumed it needed a server. It does not: all inference is local, over
the user's own database. It is the differentiator, so it moved into core scope.

For a while afterwards the design document carried the non-goal struck through beside its
correction, while a separate section further down still listed recommendations as explicitly
out of scope. Both are gone; §1 now states current exclusions only.

### 2026-08-28 — The launcher is not a feature bolted on for adoption

"Analytics tool" undersells Winnow, because analytics is a panel you open monthly and then
stop opening. "Launcher" oversells it in the other direction: Playnite is a mature open-source
launcher with a plugin ecosystem, and a straight race against it is one Winnow loses on
maturity alone. The defensible position is the intersection.

A recommender fed only by periodic library syncs sees one playtime number per game per sync,
the same impoverished view Steam has. A recommender fed by session detection sees when you
play, for how long, what you abandon mid-session and what you return to. That is a different
product, and it is why the launcher is the data-acquisition strategy for the differentiator
rather than an adoption tactic.

Two consequences fell out, both lucky. **M3 was already the launcher**: knowing when a game
stopped is the hard 80%, and actually starting one is a URI handoff (`steam://rungameid/440`)
that is nearly free. **M5 was already the cold-start fix**: historical playtime backfill gives
the recommender a real longitudinal series on install day, where a library synced this morning
has one snapshot per game, no sessions, and nearly every interesting signal degenerate.

### 2026-08-28 — Why M3 runs before M8, M9 delegates, and M10 is last

**M3 before M8.** The feed's quality is bounded by its input data. Shipping the feed before
session detection means shipping it at its worst and teaching users it is mediocre. Session
data starts accruing the moment M3 lands, so every week M3 is late is a week of history not
collected.

**M9 delegates.** Writing Winnow's own downloaders, Legendary-style, means owning CDN auth,
chunked delivery, patching and the support burden for corrupted installs, for the sole benefit
of avoiding a window appearing. Not worth it, possibly not ever.

**M10 last.** Full-screen gamepad mode is a second complete UI: focus management, controller
input, its own navigation model, its own layouts. It is the largest surface of the three
remaining asks and serves the narrowest slice of users. It is the right feature and the wrong
thing to build third.

### 2026-08-28 — Why GOG sign-in is held, and a correction

"GOG ingest found only 14 games" was recorded as a shortfall. It was not one. Galaxy's
database holds **45 owned GOG releases, of which 31 are DLC**, verified directly against
`LibraryReleases` joined to `ReleaseProperties`, the same join `GalaxyLibraryReader` uses.
`GogLibrarySource` drops DLC by design, so 45 − 31 is the 14 base games it reports. The local
reader reads the entire library correctly; the missing-games premise was invented rather than
measured.

The authenticated GOG endpoint then carries no playtime, no last-played, no title and no DLC
flag, all four of which the local reader already has. A GOG sign-in button would add a login,
a stored credential and an embedded browser in exchange for nothing.

### 2026-08-29 — M11, the appearance system, was not planned

Four themes, the transparency slider, the Acrylic/Mica choice, the island layout, the JSON
theme engine and the app icon appear in no earlier version of the roadmap. The work was
directed turn by turn while reviewing the running app, and it grew from "add a Mica effect"
into a system. It is recorded as a milestone because it is a system now and someone would
otherwise wonder where it came from, not because it was planned.

Two things it produced outlast it. A **measurement discipline for colour**: `Colorimetry`
walks AA ceilings per theme, per layout and per slider position, and the Appearance screen
prints the number live. It was built to settle arguments about transparency and it now
validates user-authored themes. And **a named cost every time contrast was traded**.

The cost is equally plain: it is polish shipped ahead of M3b and M8, the two milestones the
flywheel actually depends on. Nothing in M11 collects a session or surfaces a recommendation.

### 2026-08-31 — Recommending unowned games stays deferred

Beyond the missing catalogue access, there is a product-integrity tension. Winnow's premise is
*you own a thousand games and have played forty*. An app that opens with that diagnosis and
then sells you more games is incoherent, and users will read it, correctly, as the moment the
tool started working for someone else. The version that survives the objection is wishlist
intelligence, not a purchase feed.

## Architecture

### Avalonia was chosen over Electron

The deciding factor is that Winnow is a background daemon with a UI attached: it sits in the
tray enumerating processes every few seconds, all day, and the user interacts with it briefly
and occasionally. That profile favours a native toolkit decisively.

| Axis | Avalonia | Electron |
|---|---|---|
| Idle memory (tray-resident) | 40–80MB | 150–300MB, ~80–120MB with mitigations |
| Process enumeration | Native `System.Diagnostics.Process` | Shell out to `ps`/`tasklist`, or a native module |
| Distributable size | ~20MB trimmed / AOT | ~150MB |
| Startup time | Fast | Slow (Chromium init) |
| Steam-specific library support | ValveKeyValue, SteamKit2 | Weaker VDF ecosystem |
| 3D rendering | No first-class 3D | three.js / R3F, mature |
| Portability to a future web frontend | None | Renderer ports nearly free |

The last two rows were the entire Electron case, and both were tied to the 3D shelf view. With
the shelf cut, nothing argued for Electron.

**Accepted cost:** if a hosted service ever acquires a web portal, Avalonia XAML transfers
nothing to it where an Electron renderer would have transferred substantially. Registered,
accepted, not relitigated.

### Dapper over EF Core

EF Core does not play well with NativeAOT, and small footprint plus fast startup is a primary
reason Avalonia was chosen. EF Core also carries meaningful startup cost, felt on a tray app
that launches at login. Dapper plus an explicit migration runner keeps AOT viable and the
schema legible, and the schema is simple enough that an ORM's object-graph management earns
nothing.

The spec used to add "if the implementer prefers EF Core's migrations story, that is
defensible, but then drop NativeAOT". That door is closed: DbUp and Dapper are the shipped
choice, and three other documents already said so.

### 2026-08-28 — `SteamSyncService` was split, not renamed

The old type awaited the Steam Web API and Epic OAuth behind a "filesystem-only" doc comment,
which caused network calls blocking first paint and repeating on the 15-minute timer.
`LocalLibrarySyncService : ILocalLibrarySync` now handles the three local scans and
`RemoteOwnershipSyncService : IRemoteOwnershipSync` handles entitlement backfill at 6 hours.

Both live in `Winnow.App.Services` rather than `Winnow.Core.Ingest` as originally intended,
because `LibrarySyncReport` carries a `ResolveResult` and Core cannot reference Resolve.

### The `Score.*` module was never built

The module boundary table carried a `Score.*` row for a long time. No `Winnow.Score` project
exists: bucket derivation lives in `Winnow.Data` queries and scoring in `Winnow.Recommend`.
The row named a module that was never built, so the rule governing it had no addressee. The
boundary table now lists the projects that exist.

### `winnow-wrap` is specified and not built

The build spec opened §5.2 with "two mechanisms, both shipped". The launch-option wrapper was
deliberately deferred when M3b landed, and there is no wrapper project in `src/`. The section
now says which of the two is shipped.

### 2026-08-28 — Cross-store dedup via `gamesdb.gog.com`, and the note it replaced

`Winnow.Enrich.GamesDb` routes Epic titles to a Steam appid so they can be enriched, 62 of 67
on the user's library. It deliberately writes no `external_ids` and no merge candidates.

The note it replaced read: *"spiked and verified (`steam/224760` and `epic/Bluebird` resolve
to the same `game_id`; 67/67 Epic titles resolved, 62 carrying Steam ids). Not built. This
would collapse most of the merge queue automatically via hard ids rather than fuzzy title,
which is exactly what §5.3 wants."* Both halves sat in the same bullet, the second labelled
"(original)", which left a reader to work out which one currently applied.

The ambition is still live and is now TASK-70, which reworks cross-store identity as a link
relation rather than a destructive merge. What changed is the reason gamesdb alone cannot do
it: `external_ids` is keyed `(provider, provider_id)` globally, so putting a Steam appid on an
Epic release collides with the Steam release that already owns it, and gamesdb resolves
*games* rather than editions, so an Epic "Gold Edition" lands on the base game's record.

### 2026-08-29 — The refund-line bucket rule was reverted

"Never played" was briefly defined as anything under 2 hours, Steam's refund window.
Abandoned: a game the user demonstrably launched reading as "Never played" was confusing.
Never played now means never opened, zero minutes *and* no last-played date. 120 minutes
remains the floor for *Bounced*, which is a different claim about the same number.

`README.md` went on teaching the reverted rule as a headline feature until 2026-09-02.

### 2026-08-28 and 2026-08-30 — The Steam account-page rule, and the two amendments to it

The build spec said, flatly: **"Do not scrape either page."** It stayed that way while two
amendments accumulated in a different document, the second of which reversed a condition of
the first. Reading the current rule meant walking three layers in two files. §4.7 now states
the eight binding conditions in one place; this entry is the history.

**The original rule** prohibited scraping the authenticated Steam account pages, and pointed
at a GDPR export as the sanctioned path for the data. That export does not exist; see the
entry below.

**First amendment, 2026-08-28.** The saved-HTML importer gained an embedded-WebView peer
route: user-present, ephemeral session, two pages only, with the manual save-the-pages route
kept as a first-class equal rather than a fallback. Four conditions were stated as binding.
Condition 1 read:

> **Ephemeral session.** The WebView uses an in-private, in-memory profile. Cookies are never
> persisted to disk. The profile is torn down after harvest. Winnow never sees the password;
> it is typed into Steam's own page, and Steam Guard works normally.

The argument for calling it an amendment rather than a violation: the spirit of the rule is
that Winnow must never hold or exfiltrate the user's session or impersonate their browser, and
a user-present, user-authenticated, ephemeral, two-page harvest honours that spirit. The
ecosystem precedent is the same class of risk already accepted for the Epic embedded sign-in;
Playnite's Steam integration and the Heroic/Legendary family both operate this way, and ToS
exposure is user-driven and low-volume.

**Second amendment, 2026-08-30.** A WebView sign-in can mint a `webapi_token`, a JWT usable
against all three Steam Web API endpoints Winnow depends on, resolving the signed-in account
exactly via its `sub` claim. The token lives about a day (24h 22m, measured). Renewing it
without the user present requires persisting Steam's `steamRefresh_steam` refresh token and
spending it against `/jwt/finalizelogin`.

The decision was to persist that refresh token under DPAPI CurrentUser scope, the same
protection the Epic refresh token gets, so a signed-in user's scheduled syncs keep working
without a daily re-sign-in. That is exactly what condition 1 was written to forbid, hence a
second amendment rather than a quiet reinterpretation. Condition 1 was dropped, conditions 2
and 3 were narrowed and extended, condition 4 survived intact, and four more were added.

**What it costs, stated plainly.** A refresh token is not as reliable as an API key. It can be
invalidated by signing in elsewhere; the long lifetime only applies if the user chose
remember-me; and one contrary community report exists against the `finalizelogin` route
(node-steam-session issue #56, 2026-05-20, unresolved). That fragility is why condition 8
exists: the user must know when their session is dying, and must know that a key would not
have this problem.

Two named secrets, encrypted at rest, spent only against a closed list of API calls, is not a
session hijack and is not browser impersonation. It is the same shape of credential the Epic
integration already stores, and it is narrower than the cookie jar the first amendment's
ephemeral profile held in memory.

### 2026-08-28 — There is no Steam GDPR export, and four documents described one

The original M5 assumed a downloadable GDPR export archive containing a per-session playtime
breakdown. That premise came from a single unreliable source (takeoutday.org) and was never
verified against Valve's own documentation. `docs/spikes/steam-gdpr-export.md` measured it:
**there is no downloadable archive.** Valve's Privacy Dashboard is a set of login-gated live
pages, and its playtime page carries cumulative totals only, the same shape Winnow already
ingests from `IPlayerService/GetOwnedGames` and `localconfig.vdf`.

Four places went on describing the mechanism as live: the build spec's §5.4, its §4.7 ("the
sanctioned path is the GDPR export"), its tech-stack table (AngleSharp, "GDPR export import"),
and the recommendation-engine charter ("the M5 GDPR-export importer backfills historical
playtime and is therefore the single biggest cold-start lever available"). All four have been
corrected. The deleted text said:

> **GDPR export import.** The user requests their data from
> `help.steampowered.com/en/accountdata` (support-ticket flow, not one-click), receives it, and
> points the app at the file. The export reportedly includes a **playtime breakdown** with a
> full record of every game and duration, plus **`ExternalLicenses`** covering
> third-party-key acquisitions.

The replacement scope, approved the same day, is three mechanisms: `ClientGetLastPlayedTimes`
for `first_playtime`, `GetUserYearInReview` for 2022 onward, and a saved-page importer over
exactly two account pages. The exit criterion did not change. The mechanism did.

### 2026-09-02 — Spike findings folded into the build spec

The spikes were described as "empirical verification results that OVERRIDE spec guesses",
which made the spec wrong in place and left an agent to reconcile the two per task. The
findings are now in the spec and the spikes are evidence only. What the spec used to say:

| Spec claim, now deleted | What measurement found | Spike |
|---|---|---|
| "`localconfig.vdf` playtime fields **[VERIFY]** — confirm exact key names (`Playtime`, `LastPlayed`, `playtime_two_weeks`)" | `Playtime`, `LastPlayed`, `Playtime2wks`. The third guess was wrong in two ways | `steam-local-files.md` §3 |
| "require either the store page HTML or `IStoreService`/`IStoreBrowseService`. **[VERIFY]** which endpoint is currently viable" | `IStoreBrowseService/GetItems`, keyless, batching 100+ appids; names need `IStoreService/GetTagList`. Plain `IStoreService` has no tag method, and the one-appid arithmetic does not apply | `steam-store-tags.md` |
| "The steamcmd.net demo was erroring during design — **[VERIFY]** availability, and keep local SteamCMD as fallback" | Alive and correct. Drop local SteamCMD: 250 MB and an open non-TTY output bug | `update-signals.md` |
| "`ISteamNews/GetNewsForApp`, filtered to community announcements" | Use `tags=patchnotes`: 527 items unfiltered, 74 for the feeds filter, 34 for patchnotes. A 403 means no feed for that appid, not throttling | `update-signals.md` |
| "IGDB `external_games` lookup by Steam appid / GOG id / **Epic catalog id**" | GOG id true, 13/14. Epic catalog id false, 0/73: IGDB stores Epic *offer* and *page* ids. GOG's own cross-store graph is the Epic join, verified 67/67 | `epic-gog-local-files.md` §19, §20 |
| §5's architecture named Epic and GOG readers with no paths | The Epic manifest, cache and registry paths; the GOG WAL hazard; the `gog_` release-key filter; the installer-locale title problem | `epic-gog-local-files.md` |
| §4.1 gave no correlation window, sentinel handling, or multi-account rule | ±7 days; the `"86400"` `LastPlayed` sentinel; enumerate every `steam3id` | `update-signals.md`, `steam-local-files.md` |

Two `[VERIFY]` markers survived the fold, because no spike settled them: the exact Steam 429
figures and the licensability of a HowLongToBeat source. Both are in §9 of the spec.

## The visual system

### The palette was a deep indigo-violet, and the violet family is gone

`Ground #16112A`, `Surface #1F1838`, `SurfaceRaised #2C2350`, `Line #3D3168` and
`TextDim #9B90C4` were an indigo-violet stage, chosen as "arcade-adjacent" before the
interface had been seen against 600 real Steam capsules. Three things were wrong with it.

Violet sits between teal and hot pink on the wheel, so the chrome read as a *third accent*
rather than as ground, and both signal colours lost force against it; `Flare` in particular
looked like a brand colour rather than an alarm. It fought the art: Steam capsules are mostly
warm and dark, and a violet field pushes them green by simultaneous contrast, which is the one
thing the cool-shift floor is trying to say on its own. And it had become the default dark-app
purple, the thing every generated interface reaches for.

**The replacement keeps a hued neutral rather than retreating to grey**: grey would have made
`Volt` a decoration sitting on top of the chrome instead of the chrome's own colour
intensified. `Volt` is unchanged. `Flare` moved 6° hotter, `#FF5C8A` to `#FF4D93`, and to full
saturation, because against a green-teal ground the old rose read as salmon. `Azure` moved 8°
toward cyan, `#5B9DFF` to `#57A8F0`.

`#FF5C8A` outlived the change in four agent-facing documents, which is how the wrong hex went
on being taught for months.

### The dormancy brightness floor was 0.60

It was calibrated against procedural placeholder gradients. Against real capsules, which are
themselves dark, it compounds, and in a library whose default sort opens on its most dormant
titles the ramp's dynamic range was spent before the first scroll. Per-tile legibility was
never the problem, so the fix was at the floor rather than the curve: **saturation, not
brightness, is what carries the dormancy signal.** It is `0.68`.

`0.60` outlived the change in the `avalonia-ui` charter and in the dormancy spike.

### The refund-line premise inside the badge rule

The unread-badge rule used to argue for itself like this: *"'Never-opened' here means zero
recorded playtime, not the `Never played` bucket. Since that bucket became everything under
the refund line, the two are no longer the same set."* The premise was false by the time it was
written down: the refund-line bucket was reverted on 2026-08-29, so `Never played` means never
opened again and the two sets coincide. The rule the paragraph states is still right; the badge
must read playtime rather than a bucket name, because a bucket definition can move again.

### The caption's fill, and the three amendments it took to get to two sentences

The rule went through four statements in one section, two of which reversed each other, and a
reader had to walk all four to learn a two-line rule. It is now stated once per layout in §9.
The history:

**As first written:** *"`Well` is one step darker than `Ground`, not lighter. Every desktop
platform puts a lighter caption strip above a darker body, which means the brightest band in
the window sits directly above the art. Inverting it makes the first inch of the window an
unlit lip."* The objection was real. The means were wrong: a `Well` caption above a `Surface`
rail is two chrome tones meeting at a corner, and a visible seam is three tones in the first
inch of a window whose thesis is that the art is the only thing worth looking at.

**Second:** the caption takes the rail's ink at the rail's alpha, making the chrome one
continuous bracket. The real claim was never "the caption must be the darkest thing"; it was
"the caption must not be the *brightest* thing, and the art must be the first thing on screen
with light in it."

**Third:** under the floating layout the caption takes `Well`, because the caption and the rail
no longer touch and what is continuous is the ground rather than the panes.

**Fourth:** the caption paints no fill at all in floating, so the caption and every gap are one
*surface* rather than two that agree, at every position on the slider.

### 2026-08-29 — Mica was chosen over acrylic, and the choice was reversed

Mica was chosen because it samples only the wallpaper: one image, bounded, measurable. Acrylic
was refused because it samples whatever is behind the window — *"no bound, no measurement, and
a rail whose legibility changes when the user alt-tabs."*

The premise was right and the conclusion was one generation out of date, for two reasons. **The
tame backdrop was not a legible backdrop**: Windows composes dark Mica by tinting toward its own
near-black base so hard that the wallpaper contributes almost nothing. Back-solved from the
composite behind the chrome on a real machine, the backdrop is `#201F1E` whether the wallpaper
is orange rock or blue sky, so at the far end of the slider the rail reads as *the chrome went
grey* rather than as the desktop showing through. And **the bound does not have to come from the
material**: white bounds every backdrop there is, wallpaper or window, so the palette is measured
against white across the whole slider and the Appearance screen reports the worst case live.
The real objection was to shipping a figure nobody could check.

Acrylic became the default and Mica stayed second in the hint list. Both are now a user choice,
because reading as a tone rather than as a view is a legitimate thing to prefer, and someone who
wants it should not have to give up transparency to get it.

### 2026-08-30 — The window ran at three tiers and now runs at two

Asked for on aesthetic grounds: *"make the rail and filters panes the same level of opacity as
the game grid/main pane. Then the background and titlebar should be the same, and somewhere
between where the background is now and the rail is now."* It re-derived four constants.

What shipped before ran at three levels: the shell at 100% in the gaps and 70% on the caption,
a **chrome** tier at 70% for the rail and the filter panel, and the wall at 35%. The middle
tier had no job left to do once the merge queue, Stores, Appearance and the list view moved
onto the field's ramp on the argument that they are content in the library pane's position
rather than window furniture. **The rail and the filter panel are content columns by the same
test**, and nothing about them was chrome except a token name.

`MinChromeAlpha` and `TranslucentSurface` were retired with the tier. The second is left in the
theme format so that no user theme needs editing; nothing reads it. `ChromeGround` had already
gone when the command bar moved inside the library pane.

**A second route to the ground's alpha landed within a point and a half.** The request was for a
value *between* admitting everything and admitting the old chrome's 70%. Transmittances compose
by multiplying, so the midpoint between two of them is the geometric mean rather than the
arithmetic one: `√(1.00 × 0.70) = 0.837`, an alpha of `0.163`. The legibility boundary and the
honest reading of "halfway" agreed, and the more conservative of the two was taken, `0.15`.

### The AA ceiling figures, and the four sets that were live at once

At one point the design system carried four different sets of AA ceiling figures in one file:
27/30/30/26, a six-row per-surface table, 27/31/30/26, and 30/31/31/31, plus a mapping from the
captions on the committed screenshots. Only the last is current, and it is the only one the
document now states.

The two that moved in opposite directions are the same change. A selected rail row used to set
the mark and now holds to 40 / 54 / 47 / 41, because the rail stopped being the most open
reading surface in the window. The *reported* ceiling barely moved, because the caption took
that position on a ground the restructure deliberately opened up. **The mark was never about the
rail. It is about whichever surface is most open and carries text.**

### The committed appearance screenshots state the three-tier figures

Every `compare--*` sheet in `docs/screenshots/appearance/` was captured before the two-tier
restructure, and several print numbers on their captions that are now wrong. **They are left as
they are**: a screenshot is a record of the build that produced it, and re-lettering one is
worse than saying which figures moved. Read them against this table.

| A sheet says | It is now |
|---|---|
| "worst chrome surface" / "a selected rail row" | the **title bar** on the window's ground, in the floating layout |
| `5.04:1` solid, `2.91:1` at 45% white, `1.01:1` at 100% white *(Winnow)* | `5.04:1`, `2.82:1`, `1.42:1` — the surface changed, not the palette |
| "past 27% the white figure drops under 4.5:1" *(Winnow)* | **30%** |
| the AA mark's position on the track | **30 / 31 / 31 / 31**, and taken across both layouts |
| "the chrome admits 70%, the wall admits half of it" | **the ground admits 85%, a pane admits 35%** |
| "a field admits half of what the surface around it admits" | **a field admits exactly what the pane around it admits**, by painting nothing |
| "Chrome only" / "Chrome and the wall" *(the reach choice)* | "The ground and the side panes" / "Everything but the covers" |
| "the cover wall never opens up at any setting" | it opens with the reach setting, at the pane tier |

**The window itself is the record that is kept current.** The Appearance screen measures the
running window and reports the worst case live, so a sheet that has gone stale is a picture of
an old build rather than a claim anybody is still making.

### 2026-08-29 — The command bar moved inside the library pane

It sat flush on the window ground with the caption, on a content-against-chrome line borrowed
from VS Code. On screen that produced a tall undifferentiated block of chrome in the first inch
of the window: a caption strip and a control strip in one ink, flush together, above three panes
that all started somewhere else. The line was drawn in the right place for the reference and the
wrong place for this window.

**The strongest evidence that the position was wrong** is that the floating layout stopped
needing to say anything about the bars. It used to repaint them, margin them 8px in to line
their gutter up with the pane below, and strip their bottom rule because there was a gap under
them rather than a pane. All three overrides went.

The move also corrected an alpha: the bar used to take the chrome's reach while the wall it sat
on took the wall's, so with the art field solid and the slider up you got a see-through strip
glued to the top of a solid field.

### Two themes were withdrawn

Winnow, Cold storage, Nightshift and Phosphor shipped first and differed in **hue and value and
nothing else**, so they read as four settings of one theme rather than as four themes.
Nightshift was Winnow with the lights off; Cold storage was Winnow lifted and cooled. Two were
withdrawn and the replacements separate on temperature, chroma strategy, value structure and
material.

Nightshift kept its name and changed its argument: what makes it a room of its own is not how
dark it is but where the contrast lives.

### `ItemsRepeater` was the recommendation, and then was not

The dormancy spike's token-file table recommended `ItemsRepeater` with `UniformGridLayout` for
the cover grid. A note dated later in the same file reversed it, and the package must not be
reintroduced. `UniformGridLayout` charges every item in a row for a trailing gutter when it
computes items-per-line for the scroll anchor but packs rows greedily when it places them, so
the flush-row geometry made the two disagree by one column at every window width. The cover
wall is `src/Winnow.App/Views/CoverWall.cs`.

The stale recommendation sat in the same document as its own reversal, in the layer that was
supposed to be the evidence.

### 2026-09-02 — The Same Game screen became the Merges queue

`design-system.md` §6 and §7, `AGENTS.md`'s layout notes. The screen was rebuilt to the
mock in `docs/merge_queue_design/`, whose README is the visual spec. Four conflicts with the
previous screen were put to the user and decided:

- **The HISTORY surface is retired.** Past link acts appear as resolved strips at the bottom of
  their section, across sessions, each with `Separate again`. The queue is the retraction
  surface for every relation, and nothing loses undo.
- **Escape keeps the app's one rule for it: back to the library.** The mock made Escape answer
  Different games, a permanent dismissal, and the app's standing rule that Escape always steps
  back toward the library won. `D` answers Different games.
- **The header and rail follow the mock's copy.** The rail row is `MERGES`, the title is
  `Merges`, the primary button is `Merge N selected`. §7 was narrowed from "never Merge" to the
  answer on a card, which is still `Same game` / `Different games`.
- **Per-member include checkboxes are gone.** `Same game` nests every row under the header. A
  member no proposal named with the header is still called out in the reason sentence; a wrong
  member means `Different games`.

Choices made without a question, because the data model left one answer:

- A row is a work, not a store entry. The link joins works, so an entry already carrying two
  store entries is one row wearing two chips rather than two rows one of which could be
  promoted over the other to no effect.
- Sections: a same-game group whose rows are owned on two or more stores is ACROSS STORES,
  otherwise EDITIONS; an expansion proposal is TEST BUILDS at kind `variant_of`, PARTS when the
  storefront's word is an episode or a season, and EXPANSIONS otherwise. A base game's packs are
  split by section before a card exists, so one card carries one link kind.
- Confidence: EXACT MATCH is the matcher's top band with identical normalised titles, or an
  expansion group every member of which a storefront declared; LIKELY is the top band or a
  corroborated heuristic; WORTH A LOOK is everything below the band. `Accept N exact matches`
  takes EXACT MATCH cards in ACROSS STORES only.
- Row thumbnails take the dormancy ramp, as the README asks. The previous screen's capsules
  did not ("the question is identity, not recency"); at 34×51 the rows read as library rows,
  and a library row fades.
- The rail row carries no count and does not recede: a screen is not a cut of anything, and
  the pending count is on the screen's own header. The row sits with FEED above the rule.
- `Button.ctl`, the sort flyout and `Border.chip` moved into `Themes/controls.axaml`, because
  this screen was their second surface.

What §6 used to say, verbatim:

> **Merge confirm queue.** Two covers side by side at 200×300, signal diff between them (title
> distance, year delta, publisher). Actions are `Same game` / `Different games` — never
> "Merge"/"Cancel", which asks the user to reason about the data model instead of about games.
> 
> **Each member states its store.** The store is the fact that decides whether a pair is one
> game on two storefronts. Every member carries its stores in the same outlined chip the tiles
> wear: 1px `Line`, radius 3, body face 9px, `TextDim`. Placement differs by density because the
> space does:
> 
> - **Pair layout** (`MergeMemberTemplate`, a fixed 200px column): the chips take their own line
>   under the year and entry numbers, in a `WrapPanel` so three chips (123.1px) never clip at
>   200px.
> - **Roster rows** (`MergeRosterRowTemplate`): the chips lead the metadata line, ahead of year,
>   entries and publisher, so down a roster the stores form a column at one constant x.
> 
> Members with no ownership row draw no chip row and keep the two-part automation name.
> 
> **The card has a maximum width of 840px and is centred.** The roster density sets the ceiling,
> not the pair: card chrome 44 + cover 200 + gutter 28 + roster row minimum 526.0 (member chrome
> 30, checkbox 16 + 14, chip cover 64, two 14px margins, the condensed evidence line at 271.7,
> and the "Keep this title" radio at 102.3) = 798. 840 clears that with slack for shaping
> variance, sits on §4's 4px grid, and is twice the 420px feed card measure. The pair layout
> needs only 750. Both densities take the one ceiling, and the primary keeps its 200×300 capsule
> at both, so the card's outer geometry never changes between them. Widths were measured against
> the bundled OFL faces at the exact sizes, weights, letter-spacing and padding the markup sets.
> This is a two-column comparison and not prose, so no reading measure governs it.

What §7's table used to say: `| Merge action | \`Same game\` | \`Merge records\` |`.

### 2026-09-02 — Per-row include restored, and a row opens its details

`design-system.md` §6. Reverses the fourth decision in the entry above ("Per-member include
checkboxes are gone"), on the user's instruction the same day, after the screen was tried:
a group that arrives with one wrong member needs answering without refusing the rest, and the
entries need comparing before the answer is given.

- **Each row carries an include checkbox at its end**, checked by default, on every row but
  the header. `Same game` links the rows still checked; a row left out has its proposals with
  the linked rows recorded as answered no (rejected for a same-game group, refused for an
  expansion group), so it neither comes back on the next sweep nor takes the rest of the card
  down. A proposal between two left-out rows is left pending, because the user said nothing
  about it. The dock's Undo reverses the whole answer: the act and the recorded refusals. A
  card with nothing checked cannot be answered `Same game`; `Different games` is the answer
  that leaves them all separate.
- **The header is chosen by a radio at the row's head**, not by clicking the row. The mock
  promoted on click; that gesture now opens the game's details through the library's own modal,
  which is drawn over every pane, so the entries can be compared side by side. Space on the
  focused row still promotes it. No radio is drawn on an expansion card, whose base is the
  header by the shape of the relation, and no checkbox on the header, which is always in.

The mock in `docs/merge_queue_design/` stays as it was drawn; this entry is where the screen
departs from it.

## Corrections made during the source-of-truth migration

### 2026-09-02 — `CLAUDE.md` and `AGENTS.md` were two copies of one file

They had drifted. `AGENTS.md` said domain agents live in `.Codex/agents/` where `CLAUDE.md`
said `.claude/agents/`, and the directory on disk was `.codex/agents/`, lower case.
`AGENTS.md` also omitted the `--data-dir` paragraph, the exit-code-2 sentence and the
`%LOCALAPPDATA%` finding, so an agent reading only `AGENTS.md` would click on the real
library.

`CLAUDE.md`'s text was taken wherever the two diverged. `AGENTS.md` is now the single file
and `CLAUDE.md` is one line, `@AGENTS.md`, so Claude Code loads it.

### 2026-09-02 — `.codex/agents/` deleted

Seven `.toml` charters duplicated the seven `.claude/agents/*.md` charters with six
divergences, all defects: no delegation block, no model pins, `docs-writer.toml` untracked in
git and pointing at `AGENTS.md` while claiming a "Codex-opus-4-6" model, and
`avalonia-ui.toml` stored with literal `\r` escapes. Codex is not in active use on this
project, so the tree was deleted rather than generated from `.claude/agents/`.

### 2026-09-02 — Documents no longer state precedence over each other

`CLAUDE.md`, `AGENTS.md`, `README.md` and `ROADMAP.md` each carried a different ordering of
the same documents, conditional in places: the roadmap "supersedes the design doc's §8
milestones and amends its §1 non-goals", the spikes "OVERRIDE spec guesses", `README`'s
table listed six documents "in precedence order". Reconciling that chain per task was
producing inconsistent answers.

The chain is replaced by one document per domain, listed in `AGENTS.md`. Where a document
was wrong it has been edited to the current truth and the sentence it used to say appended
here, rather than corrected in place by a later document.

### 2026-09-02 — What `AGENTS.md` used to say about the visual system

The authority section carried visual values directly: "Flare (#FF5C8A) marks ONLY unread
updates; all numbers render in IBM Plex Mono `tnum`. Root `tokens.axaml` is the design
RECORD; the compiling copy is `src/Winnow.App/Themes/tokens.axaml` — change tokens there.
Fonts are static OFL cuts (Avalonia 11 has no variable-axis API)."

The hex was wrong: the shipping `winnow` theme seeds `#FF4D93`. Visual values now live only
in `design-system.md` and `tokens.axaml`, which is why `AGENTS.md` states none of them.

### 2026-09-02 — The second `tokens.axaml` deleted

A copy at the repository root was described as "the design RECORD", with
`src/Winnow.App/Themes/tokens.axaml` as "the compiling copy". The two had diverged: the root
file held 93 keys, the compiling copy 121. Every one of the 93 was present in the compiling
copy with an identical value, so the root file held nothing the app had not adopted; it was
simply 28 keys out of date, all of them the scrollbar and text-style entries.

The root copy is deleted. `src/Winnow.App/Themes/tokens.axaml` is the only token file.

### 2026-09-02 — Charters thinned to what exists nowhere else

The seven `.claude/agents/*.md` charters restated spec rules, and every restatement had
drifted. `avalonia-ui` taught `Flare #FF5C8A` where the shipping theme seeds `#FF4D93`, and a
dormancy floor of `0.60` where the spec and the cover pipeline both say `0.68`. `data-layer`
named the buckets "Never touched / Bounced / Stale-but-patched / Retired / Dead", a vocabulary
the copy table forbids. `enrichment-api` predated three spikes and named none of what they
found. `recommendation-engine` cited the M5 GDPR-export importer, which does not exist, and
forbade wiring the module into the UI, which M8 depends on. `winnow-reviewer` named "the
project's two authority documents" where four other files named four to six.

A charter now carries a pointer to the document that governs its domain, plus the content that
exists nowhere else, plus the delegation block. No charter carries a hex value, a threshold or
an endpoint parameter.

### 2026-09-02 — Spikes stopped being able to override anything

The spikes were described as empirical results that override the specs, which made the specs
wrong in place and put the current rule in whichever document the reader happened to open. The
findings are now in the specs (see the fold entry above) and each spike carries one line saying
it is evidence and naming the section that holds the rule.

Two spikes were also carrying claims about each other. `epic-oauth.md` opened with *"This
supersedes sections 21–22 of `epic-gog-local-files.md` ... Where the two disagree, this
document wins"*, which is a precedence claim made inside the evidence layer, the same pattern
this migration removes. It now states plainly that it probed what the other inferred. And
`avalonia-dormancy-rendering.md` held its own reversal: a note dated 2026-08-24 said the
`ItemsRepeater` recommendation was withdrawn while the table further down still made it. The
table row now says so where a reader meets it.

### 2026-09-02 — The em-dash rule was dropped

The `docs-writer` charter said "**Never use emdashes**, separate ideas with commas, semicolons
or periods." Every document in the repository uses them heavily, including the ones docs-writer
authored. The corpus is the fact: the rule was never followed and is removed rather than
enforced retroactively over the whole corpus.

### 2026-09-02 — What the enforcement pass could and could not hold

Sixty-four tests now assert rules the documents state. Four notes on where the line fell.

**The documentation check reads only qualified cross-references.** A bare `§6.1` means the
build spec through most of this corpus and means this file inside the design system. A test
that guessed which would be noisy or vacuous, so it checks the references that name the
document they point into, which is the regression that actually happens: renumbering a
document while another cites it.

**Two boundary checks carry named exceptions rather than a clean absolute**, because the
shipping code disagreed with the plan's reading of §5.1 and the code was right:

- The account stats view model reads `Winnow.Ingest.Steam.AccountPages` and the Stores view
  model reads `Winnow.Enrich.SteamWeb.Credentials`. Neither is a reader or a client: one is
  the shape of a parsed page, one is a set of credential interfaces the composition root
  wires. Both would sit more honestly in `Winnow.Core`, and the exception list is how that
  stays visible.
- `merge_candidates.score` is a stored column matching the derived-value pattern. It is the
  soft matcher's confidence in one specific pair, recorded at the moment it was queued so a
  human reviewing the queue can see what the machine thought, and §6's schema declares it.
  Re-deriving it later would answer a different question, because the matcher will have moved.

**The IL walk was abandoned.** A first attempt read method bodies to ask which *type* reached
an ingest namespace. A linear scan for token-carrying opcodes over-reads by construction: an
operand that happens to look like an opcode produces a spurious token. The two rules that
needed call-site granularity are source scans instead, which also give file and line, and the
metadata reader keeps only the table reads, which are exact.

**Several rules the plan proposed to enforce were already enforced.** The no-network guarantee
on the first-paint path is `LocalLibrarySyncContractTests`; the HTTP policy chains and request
shapes are the per-client resilience and contract tests; the theme walk and the layout token
parity are `ThemeContrastTests` and `FloatingLayoutTests`. Those were left alone rather than
duplicated.

## The library grid's expansion-grouping preference

Taken with the user on 2026-09-03, closing the last acceptance criterion of the expansion
relation work. The criterion had been left unbuilt because the stage that introduced it also
required an expansion link to move no tile count, and the two read as contradictory.

**They are not contradictory, because the preference is off by default.** With it off an
expansion link still changes no count, no playtime, no bucket and no recommendation, which is
the whole of what `expansion_of` promises. Turning it on is a presentation choice about the
grid, made by the user, and nothing about the link itself changes.

**When it is on, the pack folds into its base game's tile and the counts follow it.** The
alternative considered was drawing the pack attached to its base while still counting it as its
own title. That was rejected because the counting rule is stated per tile: a game that has no
tile cannot go on counting as one without the rail and the grid disagreeing about what the
library holds. Playtime is the exception and never rolls up — thirty hours of a base game and
none of its expansion are two facts, and their sum is a number no source reported about either.

**The base tile carries a mark when a folded pack has never been played.** Folding takes the
pack's tile away and with it its place on the Never played rail, and "you played this for two
hundred hours and never opened the expansion" is the recommendation the app exists to make. The
mark is a count in the same pip the multi-store mark uses, and the words are in the tooltip and
the automation name.

**It is applied above the shared bucket query, not inside it.** The recommender reads the same
repository the grid does, so folding down there would have taken an unplayed expansion out of
the feed at the same moment it left the grid — the opposite of the reason the mark exists. The
fold is applied where the tiles are built, and the rows the recommender consumes are untouched.

### 2026-09-03 — What the documents used to say about credentials at rest

TASK-78 closed the last plaintext gap, and three documents said something they no longer do.
Their superseded sentences, verbatim:

`README.md` used to say, under "Where your data lives":

> Credential protection is uneven today. Epic refresh tokens and Steam session tokens are
> encrypted at rest with DPAPI (`CurrentUser` scope). Steam Web API keys and IGDB client secrets
> are still plaintext rows in the local database, so anyone with access to `winnow.db` can read
> those two. Fixing that is TASK-78 in the backlog.

`game-library-design.md` §4.7's second condition used to end:

> The same standard is intended for every secret Winnow keeps; the Steam Web API key and the
> IGDB client secret do not meet it yet and are tracked as debt in `ROADMAP.md` §6.

`ROADMAP.md` §5's debt table used to carry the row:

> The Steam Web API key and the IGDB client secret are still plaintext at rest | TASK-78

**Why the change, beyond the fact it names.** The Steam Web API key and the IGDB client secret
were written raw to the `settings` table, and the cached Twitch access token with them, while
the Epic and Steam session stores already refused to persist anything unencrypted. The
documentation overpromising ("credentials use DPAPI") was its own defect: the privacy story is
part of the product. All five stored credentials are now DPAPI-encrypted under CurrentUser
scope with versioned per-credential entropy, plaintext rows from pre-protection installs are
migrated on first read and left empty, and a host that cannot encrypt refuses to store rather
than degrading to the clear — the standard §4.7's second amendment already bound the sessions
to. One distinction was drawn while doing it, and is recorded in §4.7: refusing never destroys
what a user typed, so legacy user-supplied rows are left as they were on a host that cannot
encrypt, while machine-minted rows — the token — are emptied, because a mint is free and a
bearer credential in the clear is not.

### 2026-09-03 — Shelf capacity in the scoring spec was stale in two places

`docs/recommendation-engine.md` §5's tuning table and §6a's shelf rules list both described a
ten-item shelf and a genre cap of 4. A shelf has shown six cards since the sections started
wrapping (`FeedService.VisiblePerShelf` = 6), and the genre cap moved from 4 to 3 in the same
change to preserve the property that no genre may take a majority. Both were corrected.

The superseded text, verbatim:

§5, `ShelfGenreCap` row: "Entries sharing one genre per 10-item shelf — below half, so no
genre can majority a shelf."

§6a, "Shelves own their stories" bullet: "a patched game that missed the patched shelf's ten
slots waits for that shelf's rotation rather than leaking its (stronger) patch story onto a
rail telling a different one."

### 2026-09-04 — The scoring spec's status line and §9 said the UI was unbuilt

`docs/recommendation-engine.md`. The status line said "the loop's UI affordances are not
yet wired (the App layer owns them; the contract is in §6b)." §9's heading read "Wiring it
in later (not now)", and its text ended "The UI affordances themselves (the 'not for me'
button, the inspection list) are the remaining unbuilt piece, owned by the App layer." The
feed screen, its verdict buttons, the history screen, the undo and the receipt countdown
are built and shipped; the three statements were stale.

### 2026-09-04 — "Patched since" became "Patched"

`design-system.md` §6, §7, §10.1, §11, §12; `README.md`. The old label was an unfinished
sentence. The bucket id `stale_but_patched` is untouched; this is a label change only.

### 2026-09-04 — "Bounced off" became "Started"

`design-system.md` §7, §12; `game-library-design.md` §6.1; `README.md`. The bucket's rule is
purely a playtime band: at or above `bounced_floor` (120 minutes) and below `retired_floor`.
It says nothing about recency, so the bucket also holds games in active play. "Bounced off"
claimed the user gave up, which is false for part of the set.

The label was changed to fit the rule rather than the rule narrowed to fit the label, for three
reasons. The 120-minute refund floor is a deliberate, documented threshold rather than an
accident. Narrowing the rule would mean adding a recency term to a bucket family whose whole
point is that they are playtime bands computed as queries. And the bucket's precedence
relationship with stale-but-patched is stated in terms of that span.

The bucket id `bounced` is untouched; this is a label change only.

Superseded text from `game-library-design.md` §6.1:

> At or above it the user committed past the point of no return and gave up anyway, which is
> the fact "Bounced off" names.

Superseded text from `README.md`:

> *Bounced off* means you got past Steam's two-hour refund window and stopped anyway.

### 2026-09-04 — The transparency block stopped printing measurements (TASK-96)

`design-system.md` §14.2, §14.3, §14.6. The Appearance screen's TRANSPARENCY card used to
carry standing readouts: an AA tick on the slider track, two live contrast ratios against a
dark and a white desktop, the Mica composite hex, two admittance percentages, a
gaps-and-title-bar section, and a paragraph on how input fields paint no fill. All of that was
design-document material rendered as UI. A user tuning a slider needs the slider, the two
qualifiers (backdrop and reach), and a warning only when the setting they are holding actually
hurts legibility — not a permanent measurement panel.

The measurements were not lost. They stay in §14.3 and §14.6 and are asserted by
`ThemeContrastTests` and `FloatingLayoutTests`. Five sentences in `design-system.md` described
the screen printing figures it no longer prints, and were rewritten to the current truth.

Superseded text from §14.2:

> A second element declaring `ShellGround` would put every figure the Appearance screen prints
> out by the same factor, and nothing else would catch it.

Superseded text from §14.3 "What fixes the ground":

> `0.15` is the round step past the boundary, it buys 1 to 5 points on top, and it states as a
> pair of numbers the Appearance screen prints: **the ground admits 85%, a pane admits 35%.**

Superseded text from §14.3 "What it measures":

> **The range past the mark is a choice the user is allowed to make.** Being protected from it
> is not a service, and being ambushed by it is not either — so the Appearance screen draws the
> mark on the track and reports **both** numbers live, in Plex Mono `tnum`, with the worst-case
> figure turning `Amber` and naming the line it crossed once it does. `Amber` and not `Danger`:
> §2 gives `Amber` attention and `Danger` the one destructive act, and a setting chosen with
> the number in front of you is neither an error nor something to be undone for you.

Superseded text from §14.6 "Acrylic or Mica":

> That table *is* the argument for offering both, and a condensed form of it is on the
> Appearance screen beside the choice.

Superseded text from §14.6 "The field may open up; the tiles may not":

> **The Appearance screen prints both numbers** — how much of the window's ground is desktop,
> and how much of a pane — in Plex Mono `tnum`, so the relation is visible rather than
> asserted. It is a ratio and not a second slider on purpose: two percentages on one screen
> that mean different things is a worse screen than one quantity with a stated relation.

### 2026-09-04 — The list view now carries cover art

`design-system.md` §6. The list view's opening sentence used to read:

> Same data, no art dependency: title, store, playtime, idle, unread dot.

A library is recognised by its covers; a list of titles alone is a spreadsheet. Every row now
shows a 24x36 cover from the same cache and the same dormancy ramp the wall tile uses.

Two judgements the change rests on. The cover takes the resting ramp (`DormancyAlpha`) rather
than the hover-restored one (`DisplayAlpha`): the list's hover affordance is the row's
`ChromeRaisedHalf` veil, and a cover that also woke under the pointer would be a second hover
language on one row. A game with no cover gets the grid's placeholder gradient pair, but not
the placeholder's baked Bricolage title: at 24px wide no title is legible, and the row already
names the game in Display type beside the art.

### 2026-09-04 — The indeterminate-progress rule (TASK-79)

`design-system.md` §8. Winnow states; it does not spin. When the interface cannot state a
real proportion it says what it is doing and what it is waiting for, in words, in a status
field, and offers Cancel when there is one. The rule ratifies the pattern the Stores panel
already ships and generalises the first-run placeholder rule §7 already states.

Because the indicator is words, reduced motion has nothing to disable and the surface is the
same in both motion settings. An accessibility floor with no branch in it cannot be got
wrong, and that is the argument for the rule rather than a side effect of it.

Motion may be added to a status field but may never replace one, under four conditions:
(a) the words are the indicator and the motion is decoration over them; (b) at most one
moving element on a screen; (c) it is removed entirely under reduced motion, leaving the
words, by a style and never by a local `Transitions` value; (d) it is never the only thing
saying that work is happening.

Superseded text from §8:

> There is no rule yet for an indeterminate indicator, and none may ship before there is:
> TASK-79.

### 2026-09-04 — The settings surface now has three sections

`design-system.md` §15.1 and new §16. The gear at the foot of the rail held two settings
screens, PLATFORMS and APPEARANCE. It now holds three: PLATFORMS, LIBRARY, APPEARANCE. The
new LIBRARY section answers what is in the library and holds EXPLICIT CONTENT, HIDDEN GAMES
and ADDED BY HAND.

It is not under APPEARANCE, which is material and layout. It is not under PLATFORMS, which is
about connecting to a store. What decides the content of the library once it has been
connected is its own question.

Superseded text from §15.1's floating-layout table:

> **Merge queue · Stores · Appearance**

### 2026-09-04 — Two destructive acts, not one

`design-system.md` §2, §12.3, §14.3. Deleting a list was the application's only destructive
act. Deleting a hand-added game is a second one: it removes the ownership, then the release
when no other ownership hangs off it, then the work when it has no releases left. It asks
first and the question names what survives, which is §12.3's own rule applied a second time.
`Danger` is on its confirm button and on nothing else on that screen.

Superseded text from §2's palette table, the `Danger` row:

> Destructive affordance: the window close button's hover fill, and the confirm button on the one destructive act in the application (§12.3)

Superseded text from §12.3:

> It is the only destructive act in the application, and `Danger` appears on its confirm button and nowhere else on the strip.

Superseded text from §14.3:

> `Amber` and not `Danger`: §2 gives `Amber` attention and `Danger` the one destructive act, and a setting chosen with the warning in front of you is neither an error nor something to be undone for you.

### 2026-09-04 — The details modal is a third surface for list membership

`design-system.md` §12.3. The paragraph that described `Add to list` said it was "one control
for both views" and stopped there. The details modal now offers a per-list checkbox — a
different control answering a different question (which lists already hold this game, resolved
through `same_game` links). The paragraph was extended rather than replaced: the action bar's
button remains the one bulk control for the grid and the list view; the modal's ticks are
stated as the third surface they are.

Superseded text from §12.3:

> **`Add to list` is one control for both views.** The grid selects one tile, the list view selects many, and the button reads whichever is in force, naming the number once there is more than one. The picked set is derived from the selection in the view model rather than in the pointer handler, so arrowing across the wall arms it exactly as clicking does.

### 2026-09-04 — On-screen copy shortened to short phrases (TASK-95)

`design-system.md` §7 and §10.4; `game-library-design.md` §4.7 and §6.4. Explanatory
paragraphs throughout the interface were replaced with short phrases. §7 now states the
length rule: an explanation on screen is a short phrase, and only a closed list of contexts
may run longer. §4.7 gained the account-stats rules that governed the paragraphs being cut.
§6.4 gained the non-game filter's absence rule, previously stated in the Display preferences
flyout: a row whose type no store has stated is not a non-game entry and stays visible
either way.

Superseded text from §10.4's copy table, the "No summary yet" row:

> `No description yet. Winnow fills the year, publisher and summary in from IGDB as it works through your library.`

### 2026-09-04 — Two copy drifts in design-system.md corrected to match the shipped strings

Pre-existing drifts found during the TASK-95 copy sweep and corrected separately because they
were outside that task's criteria.

Superseded text from §7's empty-state bullet for Never played:

> *"You've played everything you own. Genuinely rare."*

The shipped string in `LibraryViewModel.cs` is "You've played everything you own past the
refund window. Genuinely rare."

Superseded text from §10.4's copy table, the "Provisional title" row:

> `Steam's local files gave an id and no name.`

The shipped string in `GameDetailsViewModel.ProvisionalNote` is "Name not yet available.
Showing the app id until metadata loads."

### 2026-09-04 — The explicit-content gate narrowed from 18+ to adults-only (TASK-101)

`game-library-design.md` §6.4; `design-system.md` §16.1. The explicit-content filter was
hiding games rated 18 for violence. IGDB returns every board for a work, so GTA V, Doom
Eternal, The Witcher 3 and Cyberpunk 2077 each arrived carrying `pegi:18` and were hidden
even though the intent was to filter adult/hentai/sex games, not mature-rated ones. The
user reported the problem and the gate was narrowed: explicit now means the `AdultsOnly`
tier only (`esrb:ao`, `acb:x18`, `adult_only_sexual_content`). The broad 18+ board ratings
sit at `Restricted18` on the new `MaturityTier` scale and are not explicit. The scale is
ordered and exists for TASK-103's rating-cap filter.

Superseded text from §6.4:

> Explicit when any rating token is in the adults-only tier (`esrb:ao`, `pegi:18`, `usk:18`, `cero:z`, `acb:r18`, `acb:x18`, `classind:18`, `grac:18`) or any descriptor token is `adult_only_sexual_content`. ESRB M and PEGI 16 are not explicit: this is an 18+ gate, not a maturity gate.

Superseded text from §16.1:

> Off, works whose stored maturity evidence reads as 18+ are dropped from the grid, the list view, the feed and every bucket count — one clause in the shared bucket query all four read.

### 2026-09-04 — The IGDB override control moved from ABOUT to the left column, and the candidate list gained a scroll bound (TASK-102, TASK-105)

`design-system.md` §10.1, §10.9. Two user-reported defects on the details modal's IGDB
override control.

**The placement rule was reversed by the user.** §10.9 placed the control as the footer of the
ABOUT section, on the reasoning that ABOUT is the IGDB record in prose and the correction sits
under the answer it corrects. The user tried it there and asked for it in the left column
instead, under the cover and ON DISK. The new placement follows the column's own rule: left is
the object — its art, its store id, its install path — and which game this IS is an identity
fact that sits with them. The superseded text, verbatim:

> **It is the footer of the ABOUT section, not a section of its own.** The modal already stacks ALSO COVERS, EXTENDS, EXPANSIONS, LISTS, ABOUT and the update list; a seventh panel is how the modal becomes a stack of panels. ABOUT is the IGDB record in prose, and the year, publisher and cover above it came from the same record, so the correction sits under the answer it corrects. At rest it is one quiet line and the modal grows by one row.

**The candidate list was unbounded and ran past the card.** IGDB search returns up to 20
results, so more than three is the normal case for a common title, and the user hit the overflow
while the control was still in the ABOUT footer, in the right column. Width was never the
problem; height was. The list drew every result at its full height, the rest band it sat in was
an Auto row, so the ScrollViewer in that row was measured against infinity and never scrolled,
and a Border does not clip, so the list was drawn past the bottom of the card and cut off at the
window edge. The candidates past the cut could not be seen or reached. The move to the left
column is a separate change (TASK-102) that landed in the same commit; it did not cause the
overflow and does not fix it. The fix is a scroll region of at most 208px, bounded by a star row
so the ScrollViewer has a finite constraint.

**§10.1 gained a general rule about the modal's scroll regions.** Each column's lower part sits
in a star row and scrolls inside whatever height the card has. The rule is stated there because
it applies to both columns, not only to this control.

### 2026-09-05 — A live IGDB pin now outranks the store capsule (TASK-106)

`design-system.md` §10.9. The cover-key precedence was store-first: a release carrying a Steam
appid took `CoverKey.Steam(appid)`, and only a release with no Steam appid fell through to the
image id in `works.cover_url`. Pinning via the wrong-game control rewrote `cover_url`, but for
a Steam-owned game — the common case — nothing read it, so the tile and the modal kept
showing the original appid's portrait capsule after an assignment.

The rule is now: (1) a live IGDB pin on this work, when its `cover_url` yields an image id;
(2) the Steam portrait capsule; (3) the image id in `cover_url`. A user reaching for the
wrong-game control is saying the storefront art is wrong too, so the pin wins. Clearing a pin
now reloads the library and reopens the modal, because dropping the pin changes the cover key
back to the store capsule and only a reload draws it.

Superseded text from §10.9:

> **The cover needs no separate refresh mechanism.** The IGDB cover key is derived from the stored cover URL's image id, so rewriting the URL is what refreshes the tile.

> **Clear is drawn only while a pin stands**, read when the modal opens. Clearing writes no metadata — it only stops the pin — so it does not reload; the metadata the pin wrote stays in place and the next automatic pass fills what is empty around it.

Two code comments said the same wrong thing and were removed in the same change:

> The cover needs no separate refresh because its key is derived from the stored cover URL.

> Returns the work to automatic resolution. No reload: clearing writes no metadata, so nothing on screen has changed except this control.

### 2026-09-05 — The Clear control moved under the cover art, and its wording gained "metadata" (TASK-117)

`design-system.md` §10.9, `GameIgdbMatchCopy.cs`, `GameIgdbMatchViewModel.cs`. The user asked
for the move after confirming TASK-106's cover fix: "the clear button could be somewhere
cleaner. maybe the left column under the cover art." The left column is the object column
(§10.1) — its art, its store id, its install path — and returning the work to automatic
metadata matching is an identity fact, so the control sits with them. A single link-styled
button fits 200px where a candidate row carrying cover, name, year and platforms does not, so
this is not a reversal of TASK-102: the search surface stays in the right column for the
reason TASK-102 already states.

`ClearedNote` was carried from "Returned to automatic matching." to "Returned to automatic
metadata matching." so the confirmation is the past tense of the tooltip's promise.

Superseded strings:

> `ClearTooltip = "Return to automatic matching"`

> `ClearedNote = "Returned to automatic matching."`

Superseded doc comments:

> The control sits in the left column, under the cover art and the install path, where the user searches IGDB by title and picks the right entry by hand.

> Standing note while a pin is live, read beside the Clear control.

> Sits in the left column of the modal, under the cover art and the install path, where the identity facts about the game live. At rest it is one quiet line; the search is disclosed inline, in the modal's own tree, never a flyout (an adorner layer does not exist inside a popup).

> The pin note and the Clear control travel together.

### 2026-09-05 — Three paragraphs in §10.9 corrected, and the id-search behaviour added (TASK-102, TASK-118, TASK-120)

`design-system.md` §10.9.

**The placement paragraph was false since TASK-102.** The disclosure moved to a `Wrong game?`
link in the action band (Band 3), with the search field and candidate list in the right
column's rest band. Only the Clear control is in the left column. The paragraph described the
whole control as living in the left column.

**The row-stacking sentence was false since TASK-102.** The candidate row draws full width in
the right column on one line rather than stacking in a 200px left column.

**TASK-118 added the platform trim and tooltip.** A horizontal StackPanel measured its children
with infinite width, so TextTrimming on the platforms never engaged and the text ran under the
assign button. The detail line is now a Grid with the year in an Auto column and the platforms
in the star column. The full platform list stays reachable as a tooltip.

**The scroll region height changed from 208px to 238px.** The un-stacked row is 68px (51px
cover plus 17px of padding and rule) where the stacked row was 83px. At 238 the region shows
three rows and half of a fourth.

**TASK-120 added the id-search behaviour.** The field now takes a title or an IGDB id. The
paragraph was added to §10.9 beside the input field description.

Superseded text from §10.9, the placement paragraph:

> **It is in the left column, under the cover and ON DISK.** It shipped under ABOUT and the user asked for it here. Which game this IS is an identity fact, so it sits with the other identity facts in the column §10.1 defines as the object column — its art, its store id, its install path. At rest it is one quiet line under ON DISK.

Superseded text from §10.9, inside "What a candidate row draws":

> The left column is 200px wide, so the row stacks: the cover in its own column, and beside it the name, then the year and platforms, then the assign control.

Superseded text from §10.9, the scroll region paragraph:

> **The candidate list is a scroll region of at most 208px.** IGDB search returns up to 20 results, so more than three is the normal case for a common title. The region shows two rows and most of a third: the row cut part way, together with the scrollbar, is what says there is more rather than the list ending silently.
