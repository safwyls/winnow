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

### 2026-09-05 — Per-field metadata sources replaced a layered override model (TASK-119)

`game-library-design.md` §6, §6.4. Migration 0027 adds `works.background_url` and
`work_field_sources(work_id, field, source, set_at, PRIMARY KEY(work_id, field))`. Each
user-visible metadata field on a work now carries its own source, and that source is the
truth for that field. One value per field, one answer to where it came from.

The task was originally written as a layered override — an automatic value with a user
override stacked on top, composed by a precedence tower. The user redirected it: "we should
really have one source of truth, i think the real issue is granularity. each field we
currently populate via IGDB or Steam should stand alone." The per-field model deletes the
composition problem instead of solving it, and the same model yields both gestures the user
asked for: a metadata fetch rewrites every field in one pass ("take it all from this
record"); a manual edit sets one field and makes the user its source, leaving every other
field tracking its own.

§6.4's heading and opening line were extended from four tables / 0023-0026 to five tables /
0023-0027, and the pin paragraph was extended with the pin/field-source interaction. The
superseded text, verbatim:

> Hidden games, maturity evidence, hand-added entries and user-pinned IGDB mappings

> Four tables added by migrations 0023-0026. Each is designed so that an ingest pass cannot write, delete or overwrite it.

The opening claim about ingest passes remains true — no ingest path writes
`work_field_sources`. The sentence about clearing a pin was narrowed. The superseded text:

> Clearing the pin returns the work to automatic resolution; the metadata the pin wrote stays in place, and the next pass fills what is empty.

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

### 2026-09-05 — The shared IGDB game query carries platforms, reversing TASK-120 (TASK-121)

`game-library-design.md` §4.4, `design-system.md` §10.9. An IGDB id typed into the wrong-game
search returned a candidate row with a year and no platforms, while every
title-search row showed both. The id lookup rides
the shared `GetGamesAsync` query, which did not ask for `platforms.name`; the title search has
its own query body, which always has.

**TASK-120 recorded the opposite decision and this reverses it.** That task left the shared
query alone because widening it changes the cached shape, and changing the cached shape forces a
payload-version bump and therefore a one-time refetch of the whole library against a 4 req/s
API. The user judged that cost acceptable — "fix it right and fetch the platform. invalidating
the cache once is not an issue" — and asked for the shared query to carry platforms rather than
for a third query to be added.

**The one-time cost was measured, not assumed.** Two tests drive the real client through the
real 4 req/s limiter against canned fixtures, for the author's 967-game library. Asked for in
one call, the way `FacetSyncService` asks: 3 requests, in batches of 400, 400 and 167, which fit
inside the limiter's 4-permit bucket, so the limiter adds no delay — 158 ms end to end. Asked
for in 40-target slices, the way `EnrichmentSyncService` asks: 25 requests and 6 seconds, which
is the token bucket's own arithmetic. The two passes share the cache, so the whole cost is
between 3 and 25 requests and at most about six seconds, whichever pass reaches an id first.

**The bump nearly took the offline guarantee with it.** `GetGamesAsync` keeps a superseded
payload and serves it when no refetch is possible, so a machine with no Twitch credentials and
no network still gets an answer. That fallback knew only the bare pre-envelope shape. Every
payload on a current install is a version-2 envelope, which deserializes as a bare `IgdbGame`
with `IgdbId` 0, fails the `> 0` guard, and would have been dropped — so the bump would have
turned "a stale platform-less row" into "nothing at all" for an offline install, silently
repealing the guarantee the version mechanism exists to preserve. The fallback now reads an
older envelope first and falls through to the bare shape.

The §4.4 bullet had been stale since the version mechanism landed and was actively misleading by
the time it was rewritten: it described a cache with no payload version at all. Its superseded
text:

> The IGDB response cache has no `payload_version`. Adding a field to the cached shape yields
> empty results for 30 days rather than refetching. Bump a version field before changing the
> shape.

`design-system.md` §10.9 stated the same false thing about what the id-match row draws.
Superseded text:

> The id-match row carries no platform list, because the shared metadata query does not return platforms and widening it would force a full re-fetch against a rate-limited API.

Superseded doc comment on `IgdbManualAssignment.GetByIdAsync`:

> The returned `IgdbSearchResult.Platforms` is always empty. `IgdbGame` carries no platforms, and adding `platforms` to the shared `GetGamesAsync` query body would change the cached shape, requiring a bump to `IgdbClient.GamePayloadVersion` — which refetches every game in the library against a 4 req/s API (§4.4). The id row is identified by its id and its name, so the platform list is not what distinguishes it.

Superseded doc comment on `IIgdbAssignmentService.GetCandidateByIdAsync`:

> The candidate's `IgdbCandidate.Platforms` is empty — the enrichment layer's id lookup does not carry platforms; see `IgdbManualAssignment.GetByIdAsync` for why.

Superseded doc comment on `Apicalypse.SearchGames`:

> The field list differs from `Games`: `platforms.name` is what tells Prey (2006, Xbox 360) apart from Prey (2017, PS4/PC), and nothing else in the client needs it.

### 2026-09-05 — The IGDB collision refusal becomes a same-game offer, confirmed in place (TASK-122)

`design-system.md` §10.9, `game-library-design.md` §5.3. When a user assigns an IGDB entry that
another work already holds, the UNIQUE constraint on `works.igdb_id` refuses the pin. That
constraint is the insight: two works claiming one IGDB entry are the same game. The refusal now
becomes an offer that names and shows the holder, and the user confirms the link in place on the
details modal rather than being sent to the Merges queue.

**The user's decision, in their words: "confirm in place, dont route to queue."** §5.3 permits a
hard external-id join to auto-merge. Naming an exact IGDB id is a hard join, and the in-place
confirmation — which names and shows the other game — supplies the review a queue would otherwise
provide. The queue is where soft matches are cleared; this is not a soft match.

**The holder is the parent.** It carries the `igdb_id`, which is the first rung of the Merges
queue's own precedence ladder, and its metadata is the entry the user was reaching for.

**Nothing is pinned.** Pinning the child to an id another row holds is what the UNIQUE constraint
refused. The link is the whole answer; the absence of a pin follows from the constraint rather
than being a choice.

Superseded text from `design-system.md` §10.9's "Six states" paragraph:

> Four refusals — the game is no longer in the library, IGDB had no details for that entry, another game already holds that entry, and the write failed — each keep the controls in place under their own `Amber` sentence, and each says something different, because the third is the only one the user can act on.

### 2026-09-05 — The per-field editor joined the action band, and the band's capacity is now a stated cost (TASK-119)

`design-system.md` §10.9, new §10.10. TASK-119's per-field metadata editor added an
`Edit details` disclosure to the detail modal's action band (Band 3). The enumeration in §10.9
was corrected. The sentence it replaced:

> **The disclosure is a `Wrong game?` link in the action band (Band 3)**, beside Store page, All patch notes, Open folder and Hide.

The band now carries seven controls — the primary action, `Store page`, `All patch notes`,
`Open folder`, `Wrong game?`, `Edit details` and `Hide` — in a horizontal strip that does not
wrap. At every card width between the column's 422px minimum and 582px maximum, the full set
overruns the available space and clips. The cost is stated in §10.10 and is not fixed there; a
follow-up decides the remedy.

### 2026-09-05 — The action band's rarer controls folded behind a disclosure (TASK-123)

`design-system.md` §10.3, §10.9, §10.10, §16.2. The user's decision, in their own words:
"fold the rarer actions behind a disclosure." Wrapping and a second row were both ruled out.

**The remedy was chosen only after the overrun was measured.** A throwaway headless Avalonia
project at the app's own 11.3.20, with Skia text shaping and the repository's own Plus Jakarta
Sans files, put the controls through a real measure and arrange pass. The evidence is in
`docs/spikes/details-action-band-width.md`. The right column is 420px at the card's `MinWidth`
700 and 580px at its `MaxWidth` 860 — the figures previously carried in §10.10 and in this file
(422 and 582) were an estimate that did not subtract the card's own 1px border on each side.
Control widths: `Play` 67, `Install` 77, `Store page` 88, `All patch notes` 108, `Open folder`
94, `Wrong game?` 104, `Edit details` 88, `Hide` 51. The old strip, installed set, seven
controls: 660px — 240px past the 420px column and 80px past the 580px one. The old strip,
not-installed set, six controls (no `Open folder`): 566px — 146px past the 420px column, but it
fits the 580px column with 14px to spare. §10.10's claim that the full set overruns at every
card width was therefore true of the installed set and not true of the other one. The overrun is
a clip, not a wrap, confirmed by arranging rather than inferred. The new strip measures 357px
with `Install` and 347px with `Play`, and 361/351 while the disclosure reads `Close` — it fits
the 420px column with 59 to 73px to spare. The disclosed list measures 104 x 144px for its four
rows. A fifth row would add 38px of height and nothing at all to the width.

**The disclosure is inline, never a flyout.** §10.7 and §12.3's standing reason: Avalonia's
global `FocusAdorner` does not render inside a popup, because a popup is its own root and has no
adorner layer, so every ring in a menu here would have to be hand-drawn.

**The disclosed list sits above the divider, not in the rest band's scroll region.** An action
must not scroll out from under the control that disclosed it, so the list needs no
`BringIntoView` at all, unlike the surfaces §10.9 and §10.10 put in the rest band. Band 3 is
the right column's Auto row and the rest band is the star row below it; opening the list grows
the Auto row by 144px and the rest band absorbs it by scrolling.

**The list is vertical because vertical is the arrangement that survives the next control.** A
further control costs one row of height and no horizontal budget, so the strip cannot be pushed
back over the column's edge by the next addition.

**The disclosure control is `Button.secondary`, not `Button.link`.** `Store page` and `All patch
notes` are outbound links and draw in `Azure`; the disclosure acts here rather than leaving, so
it takes the panel's `Text`-ink treatment. Drawing it in `Azure` would make it read as a third
outbound link sitting beside the other two. The two classes have identical geometry, so the
choice costs no width.

Why each folded control was folded. `Open folder` is a filesystem errand rather than a route
into the game. `Wrong game?` and `Edit details` are corrections, which §10.9 and §10.10
already describe as consequential and rarely reached. `Hide` is a dismissal reached at most
once per game, and §16.2 already gives it a primary home in the library's context menu. Why
the two links stayed: §10.1's own diagram names them as the band, and `All patch notes` is a
route into the product's own loop — §5.2's notice, context, launch.

Superseded text from `design-system.md` §10.3:

> Beside it, `Store page` and `All patch notes` in `Azure`, and `Open folder` when there is a path.

Superseded text from `design-system.md` §10.9:

> **The disclosure is a `Wrong game?` link in the action band (Band 3)**, beside Store page, All patch notes, Open folder, Edit details and Hide.

Superseded text from `design-system.md` §10.10:

> **The disclosure is an `Edit details` link in the action band (Band 3)**, beside `Wrong game?`, in the same link idiom, because it is the same kind of act: correcting what Winnow believes about this game.

> **The action band is now at its limit.** It carries, at once: the primary action, `Store page`, `All patch notes`, `Open folder`, `Wrong game?`, `Edit details` and `Hide` — seven controls in a horizontal strip that does not wrap, in a right column between the card's 422px minimum width and its 582px maximum. Measured against those widths, the full set overruns the column at every card width and the strip clips rather than wrapping. A follow-up decides the remedy.

Superseded text from `design-system.md` §16.2:

> The action band places it as a link beside `Store page` rather than as a primary, because that band is about getting into the game and hiding is the quiet answer beside it.

### 2026-09-05 — The cover-key precedence in §10.9 listed three rungs; the code had four

`design-system.md` §10.9. The paragraph stated a three-rung cover-key precedence: (1) live
IGDB pin, (2) Steam portrait capsule, (3) stored `cover_url` image id. The code has had a
fourth rung — rung 0, user-set art via `winnow://user-art/<token>` — since migration 0027
landed with the per-field metadata editor. The document was behind its own code since that
work, not since the title-rename fix that prompted this correction.

The paragraph also described the ladder as the library load's alone. The Merges queue now uses
the same four-rung ladder; it previously had its own store-first ladder carrying neither
user-art nor the IGDB pin, so an imported cover or a pin that drew correctly on the grid and
in the details modal did not draw in the queue.

Superseded text from §10.9:

> **A live IGDB pin outranks the store capsule for that work.** The cover-key precedence is: (1) a live IGDB pin on this work, when the work's `cover_url` yields an IGDB image id; (2) the Steam portrait capsule for this release's appid; (3) the image id in the work's stored `cover_url`.

### 2026-09-05 — The text-save paragraph in §10.10 was true but incomplete

`design-system.md` §10.10. The paragraph said a text save does not reload and stated the
asymmetry with art saves as the reason the two paths are separate. Both claims were correct
but the paragraph did not say what a text save *does* — the in-place rename of every tile,
the provisional-badge clear, the gradient recomputation and the re-applied sort. A user set a
name, saw the library unchanged, and reported the override as not working. The paragraph now
states what the save does rather than only what it does not do.

Superseded text from §10.10:

> **A text save does not reload.** Reloading after one would discard the drafts the user has in the other five rows. This asymmetry is deliberate and is the reason the two paths are separate.

### 2026-09-05 — The action band's inline list became a menu (TASK-125)

`design-system.md` §10.3, §10.7, §10.9, §10.10, §12.3, §16.2. The four actions folded behind
`More` — `Open folder`, `Wrong game?`, `Edit details` and `Hide` — were drawn as buttons in a
vertical inline list in the modal's own tree. They are now rows in a `MenuFlyout` placed
`BottomEdgeAlignedLeft`, wearing the `actions` class from `Themes/controls.axaml`, the same
treatment the library grid's context menu wears.

Two reasons. A menu draws its own mark inside the item template — one step of fill above the
menu's ground plus a constant-thickness `Volt` border — so the adorner-layer objection that
forced the inline list does not apply to it. And it makes the modal agree with the library
grid, which has used that same menu treatment all along, instead of the two being separate
grammars.

The menu floats over the modal rather than sitting in a row of the card's grid, so opening it
costs the strip no width and the modal no height. The inline list used to grow Band 3's Auto
row by 144px and push the rest band down. The trigger no longer changes face: it always reads
`More`, because the menu owns whether it is open.

The keyboard evidence is in `docs/spikes/details-action-band-menu.md`.

Superseded text from §10.3:

> Beside it, `Store page` and `All patch notes` in `Azure`, and the `More` disclosure — four controls on the strip. `Open folder`, `Wrong game?`, `Edit details` and `Hide` are folded behind the disclosure and drawn in a vertical, left-aligned column directly beneath the strip, above the divider that separates Band 3 from the rest band. The `More` control becomes `Close` while the list is open; its tooltip is `Folder, corrections and hide`.

> **The disclosed list is inline, never a flyout** — §10.7's rule and §12.3's standing reason: Avalonia's global `FocusAdorner` does not render inside a popup, because a popup is its own root and has no adorner layer, so every ring in a menu here would have to be hand-drawn. Each folded control keeps the idiom it already had.

> **The disclosed list sits above the rest band's scroll region**, not inside it. An action must not scroll out from under the control that disclosed it, so the list needs no `BringIntoView`, unlike the surfaces §10.9 and §10.10 put in the rest band. Band 3 is the right column's Auto row and the rest band is the star row below it; opening the list grows the Auto row by 144px and the rest band absorbs it by scrolling — the same structural property §10.1 already relies on.

> **Vertical is the growth answer.** A further control costs one row of height and no horizontal budget, so the strip cannot be pushed back over the column's edge by the next addition.

> **Keyboard.** Tab order follows declaration order (§10.7): primary action, `Store page`, `All patch notes`, `More`, then `Open folder`, `Wrong game?`, `Edit details`, `Hide`. While the list is closed those four are not drawn and are therefore not Tab stops — the same disclosure contract §10.9 and §10.10 already use.

Superseded text from §10.7:

> **No flyout anywhere in this panel, deliberately** — an adorner needs an adorner layer and a popup is its own root, so any menu here would need its ring hand-drawn. Three links do not need hiding behind one.

Superseded text from §10.9:

> **The disclosure is a `Wrong game?` link in the action band's overflow list (§10.3)**, folded behind the `More` control with `Open folder`, `Edit details` and `Hide`.

> **Inline, never a flyout.** §10.7's rule, applied again: Avalonia's global `FocusAdorner` does not render inside a popup — a popup is its own root and has no adorner layer — so every ring in a menu here would have to be hand-drawn. The disclosure opens in the modal's own tree, which is also §12.3's reason for the action bar.

Superseded text from §10.10:

> **The disclosure is an `Edit details` link in the action band's overflow list (§10.3)**, beside `Wrong game?`, in the same link idiom, because it is the same kind of act: correcting what Winnow believes about this game.

Superseded text from §12.3:

> which is §10.7's reason for the detail panel having no flyout either.

Superseded text from §16.2:

> The action band folds it behind the `More` disclosure (§10.3) rather than placing it on the strip, because that band is about getting into the game and hiding is the quiet answer behind it; the context menu remains the route that acts on a whole picked set.

### 2026-09-05 — Menu rows keep one name, and disclosed sections carry their own close control (TASK-126)

`design-system.md` §10.3, §10.9, §10.10. TASK-125 turned the action band's folded actions
into a `MenuFlyout` and pinned that the trigger keeps one constant face because the menu owns
its open state. TASK-126 applies the same principle one level down, at the rows that disclose
sections.

Before, the two menu rows `Wrong game?` and `Edit details` renamed themselves to `Close` while
the section they disclose was open, and their command toggled. `Close` does not say what it
closes, dismissing a section the user is looking at meant reopening a menu to reach the row,
and a label that carries state is defensible on a permanently visible button but not on a menu
row that is only visible while the menu is open. It is the same reasoning that already fixed
the `More` trigger itself in TASK-125.

Each disclosed section now carries its own close control — a `×` glyph in the trailing Auto
column of the section's header row — reusing the modal's own close affordance rather than
inventing a new idiom. The section also gains a heading (`IGDB MATCH`, `EDIT DETAILS`) because
the menu row that named the section closes itself as the section appears, so without a heading
nothing on screen identifies the surface.

Choosing an already-open row no longer toggles it closed. The command is one-way: it scrolls
the section into view, because both sections open below the fold in the rest band's bounded
scroll region. For §10.9 the caret returns to the search field. Nothing standing in the section
is discarded: any same-game offer the user may be part-way through answering, any search
results, and §10.10's six drafts all survive. The editor does not reload. Closing is the close
control's job alone.

The close control returns focus to the `More` trigger — the control the section was opened from,
on the strip outside the rest band's scroll region, so it is always drawn and focus is never
dropped or left below the fold. The two places §10.9 folds itself on success — a landed
assignment and a landed same-game link — are unaffected: those reload the library and reopen the
modal, so focus there belongs to the reopened modal.

§10.3 gained a standing rule stated at the trigger level and referred to from §10.9 and §10.10.
No existing text in §10.3 was changed.

Superseded text from §10.9:

> Each row's assign control is still a Tab stop, and a row reached by Tab is scrolled into view, so focus is never left off screen.

Superseded text from §10.10:

> The rest band is a bounded scroll region and the editor opens below the fold, so pressing the disclosure scrolls the editor into view.

### 2026-09-05 — Accessible names sat where UIA drops them (TASK-129, TASK-30)

`design-system.md` §8. Four surfaces put their accessible name on an element that has no
automation peer of its own: the game tile (`GameTileView`, on `Border#Lift`), the feed card,
the rail's fetch-status field and a merge-queue row. The tile is every object on the cover
wall, so on a real library the great majority of what a screen reader met was nameless. The
fault had been in place unnoticed on all four, and it was invisible without a test: the
elements keep their children and nothing throws.

The findings that constrained the fix, verified against Avalonia 11.3.20's source rather than
assumed:

- `Control.OnCreateAutomationPeer` returns a `NoneAutomationPeer`, and `Border`, `Panel`,
  `Grid`, `StackPanel`, `DockPanel`, `ContentPresenter` and `ContentControl` do not override
  it. `NoneAutomationPeer.IsControlElementCore` is false; the Win32 provider maps that onto
  `UIA_IsControlElementPropertyId`, which is what Windows filters its control view on.
- `TextBlockAutomationPeer.GetNameCore` returns the `Text` and never calls base, so
  `AutomationProperties.Name` on a `TextBlock` is silently discarded. A `TextBlock` does raise
  a UIA name-changed event when its `Text` changes.
- `AutomationProperties.PositionInSet`, `SizeOfSet`, `IsRequiredForForm`, `IsColumnHeader` and
  `IsRowHeader` compile and are read by nothing. A search of the whole Avalonia repository
  returns exactly one hit each: their own declaration.
- `ControlAutomationPeer` raises a property-changed event for `IsVisible`, `Bounds`,
  `RenderTransform`, `VisualParent` and `AutomationProperties.ItemStatus`, and for nothing
  else. Changing a `Name` at runtime announces nothing.
- `AutomationProperties.AccessibilityView="Control"` is consulted before the peer's own answer,
  so it restores a pruned element with its name intact. `AutomationControlType.None` then
  reports as a UIA `Group`, and `ControlTypeOverride` can name it better.
- An `ItemsControl` reports as a `List` with no items in the control view, because every
  container is a `ContentPresenter` yielding a `NoneAutomationPeer`. An item's name belongs on
  the `DataTemplate` root, never on the control.
- `ContentControlAutomationPeer.GetNameCore` falls back to `Owner.Content?.ToString()`. The
  rail's bucket rows are `Button`s whose content is a `Grid`, so each announced the literal
  string "Avalonia.Controls.Grid".

The fix therefore differs per site, because the remedy available differs. Where there was a
peer-bearing control to move the name onto, it moved: the tile's onto its `UserControl` root,
the feed card's onto its `Button`. Where there was none, the element states
`AccessibilityView="Control"` and the name stays where it is: the rail's fetch-status field and
the merge-queue row. The rail's bucket rows gained names of their own, which also displaces the
`ToString()` fallback. The feed card gained a live `ItemStatus` for the verdict receipt and its
replacement countdown, because a name change there would have announced nothing.

TASK-30's count became a phrase rather than a set position. `PositionInSet` and `SizeOfSet`
being inert, "3 of 12" is not expressible, so the tile's name says "Patched since you played:
3 updates." The count is carried out of the same `major_update` query aggregate that gives the
badge its own timestamp, under the same acknowledgement watermark, so the words and the dot
cannot disagree.

**A game's unread count is the maximum across its store copies, never the sum.** Two copies of
one game receive the same patches, so adding them would report a number no storefront ever
pushed.

The rule is held by `tests/Winnow.Tests/Enforcement/AutomationNameReachabilityTests.cs`, which
scans every `.axaml` under `src/Winnow.App`, rather than by review. The failure mode is silent,
so there is nothing for a reviewer to notice and nothing for a manual pass to catch on the next
surface either.

Superseded text from §8:

> The unread badge is likewise backed by the rail count and a tooltip.

### 2026-09-05 — Screenshots and ratings: fetch, store, attribute (TASK-111, TASK-112, TASK-113)

Screenshots and ratings are fetched from IGDB and Steam, stored in `work_images` and
`work_ratings` (migration 0028), and attributed to their source. Three figures with their
counts, never a blend: IGDB's own users (`igdb_users`), IGDB's aggregation of external critics
(`igdb_critics`), and Steam's community reviews (`steam`). Steam keeps its own label ("Very
Positive") verbatim. Both tables are shaped on `work_maturity` (0024) — one row per (work,
source), observed and timestamped.

**The payload-version bump was measured, not assumed.** The earlier TASK-121 figure was
deliberately not quoted, because this change adds more fields than that one did. Measured
against canned fixtures through the real client: 967 games, 3 requests in batches of
400/400/167, 184 ms end to end against the local responder, cached payload 636,286 bytes total
or 658 per game — against 607,276 bytes, 628 per game, for the same games with the six new
fields absent. About 30 bytes and 4.8% per game, and no change to the request count, because
an Apicalypse `fields` clause is one request whatever it lists. The network time was not
measured: no live credentialed IGDB call was made, so the figure is the local cost of the
shape change and not a round-trip time.

**Screenshots did not get a second image path.** They ride the existing `IgdbCoverSource`, the
existing `CoverPipeline` and the existing disk cache. The only thing that distinguishes them is
the size token in the CDN path, chosen from a new `CoverKey` provider `igdb-shot`. Cover and
Steam keys keep their exact tokens and their exact cache stems, so nothing already cached was
invalidated. Verified live 2026-09-05 by unauthenticated GETs against `images.igdb.com` using
`co6m51`: `t_screenshot_huge` returns 200 at 1280x720, 58,654 bytes, while a fabricated token
returns 404 — so the CDN discriminates between tokens rather than serving anything for any
path. The same probe reproduced the 2026-08-23 cover figures exactly (264x352 / 9,926 bytes
and 528x704 / 32,659 bytes).

**A pinned work refetches against its pin rather than re-resolving.** The pin says which game
it is; the refetch says fetch it again. Re-resolving would let a refetch quietly undo a
correction the user made by hand. The refetch is additionally held back by a per-work cooldown
on top of the two clients' existing Polly rate limiters.

Superseded text from `game-library-design.md` §4.4:

> The IGDB response cache carries a payload version per namespace: game payloads at **3**
> (name, summary, first release date, cover, genres, themes, game modes, player perspectives,
> platforms, publisher, `game_type`, `parent_game`, `version_parent`, `version_title`),
> age-rating payloads at **1**, search payloads at **1**. Change a cached shape and bump its
> version in the same commit, or the cache serves rows with the new field silently empty for
> the rest of the 30-day TTL. A payload whose version does not match is refetched, and the
> older payload is still served when no refetch is possible.

Superseded doc comment from `src/Winnow.Covers.Igdb/ArtKeys.cs`:

> IGDB screenshots written into `works.background_url` reach a tile through this same call, so the codebase does not grow a second image path.

Screenshots are now stored as image ids in `work_images` and keyed with
`CoverKey.IgdbScreenshot`, not written into `background_url`. The surviving point — that there
is still only one image path — is unchanged.

Superseded comment from `tests/Winnow.Tests/Enforcement/SchemaDisciplineTests.cs` (above the
`recordedObservations` allow list):

> The one stored score, and it is not a derived value: it is the soft matcher's confidence in one specific pair, recorded with the pair at the moment it was queued, so a human reviewing the queue can see what the machine thought. §6's schema declares it. Re-deriving it later would answer a different question, because the matcher will have changed.

`work_ratings.score` is now a second entry on that list. It is admitted under the same
principle, not as an exception to it: it is a figure IGDB or Steam published, recorded as
observed against the work and the source that published it, and nothing in Winnow computes it.

### 2026-09-05 — The details modal restructured for seven additions (TASK-111, TASK-112, TASK-113, TASK-115, TASK-38, TASK-21, TASK-30)

`design-system.md` §10.1, §10.2, §10.3, §10.5;
`src/Winnow.App/Views/GameDetailsView.axaml`.

Seven additions landed together: a reception line of attributed ratings (TASK-112), a lifetime
axis replacing the gap rail for games with sufficient data (TASK-115), a refetch-metadata menu
row with a live status line (TASK-113), an ACQUIRED block in the left column (TASK-38),
screenshots inside ABOUT (TASK-111), and accessibility names and heading levels across the
modal (TASK-21, TASK-30). The rest band's section order changed, and the update list heading
became the constant `UPDATES`.

Superseded text from `src/Winnow.App/Views/GameDetailsView.axaml`, the ALSO COVERS comment:

> First in the band because it is a fact about identity, and it draws only when there is coverage to draw.

The "first in the band" half is reversed: ALSO COVERS now follows the update list and ABOUT.
The "draws only when there is coverage" half still stands.

Superseded text from §10.2:

> **It is deliberately not a playtime chart.** The obvious move is a line through `playtime_snapshots`, and on a real library that table holds one reading per game — measured, 611 of 616 — so a line through one point is a decoration pretending to be evidence.

The snapshot table now holds a multi-year monthly series per game for any install that has run
the Steam Replay backfill. The lifetime axis draws that series as a two-zone chart: a flat band
for the pre-coverage span whose amount is known but whose shape is not, and one bar per
measured month for the rest. The gap rail is preserved as the fallback for games with no release
year or fewer than two month-end readings.

Superseded text from §10.3, the tooltip:

> Its tooltip is `Folder, corrections and hide`.

The menu gained `Refetch metadata` as its second row, so the tooltip is now `Folder, metadata,
corrections and hide`.

Superseded text from §10.3, the menu-row list:

> `More` opens a menu whose rows are `Open folder`, `Wrong game?`, `Edit details` and `Hide`, in that order.

The menu's rows are now `Open folder`, `Refetch metadata`, `Wrong game?`, `Edit details` and
`Hide`, in that order.

Superseded text from §10.5:

> `acquired_at`, `license_type` and `price_paid_cents` are in the schema and are populated only for a user who has run the saved-page import; `platform` and `edition_note` are empty for every row Steam's local files produce. **None of them is bound in this panel.** Purchase facts belong to the account stats screen, which is where they are read, and a row that appears for some users and not others in a panel about one game is worse than a row that is simply elsewhere.

`acquired_at` and `license_type` are now bound in the left column's ACQUIRED block for users
who have run the saved-page import. `price_paid_cents` remains unbound, for the reason §7
gives: the sentence this product must not write. The reasoning that a row appearing for some
users is worse than a row elsewhere no longer holds for facts about the copy that are in the
copy's own column; it still holds for the money.

Superseded text from §10.1:

> Three inner scroll regions in the modal carry the same problem.

The screenshot thumbnail strip in ABOUT is the fourth inner scroll region. It scrolls
horizontally, and its content carries `InnerScrollGutterBottom` — the same 20px as the vertical
regions' `InnerScrollGutter`, turned through ninety degrees.

### 2026-09-05 — The action band's control count is per-store, and the no-way-in sentence (TASK-131)

`design-system.md` §10.3, §10.4. The strip's control count was stated without qualification, and
the `Text`-ink enumeration was one short.

§10.3 said "four controls on the strip" as though every store had all four. Steam has four; GOG
has three (no patch notes link, one GOG Galaxy link instead of two web links); Epic has two (play
and the menu trigger) when installed and one (the menu trigger alone) when not. The sentence now
reads "four controls on the strip for Steam", and a per-store table states what each store
offers and what it does not. The Epic row names why: no verified install route, and no store-page
slug in any field Winnow stores. The GOG row notes that its store page and patch notes are
reachable through an anonymous API request Winnow does not yet make.

A no-way-in sentence was added for entries where there is no primary action and no link, in
`Text` ink, with three variant strings (unknown install state, missing identifier, no install
route). The `Text`-ink enumeration moved from nine to ten.

Superseded text from §10.3:

> Beside it, `Store page` and `All patch notes` in `Azure`, and the `More` control — four controls on the strip.

> Nine runs take `Text`.

### 2026-09-05 — Epic's install route is verified, the no-way-in variants drop from three to two (TASK-132)

Supersedes part of the entry directly above. That entry recorded "no verified install route"
for the per-store table's Epic row and "three variant strings (unknown install state, missing
identifier, no install route)" for the no-way-in sentence. Both are now false.

`design-system.md` §10.3, §10.4; `GameActionBandCopy.cs`; `StoreActions.cs`.

The root cause of Epic offering no actions at all was a data gap, not a missing verb. Of 67
Epic ownership rows, 0 held a complete launch triple because the namespace was read at ingest
and dropped. `catcache.bin` carries it for 67 of 67 owned base games, locally and offline.
Storing it takes the triple from 0/67 to 67/67. Verified 2026-09-05.

The install verb is `?action=install`, verified by execution against Epic Games Launcher
build 20.2.9 on 2026-09-05. Epic's own documentation names `?action=installer`, which routes
to `SelectiveDownloadUpdate` and is a no-op on an uninstalled game; it also names
`?action=updatecheck`, which is not registered in build 20.2.9. The per-store table's Epic row
now carries both Play and Install URI templates; the Not installed column and the explanatory
prose below the table were rewritten. The in-launcher store route
`com.epicgames.launcher://store/product/<slug>` exists (verified by execution via
`MainRouter`), but Winnow holds no product slug, so no link is built. See
`docs/spikes/store-actions-per-launcher.md` for the full evidence and the two methodological
failures that produced the original wrong findings.

The third no-way-in variant — the one for a store with no verified install route — no longer
applies to any store and was retired. `NoWayIn.NoInstallRoute` was removed from the enum; its
sentence was removed from `GameActionBandCopy`; an Epic game whose three-part key is incomplete
now classifies as `NoStoreId`. The §10.3 causes list now reads "Two sentences, one per cause"
where it read three. The §10.4 copy table dropped the corresponding row. The Text-ink listing
carries two no-way-in variants where it carried three; the count of ten runs is unchanged
because the no-way-in sentence is one run regardless of variant count.

Superseded text from §10.3:

> | Epic | `Play` — `com.epicgames.launcher://apps/<ns>%3A<catalogItemId>%3A<appName>?action=launch&silent=true`, and only when all three ids are held | nothing | nothing |

> The Epic Games Launcher exposes no install route Winnow has verified, and an Epic store URL
> needs a product slug that nothing Winnow stores holds. GOG's store page and patch notes are
> both reachable through an anonymous API request Winnow does not yet make, so they are absent
> rather than impossible. See `docs/spikes/store-actions-per-launcher.md` for the evidence.

> there is nothing to press. Three sentences, one per cause: the install state was never read,
> the store's identifier is not held, or the store has no install route at all.

### 2026-09-05 — The details modal scales against the window (TASK-133)

`design-system.md` §3, §5.5, §10.1. The card carried three absolute caps — `MinWidth="700"
MaxWidth="860" MaxHeight="720"` — with no relation to the window. On a 4K display the card
was about a fifth of the screen's width and the screenshot hero was not fully visible. The
card now scales against the window: `MaxWidth` is half the window's width (floor 860, ceiling
1582), `MaxHeight` is two-thirds of the window's height (floor 720, no ceiling). The two
floors reproduce the shipped card exactly at every window size the app allows. The ceiling of
1582 is the card width at which the screenshot hero is drawn at its native 1280x720
(`t_screenshot_huge`); past it every pixel is upscale. The caps are expressed as three named
`ScaledLength` resources bound to `$parent[Window].Bounds`, and all three are unit-tested at
seven window sizes. The hero image changed from `Stretch="UniformToFill"` (a cropped
horizontal strip) to `Stretch="Uniform"` (the whole frame), left-aligned, with a height cap
at three-tenths of the window (floor 200). A prose measure token was added to §3:
`ProseMeasure` = 410px, applied by the `.prose` class. The evidence is in
`docs/spikes/details-modal-scale.md`.

Superseded text from §5.5:

> That bitmap is upscaled to a card up to 860px wide, and the upscale is what softens it

### 2026-09-06 — Screenshots open in a lightbox overlay, not inside the modal (TASK-134)

`design-system.md` §10.1 and §10.7.

TASK-133 made the in-modal hero draw the whole frame rather than a cropped strip. That fixed
the cropping but not the framing: a screenshot competing for room with the reception line, the
axis, the action band and six sections is still a screenshot in a crowded column. The user's
direction after seeing the scaled modal: "rather than have them appear inside the details modal
i think we should do a popover image that dims the background and has a close button and
left/right navigation. bringing the focus onto the image and not smashing it in between the
other controls in the details modal."

The answer is a full-window **overlay**, declared in `MainWindow.axaml` as a sibling of
`GameDetailsView` in the window's own `Grid`, after it so it draws over it. Never a `Popup` or
a `Flyout`. §10.7's ban on flyouts is about popups having no adorner layer, so `FocusAdorner`
draws nothing in one; the detail modal is not a popup either, which is exactly why its focus
rings work, and the lightbox is the same pattern one layer up. §10.7 now records that reasoning
so the next reader does not have to re-derive it.

The frame is capped at 1282 x 722 — 1280 x 720 plus a 1px border each side — because 1280x720
is the native size of IGDB's `t_screenshot_huge`. `Stretch="Uniform"`, so a smaller window
shrinks the whole frame rather than cropping. Native is reached from a window of about
1424 x 864 up; at the app's own minimum window the shot is 6.3 times the area of the hero it
replaces, at the default window 6.9 times, and even at 3840x2160, where the hero was largest,
1.24 times.

**This entry supersedes part of the TASK-133 entry above.** That entry states that 1582 is the
card width at which the hero is drawn at its native 1280x720, and describes a third
`ScaledLength` resource, `HeroHeightCap`, at three-tenths of the window with a floor of 200.
The hero and that resource are gone. The 1582 ceiling itself stays, but it is now retained
rather than derived: what still argues for a ceiling is that nothing in the card rewards more
width — the object column is a fixed 200px and prose is bounded by the reading measure (§3). A
future pass wanting a different ceiling would have to measure a new reason for one.

One finding discovered rather than assumed: `CoverImaging.WidthBuckets` topped out at 640
pixels, so the in-modal hero was already a 640-wide decode upscaled — at 3840x2160 it was
drawn 1148px wide from a 640px bitmap, which is not what "drawn pixel-for-pixel" in the
superseded §10.1 text implied. A 1280 bucket was added so the lightbox draws at native. That
also retired the comment on `CoverImagingTests`'s widest snap case, which read
`// clamped: no display needs more than the capsule holds` and is no longer true.

The evidence is in `docs/spikes/screenshot-lightbox-scale.md`.

Superseded text from §10.1:

> **The card scales against the window, not the display.** Three named `ScaledLength` resources
> at the top of the view — `CardWidthCap`, `CardHeightCap` and `HeroHeightCap` — each bound to
> `$parent[Window].Bounds`. A `ScaledLength` carries a `Fraction`, a `Least` floor and an
> optional `Most` ceiling, so each cap is one named object rather than a number buried in a
> layout attribute. All three are unit-tested at seven window sizes
> (`tests/Winnow.Tests/DetailsModalScaleTests.cs`).

> **1582 is the card width at which the screenshot hero fills its native resolution.** IGDB's
> `t_screenshot_huge` is 1280x720. At 1582 the hero image is exactly 1280px wide and drawn
> pixel-for-pixel (the box is 1282px — 1px border each side); past it every further pixel is
> upscale. Nothing else in the card rewards more width: the left column is a fixed 200px, and
> prose is bounded by the reading measure (§3). See `docs/spikes/details-modal-scale.md` for the
> measurements.

Only the second sentence of the following paragraph was replaced — with "Picking a thumbnail
opens the lightbox (§10.7)." — while the rest of the paragraph was only re-wrapped and
survives unchanged. The second paragraph was deleted entirely.

> at 120x68, with a caption naming the count and the source. Picking a thumbnail expands that
> shot to a hero above the strip, inline in the modal's own tree — never a popup, §10.7's rule
> applied again, because a popup would need a hand-drawn focus mark per thumbnail. Each thumbnail
> is a real `Button`, so it is a Tab stop with the panel's drawn ring, and a thumbnail reached by
> Tab is scrolled into view — the same arrangement the IGDB candidate list (§10.9) uses. A game
> with no screenshots draws nothing at all, never an empty frame; that is a property of the data:
> no ids in `work_images` means no view model. The images ride the existing cover cache under a
> `CoverKey.IgdbScreenshot`, which resolves to `t_screenshot_huge` — an IGDB cover is 3:4 and a
> screenshot is 16:9, so the provider is what picks the size token; there is no second image path.
> The strip is a bounded horizontal scroll region, the fourth such region in the modal.
>
> The hero uses `Stretch="Uniform"` — the whole frame, never cropped — and is left-aligned so it
> takes the shot's own width instead of the column's, with no empty bars beside it. Its height
> cap is `HeroHeightCap`: three-tenths of the window's height, never below the 200px it had.
> Three-tenths reproduces roughly what shipped at the smallest window and grows from there: 200px
> was 0.29 of the modal host's height at a 720-tall window. The cost is stated honestly: the
> drawn box is narrower than the old full-width crop, because a whole 16:9 frame in a
> height-capped box is narrower than a horizontal strip that fills the column. From the default
> window up the shot's area is larger, and at 3840x2160 it is nearly three times the area; at the
> app's smallest window it is smaller in area but is the whole picture rather than a slice of it.


## 2026-09-06 — Agent instructions for Astra (TASK-140)

The user requested direct prose authorship without a docs-writer agent. Shared writing
guidance now lives in AGENTS.md; the six domain roles retain their technical scope and
inherit the selected model. Both harnesses carry equivalent role instructions. The design
skill now scopes its exploratory guidance to choices left open by Winnow's visual spec.
The Codex Backlog hook reads patch paths from tool_input.command instead of file_path.

Previous instruction text follows. Repeated delegation blocks are recorded once.

AGENTS.md:

> | Per-domain agent charters | `.claude/agents/` |

AGENTS.md:

> - Domain agents live in `.claude/agents/`. Delegate work by domain and pass the agent its
>   charter.

.claude\agents\avalonia-ui.md:

> ## Non-code text is delegated, always
>
> All non-code text — documentation files, README/ROADMAP/docs edits, code comments, XML doc
> comments, and any other prose — is authored exclusively by the `docs-writer` agent (pinned
> to claude-opus-4-6). Never write it yourself. Draft the technical facts, then delegate the
> wording via the Agent tool (`subagent_type: "docs-writer"`), passing the file paths and the
> facts to convey, and apply/verify what it returns. If you cannot spawn agents from your
> context, leave the text as a clearly marked `TODO(docs-writer)` and report the pending
> delegation in your final summary instead of writing the prose yourself.

.claude\agents\docs-writer.md:

> ---
> name: docs-writer
> description: Exclusive author of all non-code text for Winnow — documentation files (README, ROADMAP, docs/, design notes), code comments, XML doc comments, and any other prose. Every other agent delegates non-code text generation here; no other agent writes it. Always runs on claude-opus-4-6.
> model: claude-opus-4-6
> tools: Read, Grep, Glob, Write, Edit
> ---
>
> You are the documentation and prose specialist for Winnow, a game library manager. You are the
> ONLY agent permitted to author non-code text in this repository: markdown documents, files
> under `docs/`, code comments, XML doc comments, commit message drafts, and any other prose an
> implementation agent needs. Other agents hand you the technical facts; you produce the words.
>
> **Read `AGENTS.md` in full before writing anything.** Its "Where to read" table names one
> owner per domain, and a fact belongs in exactly one of them. Its naming rules are
> load-bearing: the common noun "hoard" is deliberate English in the places that table lists,
> and search-and-replacing it is a regression.
>
> ## Where a sentence goes
>
> - **A rule an agent must obey** goes in the domain document that owns it, stated imperatively,
>   present tense, with no reason attached.
> - **The reason** goes in `docs/decisions.md`, which is append-only. The log entry names the
>   rule it explains; the rule does not name the log entry.
> - **A measurement** goes in the spec as a finding. The spike stays as the record of how it was
>   learned, and is never the place to look up a rule.
> - **State** — what is shipped, what is deferred, what is broken — goes in `ROADMAP.md` or in a
>   Backlog task, never in a spec.
>
> **Edit a wrong section; never amend it.** If something makes a section false, rewrite the
> section to the current truth in the same commit and append what it used to say to
> `docs/decisions.md`. The words "supersedes", "amended", "superseded", "retired", "the original
> text" and "as first written" belong only in that log. A document that argues with itself makes
> every reader reconstruct the argument before they can act.
>
> ## House style
>
> - Plain declarative sentences. Lead with the fact, follow with the reason.
> - Record decisions with dates and evidence: "verified 2026-08-26", "measured, not assumed".
> - Never oversell. If a feature is partial, say what is missing.
> - Avoid jargon where simpler language carries the same meaning.
> - Brevity and clarity. Good documentation is to the point and conveys meaning with minimal
>   effort from the reader.
>
> ## Code comments
>
> A comment states a constraint the code cannot show: why something is load-bearing, what
> invariant a future editor would otherwise break. Never write comments that narrate what the
> next line does, describe where a change came from, or justify a diff to a reviewer. When an
> implementation agent hands you a comment request that fails that test, return "no comment
> needed" rather than writing filler.
>
> ## Discipline
>
> When editing an existing document, preserve its structure and voice, and make the smallest
> edit that carries the new fact. When asked for text about behaviour you have not verified,
> read the relevant source first; never document from the requesting agent's summary alone if
> the code is available to check.
>
> **You never modify code semantics.** If a comment edit would require touching executable
> lines, report what is needed instead of doing it.

.claude\agents\recommendation-engine.md:

> model: fable

.claude\agents\steam-ingest.md:

> - **This machine has a live Steam install at `C:\Program Files (x86)\Steam`.** Use it to
>   verify key names and formats empirically before coding against them. Most of §4.1 exists
>   because a widely-circulated answer turned out to be wrong when checked against it.

.claude\agents\winnow-reviewer.md:

> ## Non-code text is delegated, always
>
> All non-code text in this repository (documentation, code comments, prose) is authored
> exclusively by the `docs-writer` agent (pinned to claude-opus-4-6). Your review reports are
> exempt — reporting findings is your function — but if you are ever asked to author or fix
> documentation or comments, decline and report that the work belongs to `docs-writer`.

.claude\agents\winnow-reviewer.md:

> **Do not re-review by hand what a test already
> asserts** — check that the test still exists and still runs, and spend your attention on what
> no test can reach:

.codex\agents\docs-writer.toml:

> name = "docs-writer"
> description = "Exclusive author of all non-code text for Winnow — documentation files (README, ROADMAP, docs/, design notes), code comments, XML doc comments, and any other prose. Every other agent delegates non-code text generation here; no other agent writes it. Always runs on Codex-opus-4-6."
> developer_instructions = """
> You are the documentation and prose specialist for Winnow, a game library manager. You are the
> ONLY agent permitted to author non-code text in this repository: markdown documents, files
> under `docs/`, code comments, XML doc comments, commit message drafts, and any other prose an
> implementation agent needs. Other agents hand you the technical facts; you produce the words.
>
> **Read `AGENTS.md` in full before writing anything.** Its "Where to read" table names one
> owner per domain, and a fact belongs in exactly one of them. Its naming rules are
> load-bearing: the common noun "hoard" is deliberate English in the places that table lists,
> and search-and-replacing it is a regression.
>
> ## Where a sentence goes
>
> - **A rule an agent must obey** goes in the domain document that owns it, stated imperatively,
>   present tense, with no reason attached.
> - **The reason** goes in `docs/decisions.md`, which is append-only. The log entry names the
>   rule it explains; the rule does not name the log entry.
> - **A measurement** goes in the spec as a finding. The spike stays as the record of how it was
>   learned, and is never the place to look up a rule.
> - **State** — what is shipped, what is deferred, what is broken — goes in `ROADMAP.md` or in a
>   Backlog task, never in a spec.
>
> **Edit a wrong section; never amend it.** If something makes a section false, rewrite the
> section to the current truth in the same commit and append what it used to say to
> `docs/decisions.md`. The words "supersedes", "amended", "superseded", "retired", "the original
> text" and "as first written" belong only in that log. A document that argues with itself makes
> every reader reconstruct the argument before they can act.
>
> ## House style
>
> - Plain declarative sentences. Lead with the fact, follow with the reason.
> - Record decisions with dates and evidence: "verified 2026-08-26", "measured, not assumed".
> - Never oversell. If a feature is partial, say what is missing.
> - Avoid jargon where simpler language carries the same meaning.
> - Brevity and clarity. Good documentation is to the point and conveys meaning with minimal
>   effort from the reader.
>
> ## Code comments
>
> A comment states a constraint the code cannot show: why something is load-bearing, what
> invariant a future editor would otherwise break. Never write comments that narrate what the
> next line does, describe where a change came from, or justify a diff to a reviewer. When an
> implementation agent hands you a comment request that fails that test, return "no comment
> needed" rather than writing filler.
>
> ## Discipline
>
> When editing an existing document, preserve its structure and voice, and make the smallest
> edit that carries the new fact. When asked for text about behaviour you have not verified,
> read the relevant source first; never document from the requesting agent's summary alone if
> the code is available to check.
>
> **You never modify code semantics.** If a comment edit would require touching executable
> lines, report what is needed instead of doing it."""

## 2026-09-06 — Lightbox controls follow the image

The controls now overlay the image, freeing the space previously reserved for navigation
and close. The former design-system window-size measurements described that old layout:

**What a window produces:**

| window | overlay | shot drawn | the hero it replaces |
|---|---|---|---|
| 1200x640 (the app's own minimum) | 1200 x 604 | 882 x 496 | 352 x 198 |
| 1280x820 (default) | 1280 x 784 | 1136 x 639 | 434 x 244 |
| 1440x900 | 1440 x 864 | 1280 x 720 | — |
| 1600x900 | 1600 x 864 | 1280 x 720 | 476 x 268 |
| 1920x1080 | 1920 x 1044 | 1280 x 720 | 572 x 322 |
| 2560x1440 | 2560 x 1404 | 1280 x 720 | 764 x 430 |
| 3440x1440 | 3440 x 1404 | 1280 x 720 | 764 x 430 |
| 3840x2160 | 3840 x 2124 | 1280 x 720 | 1148 x 646 |

The shot is drawn at its native size from an overlay of 1424 x 828 upward, which is a window
of about 1424 x 864 once the title bar is taken off — so from 1440x900 up. Below that the
whole frame shrinks uniformly. At the app's own minimum window the shot is 6.3 times the area
of the hero it replaces, at the default window 6.9 times, and even at 3840x2160, where the
hero was largest, 1.24 times.

## 2026-09-06 — Refine lightbox controls

Replaced the opaque control fill with translucency and centered vector icons.
The design system previously said:

> Controls use an opaque `Surface` background so they remain readable over any screenshot.

## 2026-09-06 — Lightbox opening focus and caption

The close control receives focus without an opening highlight, and the count sits directly
below the image. The design system previously said:

> Focus moves
> to the close control when the overlay appears, with `NavigationMethod.Tab` so the drawn ring is
> visible; a plain `Focus()` sets focus without marking it visible, which is an overlay taking
> focus without showing where it went.

> The overlay keeps 24px of outer space and an 8px gap before the position caption.

## 2026-09-06 — Acquisition CSV closes TASK-38

Settings → Library now exports the stored acquisition facts, including price, without adding
price to the game modal. The full JSON/import milestone stays deferred. Superseded text:

> layout. LIBRARY answers what is in the library and holds three cards: **EXPLICIT CONTENT**,
> **HIDDEN GAMES**, **ADDED BY HAND**.

> | M6 | Export (JSON + CSV) | JSON is complete and re-readable; CSV covers a defined set of views | deferred 2026-08-31; exit criterion to be restated |

> Merge *execution* (the queue records intent; nothing applies it), JSON/CSV export, install
> management, and full-screen gamepad navigation.

## Storefront links completed — 2026-09-06

Superseded design-system.md §10.3 sentences:
- `com.epicgames.launcher://store/product/<slug>` does work — the launcher rewrites it to its embedded store — but Winnow holds no product slug, so the link is not built.
- GOG's store page and patch notes are both reachable through an anonymous API request Winnow does not yet make, so they are absent rather than impossible.
- The per-store matrix previously listed only Show in GOG Galaxy for GOG links and nothing for Epic links.

Superseded spike statements:
- ### Store page — verified-by-execution that the route exists; the slug is missing
- One landed; three remain as follow-on work.
- **Epic slug, from the 68KB productmapping** — one request for the whole library, trivially cacheable. Unlocks the store page for about 84% of Epic titles.
- **GOG slug, from `api.gog.com`**, or opportunistically free from the Galaxy database when it happens to have cached it.
- `store.epicgames.com` returns 403 to every request from this machine, bogus paths and real ones alike, so the final web URL `https://store.epicgames.com/p/{slug}` is **needs-execution-by-the-user**: open `https://store.epicgames.com/p/soma` in a browser and confirm it lands on SOMA. The slug is measured; the URL template built from it is not.
- **GOG changelog** — the same call as the slug, so patch notes cost nothing extra once the slug is being fetched.
- verified-by-execution (API), not built
- none shipped; slug from `api.gog.com` would unlock it
- verified-by-execution (route exists); slug not stored; slug from productmapping would unlock it
- none shipped; changelog from `api.gog.com` would unlock it
- What Winnow lacks is not a route but the **slug**, which it does not store.

## 2026-09-06 — Keep card facts out of the action hit targets

At 108x162, the back's unbounded metadata could draw its year and store chips over the
Details button. Those pixels hit metadata and flipped the card, although a button was
visible underneath. Facts now scroll in the space above pinned actions. Repeated presses
on a button or scrollbar belong to that control rather than the cover's double-click gesture.
The old MainWindow pointer-handler comment said:

> Registered on the tunnel route so the double click is caught before the
> back face's buttons.

## 2026-09-06 — Keep library covers face-up

The library grid no longer turns a cover over to expose a second information surface. Compact
Play/Install and Details icons appear over the stable cover on hover or keyboard focus, and a
double click outside those controls still opens Details. This removes transient layout and hit
targets from the grid without removing either direct action.

The old hover-overlay specification said:

> Bottom-aligned gradient scrim to `Ground` at 92%. Title in Body L, playtime and idle time in
> Data S. A single primary action, `Play`, in `Volt`.
>
> The resting mark uses initials because the density slider's floor is 108px and a row of
> word-chips is wider than the tile there; the words are reachable on hover, on the back face,
> in the modal and in the automation name, which satisfies §8's decorative-redundant rule.
>
> The back's actions stay pinned below its facts. At the 108px density floor, a wrapped title
> and multiple store chips can exceed the space above Play/Install, Add to list and Details. The
> facts therefore scroll inside that remaining space, with the inner scrollbar gutter; they
> cannot draw over the buttons or intercept their clicks. Buttons and the facts scrollbar own
> repeated presses. The cover's double-click gesture applies only outside those controls.
>
> The flip keeps the back's hit targets still. The front squashes over 80ms, then the back fades
> in over 80ms at its final size. Interactive back controls never scale during the turn; a click
> near a visible button edge must hit that button while it is appearing. Reduced motion snaps
> both faces.

The old art-backed-surface specification said:

> The tile's back face and the detail modal lay the game's own art behind their information.
> Both surfaces used to be flat `Surface`, so a game lost its identity the moment the user
> turned it over or opened it.
>
> The construction is identical on both. Opaque `Surface` at the bottom, then the game's art,
> then a veil — `ArtVeil`, the theme's own `Surface` at `ArtVeilAlpha` — then the text. The opaque
> base stops a half-decoded cover showing the window through the gap between the dormancy ramp's
> two layers; the same argument §14.4 makes for `TileGround`.
>
> The veil IS `Surface`, and that is the whole trick. Over the opaque `Surface` each surface
> already paints, a veil of `Surface` composites back to `Surface`, bit-for-bit.
>
> Which inks are held to 4.5:1. `Text`, `TextDim`, `Azure` and `Amber` — the four these two
> surfaces set text in. `Flare` is excluded: on these surfaces it is a dot and never a word
> (§5.2), and WCAG scores a non-text component against 3:1, not 4.5:1. The hovered update row is
> measured too: `SurfaceRaisedFaint` is the one veil that sits between the ink and the field
> rather than replacing it, and it moves the worst figure by at most 0.07. Every other hover and
> focus fill is opaque `SurfaceRaised`, which covers the art entirely and is already held to
> §8's floor.
>
> Both surfaces reuse existing image paths. The tile's back face binds the same
> `CoverPresenter.Floor` and `CoverPresenter.Vivid` bitmaps the front face draws, from the same
> presenter, at the same `DisplayAlpha`, with §5.1's 140ms restore and the same reduced-motion
> snap. One image path, one lease, one decode: the cover wall's memory bound is untouched.

## 2026-09-06 — Separate pointer hover from keyboard action focus

The first face-up action dock used `:pointerover` and `:focus-within` to control both opacity
and hit testing. A pointer click focuses a button, so leaving the card still satisfied
`:focus-within`; a recycled container could then carry the focused, revealed action to its new
game. The view now tracks pointer presence and Tab or directional focus separately, clears the
outgoing state on rebind and detach, and uses pointer geometry while capture is active. The
controls remain at their final size while the reveal fades.

Play/Install now occupies the bottom-right of the store-chip row. Details is a 40px top-right
folded corner, and the unread badge sits immediately below it so the two targets never overlap.

The visual specification previously said:

> 10px `Flare` dot, top-right, 8px inset, with a 2px `Ground`-coloured ring so it reads against
> any cover. Optional soft outer glow at 30% opacity.
>
> Bottom-aligned gradient scrim to `Ground` at 92%. Title in Body L, playtime and idle time in
> Data S. Two compact icon actions sit at the top-left, clear of the unread badge: the available
> primary action (`Play` for an installed game, `Install` otherwise) and `Details`. The actions
> appear on pointer hover and when keyboard focus enters the tile. Each has a tooltip, an
> accessible name and the standard visible focus treatment from §8.
>
> Hover and focus reveal actions without replacing, scaling or moving the cover, so their hit
> targets are stable throughout the 140ms overlay transition.

## Epic namespace misses — 2026-09-06

The store-actions spike previously said: the 11 misses are delisted or giveaway-only titles (Frostpunk, Palia, LOTR Return to Moria, Moonlighter, ABZU, Dauntless, Drawful 2, Torchlight, Unreal Tournament, >observer_, Hob).
The mapping is incomplete; eight of those titles still have product-home mappings. The correction replaces the unsupported classification with the measured namespace results.

## 2026-09-06 — Keep appearing card controls at their final size

Animated tests reproduced a Details miss at the 108px density floor: a click near the enabled
button's edge during its first 80ms hit a Border while the back was scaling. The front still
squashes, but the back now fades in at a stable size. Center clicks after settling did not
expose this failure.

The hover stat and single-store chip also overlapped at 108px. Stats now wrap to two lines,
and every store chip sits on the row below. The visual spec previously said:

> Bottom third of the tile, gradient scrim to `Ground` at 92%.

The overlay remains bottom-aligned and grows to fit its bounded content at dense sizes.
