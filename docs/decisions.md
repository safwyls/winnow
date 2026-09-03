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
