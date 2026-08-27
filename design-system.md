# Hoard — Design System

**Applies to:** Avalonia 11+ desktop client, dark-only for v1
**Companion files:** `tokens.axaml` (drop-in ResourceDictionary), `mock-library.html` (visual target)

---

## 1. The thesis

This is a **game library that happens to be analytically sharp** — not an analytics tool
about games. Cover art is the primary interface. Data lives inside the art, not beside it.

Two rules follow, and everything else is downstream of them.

**The art is the chart.** Dormancy is rendered as desaturation of the cover itself. A game
played last week is full-vivid; one dormant three years is faded and cool-shifted. Scanning
the grid, bright tiles are alive and ghosted tiles are what you forgot you owned. No
sparkline, no bar, no second visual language competing with the art.

**Patched-since-played is an unread badge.** A hot pink dot in the tile corner. Games
libraries already own this metaphor — an update badge means "something changed, go look."
This turns the product thesis into something felt at a glance rather than read in a column.

The consequence: **your library has unread mail.** That sentence is the whole product, and
the grid states it without a single label.

---

## 2. Palette

The neutral family is **one dark green-teal ink, stepped six times.** It is not grey and it
is not black: it has a committed hue, so `Volt` reads as that same ink turned up to full
voltage — continuous with the room, which is right for a colour marking state the interface
always has. `Flare` is the only hue in the palette the room cannot produce, which is exactly
what an unread marker has to be. Cover art supplies all the real colour; the chrome is a
stage and stays out of the way.

| Token | Hex | Role |
|---|---|---|
| `Well` | `#050D0E` | Title bar, scrollbar track — the unlit lip, one step below Ground |
| `Ground` | `#0F1C1E` | Window background — deep green-teal ink, never black |
| `Surface` | `#16282A` | Rail, panels, list rows |
| `SurfaceRaised` | `#1D3437` | Hover, selection, popovers |
| `Line` | `#2B4A4C` | Dividers, borders, tile outlines |
| `Text` | `#F0EDE7` | Primary text — warm off-white |
| `TextDim` | `#8FA5A0` | Labels, metadata — sage, the room's own colour |
| `Flare` | `#FF4D93` | **Patched since you played** — the unread signal |
| `Volt` | `#4DE8C2` | **Active / recent / selected** |
| `Amber` | `#FFB63D` | Attention: high playtime, "played out", warnings |
| `Azure` | `#57A8F0` | Informational, links, secondary counts |
| `Danger` | `#E04B45` | Destructive affordance. Today: the window close button, nothing else |

### Discipline

`Flare` is the rarest colour in the interface and appears **only** on unread-update markers
and the bucket that counts them. The instant it becomes a generic accent, the badge stops
meaning anything and the product loses its point.

`Volt` carries selection and recency. `Amber` carries "you've been here a lot." `Azure` is
the neutral one and does the boring work. `Danger` is the close button's hover fill; it sits
at hue 2° against `Flare`'s 336° so that a red the size of a caption button can never be
mistaken for a 10px unread dot.

**Hierarchy is carried by temperature as well as lightness.** `Text` is warm off-white and
`TextDim` is a cool sage: primary text reads as paper laid on the room, metadata as part of
the room. Do not "fix" this by neutralising either one.

Never tint cover art with brand colour. The art is content; the interface stays out of it
except through the saturation ramp in §5.

> **Palette revised — the violet family is gone.** `Ground #16112A`, `Surface #1F1838`,
> `SurfaceRaised #2C2350`, `Line #3D3168` and `TextDim #9B90C4` were a deep indigo-violet
> stage, chosen as "arcade-adjacent" before the interface had been seen against 600 real
> Steam capsules. Three things were wrong with it. Violet sits between teal and hot pink on
> the wheel, so the chrome read as a *third accent* rather than as ground, and both signal
> colours lost force against it — `Flare` in particular looked like a brand colour rather
> than an alarm. It also fought the art: Steam capsules are mostly warm and dark, and a
> violet field pushes them green by simultaneous contrast, which is the one thing §5.1's
> cool-shift floor is trying to say on its own. And it had become the default dark-app
> purple — the thing every generated interface reaches for — which for a library about your
> own hoard is exactly the wrong register. **The replacement keeps a hued neutral rather
> than retreating to grey**: grey would have made `Volt` a decoration sitting on top of the
> chrome instead of the chrome's own colour intensified. `Volt` is unchanged; `Flare` moved
> 6° hotter (`#FF5C8A` → `#FF4D93`) and to full saturation, because against a green-teal
> ground the old rose read as salmon; `Azure` moved 8° toward cyan (`#5B9DFF` → `#57A8F0`)
> so it belongs to this palette rather than the previous one.

---

## 3. Typography

Three roles, three families. All SIL OFL — bundle them, don't rely on system fonts.

| Role | Face | Usage |
|---|---|---|
| Display | **Bricolage Grotesque** 700 | Bucket names, screen titles, tile titles in list view |
| Body / UI | **Plus Jakarta Sans** 400–600 | Labels, buttons, prose, tooltips |
| Data | **IBM Plex Mono** 400–500 | Playtime, dates, counts, durations, fractions |

**Bricolage Grotesque is the voice.** Slightly irregular, optically quirky, sharp — it reads
like game packaging rather than a dashboard, which is precisely the correction this design
needed. It has `wdth` and `opsz` axes; use `wdth` 100–110 for headers, never above 120.

**Every number is Plex Mono with tabular figures.** Non-negotiable in list view, where a
playtime column that doesn't align vertically is unreadable at scan speed
(`FontFeatures="tnum"`).

### Scale

```
Display L    22px / 26  Bricolage 700, wdth 105
Display S    12px / 15  Bricolage 700, wdth 110, +0.06em, uppercase
Body L       15px / 22  Jakarta 500
Body         13px / 18  Jakarta 400
Label        11px / 14  Jakarta 600, +0.04em, uppercase
Data         12px / 16  Plex Mono 400, tnum
Data S       10px / 12  Plex Mono 400, tnum
```

---

## 4. Layout

**Grid is the default view. List is a toggle**, remembered per-session.

```
┌──────────────┬────────────────────────────────────────────────────────┐
│  RAIL 220px  │  search           ▦ ▤   density ──○──   sort ▾         │
│              ├────────────────────────────────────────────────────────┤
│  1,247       │   ┌────┐  ┌────┐  ┌────┐  ┌────┐  ┌────┐  ┌────┐       │
│              │   │    │● │    │  │▓▓▓▓│  │    │● │▓▓▓▓│  │    │       │
│  ● Patched94 │   │    │  │    │  │▓▓▓▓│  │    │  │▓▓▓▓│  │    │       │
│    Never 412 │   └────┘  └────┘  └────┘  └────┘  └────┘  └────┘       │
│    Bounced186│    vivid   vivid   faded   vivid   faded   vivid       │
│    Played 391│                                                        │
│    Won't run │   ┌────┐  ┌────┐  ┌────┐  ┌────┐  ┌────┐  ┌────┐       │
│              │   │▓▓▓▓│  │    │  │▓▓▓▓│  │    │● │▓▓▓▓│  │▓▓▓▓│       │
│  ── LISTS ── │   └────┘  └────┘  └────┘  └────┘  └────┘  └────┘       │
│  Co-op night │                                                        │
└──────────────┴────────────────────────────────────────────────────────┘
              ● = unread patch badge      ▓ = desaturated (dormant)
```

4px base unit. Spacing: `4 · 8 · 12 · 16 · 24 · 32 · 48`.

**Tile geometry.** 2:3 portrait, matching Steam's `library_600x900` capsule and IGDB covers.
Default 148×222, gutter 16px. Density slider spans 108×162 → 200×300; the grid reflows on
available width, it does not use fixed column counts.

**Radius:** 6px on tiles, 4px on controls. Softer than the previous system — tiles are
objects you'd pick up, not records in a table.

**Elevation.** Tiles get a real drop shadow on hover (`0 8px 24px rgba(0,0,0,.5)`) plus a
2px lift. This is the one place shadow is permitted; everywhere else, elevation is the
`Surface → SurfaceRaised` step.

---

## 5. Signature: the living grid

### 5.1 Dormancy ramp

Map months-since-last-played to a saturation/brightness pair:

| Idle | Saturation | Brightness | Reads as |
|---|---|---|---|
| < 1 month | 1.00 | 1.00 | Vivid, current |
| 6 months | 0.72 | 0.91 | Slightly cooled |
| 1 year | 0.50 | 0.83 | Visibly faded |
| 2 years | 0.34 | 0.74 | Ghosted |
| 3+ years / never | 0.22 | 0.68 | Nearly monochrome |

Clamp at `0.22 / 0.68` — never fully grey. A cover you can't identify is a cover you can't
choose, and the point is to make forgotten games *findable*, not invisible.

A **−6° hue rotation** is part of the floor, composed as
`saturate() → hue-rotate(-6deg) → brightness()`. It is what makes dormant art read as *cool*
rather than merely grey (§1). Small, but load-bearing: Steam capsules are mostly warm and
dark, and without it the floor lands on a neutral-warm mud that looks like a rendering fault
instead of an encoding.

> **Brightness floor revised.** This was `0.60` until the ramp was first seen on real cover
> art. `0.60` had been calibrated against procedural placeholder gradients; against real
> capsules — which are themselves dark — it compounds, and in a library whose default sort
> opens on its most dormant titles the ramp's dynamic range was spent before the first
> scroll. Per-tile legibility was never the problem, so the fix is at the floor, not the
> curve. **Saturation, not brightness, is what carries the dormancy signal.**

**Hover restores full saturation over 140ms.** The game wakes up under the cursor. This is
the single most important interaction in the app: it makes the dormancy encoding legible by
showing you the before and after, and it feels good.

### 5.2 Unread badge

10px `Flare` dot, top-right, 8px inset, with a 2px `Ground`-coloured ring so it reads
against any cover. Optional soft outer glow at 30% opacity.

Present only when a major update landed after the user's last session (both signals from
§4.5 of the design doc — build push *and* announcement). Never on never-opened games; an
unplayed game has nothing to be behind on.

**"Never-opened" here means zero recorded playtime, not the `Never played` bucket.** Since
that bucket became everything under the refund line (design doc §6.1), the two are no longer
the same set: it holds games with up to two hours of real play, and those can absolutely
have missed a patch — they are the pile this feature exists for. The badge is `Patched
since` bucket membership and the bucket query enforces the distinction by testing staleness
*above* the refund line and below only the genuinely never-opened row. The update poller's
eligibility filter draws the same line, for the same reason, and must keep drawing it on
playtime rather than on a bucket name.

Clicking the badge opens the patch notes for the updates you missed. This is the feature
that closes the loop: notice → context → launch.

### 5.3 Hover overlay

Bottom third of the tile, gradient scrim to `Ground` at 92%. Title in Body L, playtime and
idle time in Data S. Store badge bottom-left. A single primary action, `Play`, in `Volt`.

Do not show more than four facts. The tile is a decision surface, not a detail view.

### 5.4 Implementation note — this is the hard part

Avalonia has no CSS `filter`. Two viable approaches, in preference order:

1. **Shader effect.** Avalonia 11's effect pipeline over a saturation matrix. Cheapest at
   scale, animates smoothly for hover. **[VERIFY]** current API surface and platform
   coverage before committing.
2. **Pre-computed bitmap variants.** Generate 3–4 desaturation steps per cover at cache
   time, store alongside the art, swap on state change. Bulletproof and portable; costs disk
   and gives you stepped rather than continuous fade. Hover restore becomes a cross-fade
   between two bitmaps.

Fall back to (2) if (1) is unavailable on any target platform. Do not attempt per-frame
pixel manipulation on the UI thread.

Covers must be virtualized and decoded off-thread at display resolution, not full size. A
1,200-tile grid of 600×900 source bitmaps decoded eagerly will exhaust memory. The panel is
`Views/CoverWall.cs`, not `ItemsRepeater`: `UniformGridLayout` charges every item in a row
for a trailing gutter when it computes items-per-line for the scroll anchor but packs rows
greedily when it places them, so §4's flush-row geometry made the two disagree by one column
at every window width. Its remarks carry the measurements.

---

## 6. Components

**Rail bucket.** Display S name, Data count. Selected: `SurfaceRaised` fill, 2px `Volt` left
edge. The `Patched since` bucket is the only one carrying a `Flare` dot next to its count.
Zero-count buckets render at 40% opacity rather than hiding, so the rail never reflows.

**List view.** Same data, no art dependency: title, store, playtime, idle, unread dot.
44px rows, `Surface` ground, `Line` rules, `Volt` selection edge. This is the power-user
view — sortable columns, multi-select, bulk list assignment. Everything the grid can't do
densely lives here, which is how the analytics capability stays available without dominating
the default experience.

**Merge confirm queue.** Two covers side by side at 200×300, signal diff between them
(title distance, year delta, publisher). Actions are `Same game` / `Different games` — never
"Merge"/"Cancel", which asks the user to reason about the data model instead of about games.

**Session journal prompt.** 400×220 frameless, bottom-right, `SurfaceRaised`, with the
game's cover at 60×90 on the left. Title, duration in Data, one text field, 5-dot rating in
`Volt`. Appears at most once per session, never steals focus.

---

## 7. Copy

Plain and specific. The app knows something faintly embarrassing about the user — they own
1,247 games and have opened 412 of them zero times — and must never be smug about it.

| Context | Write | Don't write |
|---|---|---|
| Bucket: updates missed | `Patched since` | `Needs attention` |
| Bucket: under the refund line | `Never played` | `Pile of shame` |
| Bucket: refund line to retired | `Bounced off` | `Barely played` |
| Bucket: high playtime | `Played out` | `Completed` |
| Bucket: unrunnable | `Won't run` | `Dead` |
| Badge tooltip | `3 updates since you played` | `New content available!` |
| Journal prompt | `How was that?` | `Rate your session!` |
| Merge action | `Same game` | `Merge records` |

**Empty states are directions, not moods.**

- Patched since, empty: *"Nothing's been patched since you last played. This fills up on its own."*
- Never played, empty: *"You've played everything you own. Genuinely rare."*
- First run, mid-scan: *"Reading your Steam library. Covers and metadata fill in over the next few minutes — you can browse now."*

The last one matters: `appdetails` backfill takes hours (§4.3 of the design doc), so the
interface promises a browsable library immediately and art later. Render placeholder tiles
with the title set in Bricolage on a `Surface` field — never a spinner, never an empty grid.

---

## 8. Accessibility floor

- **The saturation ramp is decorative-redundant.** Idle time also appears as text on hover
  and as a sortable column in list view. A user who can't perceive the fade loses nothing.
  The unread badge is likewise backed by the rail count and a tooltip.
- Visible keyboard focus everywhere: 2px `Volt` outline, 2px offset.
- Full keyboard grid navigation (arrows, `/` to search, `Enter` to launch).
- Reduced motion disables the hover saturation animation — state snaps instead of fading.
- `TextDim` on `Surface` measures **5.88:1**, and on `SurfaceRaised` — which is what a
  selected list row puts under the store and idle columns — **5.04:1**. Do not dim further.
  (The old violet pair measured 4.9:1 on `Surface` and 4.30:1 on `SurfaceRaised`, so the
  hover state was quietly below the floor; the new family clears it in both.) `Text` on
  `Surface` is 13.1:1, `Azure` 6.03:1, and `Volt` on `Ground` 11.3:1.
- Provide a settings toggle to disable the dormancy ramp entirely for users who prefer
  uniform art. The badges and buckets carry the signal without it.
- The caption buttons are real buttons, so they keep the same 2px `Volt` focus ring and are
  reachable by Tab like anything else. `Danger` is never the only thing distinguishing
  close: it has its own glyph and its own tooltip.

---

## 9. Window chrome

The app draws its own title bar. Avalonia's `ExtendClientAreaToDecorationsHint` with
`ExtendClientAreaChromeHints="NoChrome"` puts the client area over the decorations; the
caption and its three buttons are ordinary controls in the window's own tree.

**`Well` is one step *darker* than `Ground`, not lighter.** Every desktop platform puts a
lighter caption strip above a darker body, which means the brightest band in the window sits
directly above the art. Inverting it makes the first inch of the window an unlit lip and the
cover wall the first thing on screen with any light in it — which is §1's thesis applied to
the one surface the OS used to own. The same tone backs the scrollbar track and the detail
modal's scrim.

The mark at the left is two 2:3 capsules, one behind the other: the app's own atom, and what
a hoard of them looks like. Nothing else lives in the caption — no menu, no search, no
status. It is a lip, not a toolbar.

**Behaviour the system used to provide is now ours, and all of it is load-bearing.** Drag
uses `BeginMoveDrag`, which hands the press to Windows' own move loop — that is what buys
Aero Snap, the edge previews and Win+Arrow rather than a hand-rolled imitation. The cost is
that the loop is modal and owns the pointer until release, so the second press of a double
click may or may not carry an intact click count; the title bar therefore tests both the
framework's count and its own press clock (500ms, 8px, in *screen* coordinates so that a
click after a drag is not read as a double). The gesture is deliberately not also wired to
`DoubleTapped`: Avalonia raises that from the tunnelling half of the same press, and a second
handler would toggle the window twice and land it back where it started.

`OffScreenMargin` is applied to the window's root panel. Windows sizes a maximised window
past the work area by the resize border, and with the client area extended that overhang is
ours to absorb — without it the caption and the first column of tiles are clipped the moment
the window is maximised. The middle button says what it will do, not what state the window is
in: maximised, it draws the two-square restore glyph and offers "Restore down".

**Scrollbars** keep Fluent's `ScrollBar` theme — its rest/expanded behaviour is good — and
only repaint it, by overriding the resource keys its template reads. `Application.Resources`
outranks `Application.Styles` in Avalonia's lookup, so the token file wins over the theme
without forking a template that would then have to be maintained. Two changes beyond colour:
the stepper arrows are hidden, and the resting thumb is widened from Fluent's 2px hairline to
4px, because a scroll position you have to hunt for is not a scroll position on a 606-tile
wall. **The thumb is neutral, never `Volt`** — a scrollbar is chrome, and spending the
selection colour on it would make every scroll position look like a selection.

### 9.1 The resize inset — nothing interactive lives at the window edge

Extending the client area over the decorations does not hand back the pixels the resize
border sits on. Windows still answers `WM_NCHITTEST` for the outer band with
`HTRIGHT` / `HTBOTTOM` / `HTBOTTOMRIGHT`, **before the client area sees the pointer at all** —
so a control flush to the edge is drawn by us and hit-tested by the OS. Measured on this
window: the band is exactly **8px** on the right and 8px on the bottom.

A 12px scrollbar flush to that edge therefore left 2–4 usable pixels, and the resting 4px
thumb sat entirely inside the band. The scroll position was visible and unreachable.

**`ScrollBarEdgeInset` (`0,0,10,10`) is the rule that follows: no interactive control may sit
inside the 8px the OS owns, and anything that would have is stepped 10px in.** Every vertical
scrollbar at the window edge carries it as a margin; interior ones — the rail's, the detail
modal's — opt out with `ScrollViewer.inner`, because their edge is a divider of ours rather
than the window's.

**The filter panel changed sides of this rule when it moved right (§11.1).** Beside the rail it
was interior and opted out; against the window's right edge it is not, and its resting thumb
would have sat entirely inside the band. It now takes the inset like any other edge scrollbar,
and its content margin widened to clear the swelled track. The rule is about which edge a
control is on, never about which control it is.

Two alternatives were weighed and rejected. **Widening the thumb** changes nothing: the border
wins above Avalonia's hit testing entirely, so a wider control is a wider unreachable control.
**Hooking `WM_NCHITTEST` to answer `HTCLIENT` along the scrollbar's height** would buy the
scrollbar back by taking away edge-resize down the whole right side of the window — a worse
trade than ten pixels of ground. The inset costs nothing anyone can see; it leaves the resting
hairline exactly as subtle as it was, and it lets Fluent's own swell-to-12-on-hover finally
fire, because the pointer can now reach the track that triggers it.

---

## 10. The detail view

§5.3 caps the tile's hover overlay at four facts — "the tile is a decision surface, not a
detail view." This is the detail view that cap presupposes. It stays a **modal over the
library**, opened by `Enter` or a double click, dismissed by `Escape` or a click on the scrim.
That is not inertia: the library is a scanning surface, and the panel is a decision the user
made about one tile in the middle of a scan. A modal keeps the wall's scroll position, so
`Escape` returns them to exactly the row they were reading. A page would turn "go back" into
navigation and lose their place.

### 10.1 What it answers, in the order people ask

```
┌─ 200px ────┬──────────────────────────────────────────────────┐
│            │  Empyrion: Galactic Survival                 [×] │  1 WHAT IS THIS
│  cover     │  2020 · Eleon Game Studios                       │
│  200×300   │  [STEAM] [Patched since] [Not installed]         │
│            │                                                  │
│            │  37h    SINCE YOU PLAYED               9y 7mo    │  2 MY HISTORY
│            │  PLAYED ├────────────────────────────────●●┤     │    the gap rail
│            │         2 Jan 2017                     today     │
│            │         2 updates landed while you were away.    │
│            │         Checked once, on 23 Aug 2026.            │
│            │                                                  │
│            │  [ Install ] [ Store page ] [ All patch notes ]  │  3 GET ME IN
│ STEAM APPID├──────────────────────────────────────────────────┤
│ 383120     │  ABOUT                                (scrolls)  │  4 THE REST
│            │  Empyrion – Galactic Survival is a 3D open…      │
│ ON DISK    │                                                  │
│ C:\…       │  SINCE YOU PLAYED                                │
│            │  ● v1.19.2 Patch        11 Aug 2026  Patch notes │
└────────────┴──────────────────────────────────────────────────┘
```

**Two columns, split by what they are about rather than by where the art fit.** Left is the
object: its art, the id Steam calls it, where it lives on disk. Right is your relationship
with it. The divider spans the right column only, because the left one keeps going.

That split is also a bug fix. With everything in one right-hand stack, a game with no
last-played date left ~130px of nothing beside a 300px cover, and the panel read as broken
rather than as sparse. Two short reference facts under the art fill exactly that, and they
belong there on the merits.

### 10.2 Signature: the gap rail

**The one thing Hoard can draw that nothing else can.** Storefronts hold your last-played
date and they hold a game's patch history; nobody puts them on the same axis. The rail runs
from your last session to now, with the updates that landed in between marked on it.

- **The rule is §5.1's dormancy ramp turned on its side** — `Volt` at the last-played end
  fading to `Line` at today, half faded at the half-way point. The user has been looking at
  desaturating capsules for weeks; this is the one screen with room to say why.
- **Marks are `Flare`**, legal here and only here in the panel, because they are literally
  §5.2's unread signal — an update after the last session — plotted in time instead of stacked
  in a tile corner. Capped at 14; past that a rail is a smear, and the list below stays the
  exhaustive record.
- **The rail is normalised, never scaled to duration.** A 14-day gap and a 9-year gap draw the
  same length, with the span stated as a number beside it. Scaling would make most rails
  invisible and would be a second, competing encoding of a fact the digits already carry.
- **Everything it draws is restated in words underneath** (§8: the encoding is
  decorative-redundant). A user who cannot resolve a 7px dot loses nothing.
- **No last-played date, no rail.** Two different absences, kept apart by the copy: *"You've
  never opened this."* and *"Steam has no date for your last session."*

**It is deliberately not a playtime chart.** §1 names longitudinal playtime as the thing
storefronts discard, and the obvious move is a line through `playtime_snapshots`. On a real
library that table holds **one reading per game** — measured, 611 of 616 — and a line through
one point is a decoration pretending to be evidence. What the snapshots honestly support is a
sentence: *"Checked 12 times since 23 Aug 2026 — up 1h 7m."* The delta is between the first
and last reading Hoard holds, which is the part it actually watched happen, not the total
Steam already knew. At one reading it says so; at zero it says nothing at all.

### 10.3 Getting in

`steam://run/<appid>` when the game is on disk, `steam://install/<appid>` when it is not — and
the button is **named for which one it is**, `Play` or `Install`. A button reading "Play" on an
uninstalled 60GB game promises something the next hour will not deliver. No appid means no
primary action at all, never an inert button.

Beside it, `Store page` and `All patch notes` in `Azure`, and `Open folder` when there is a
path. The folder goes through the launcher's directory entry point as a path — never a `file:`
URI, which the link model refuses on purpose.

**Every outbound target is built by `GameLink.Create` and nothing else.** Three schemes are
allowed — `https`, `http`, `steam` — and everything else is refused, including the ones that
look harmless: `file:`, `javascript:`, `data:`, anything relative, anything carrying a control
character. `update_events.url` is captured from a network response, so it is untrusted input.
**A target that fails validation is a null link, and a null link renders no button** — never a
dead one, and never a URL the data did not supply.

### 10.4 Copy

| Context | Write | Don't write |
|---|---|---|
| Rail, updates missed | `2 updates landed while you were away.` | `2 new updates!` |
| Rail, none recorded | `No updates recorded in that stretch.` | `Nothing has shipped` |
| Longitudinal record | `Checked 12 times since 23 Aug 2026 — up 1h 7m.` | `12 snapshots` |
| Record, one reading | `Checked once, on 23 Aug 2026.` | `Insufficient data` |
| No last-played date | `Steam has no date for your last session.` | `Unknown` |
| Never opened | `You've never opened this.` | `Never played` |
| Provisional title | `Steam's local files gave an id and no name.` | *(nothing)* |
| No summary yet | `No description yet. Hoard fills the year, publisher and summary in from IGDB as it works through your library.` | `No data` |

Two of those are load-bearing. **"No updates recorded in that stretch"** and not "nothing has
shipped": update polling is staggered across days (§4.5 of the design doc), so an empty rail
can mean a quiet decade or a turn that has not come round yet, and the interface may only claim
the one it can support. **"Checked"** and not "sampled" or "snapshotted": name the thing by
what the person recognises, not by the table it lives in.

### 10.5 What is absent, and why

`acquired_at`, `license_type`, `price_paid_cents`, `platform`, `edition_note` are all in the
schema and all **empty for every row** this data source produces — Steam's local files carry
none of them. They are absent from the markup entirely rather than bound and hidden: a row
that can never appear is dead weight impersonating a feature. `account_ref` is populated and
still absent, because showing a user their own Steam account id is noise.

**Achievements are not here.** No data exists yet, and §6.2's rule stands regardless: never a
blended cross-platform completion figure. When they land they are per-release rows, not an
average.

### 10.6 Text is selectable

Titles, summaries, install paths and appids are `SelectableTextBlock`, not `TextBlock`. Three
consequences worth recording:

- **`tokens.axaml`'s text styles select on `:is(TextBlock)`, not `TextBlock`.** An Avalonia
  type selector matches the exact type, so a bare `TextBlock.body` silently skips
  `SelectableTextBlock`. The failure is not a build error — it is unstyled text in a system
  font.
- **Selection needs no focus; `Ctrl+C` does.** `SelectableTextBlock` arrives focusable from its
  own control theme, so every selectable line is a Tab stop unless told otherwise. The four
  worth stopping on keep it; everything else sets `Focusable="False"`. With all of them
  focusable it took five Tab presses to reach `Play`, past a publisher name nobody tabs to a
  modal to copy.
- **Focused text gets a raised field, not a ring.** A 2px outline around a five-line paragraph
  is a box, not an indicator.

### 10.7 Focus is drawn, not adorned

§8's global `FocusAdorner` did not deliver a visible ring in this panel — measured on the
running window, a few stray pixels at the corners of one button and nothing on the rest. This
panel draws its own, the same way MainWindow's display-preference checkbox does for the related
popup-adorner reason.

**The ring is a brush swap on a border whose thickness never changes.** Thickening a border on
focus reflows the row it sits in, and buttons that shuffle sideways as you tab through them are
worse than no ring. It is set on `PART_ContentPresenter` rather than on the Button, because
Fluent's own state styles write that presenter directly and a `TemplateBinding` loses to them
on hover.

The launch button is the one place the ring is not `Volt`, because on a `Volt` fill it cannot
be: it is `VoltInk`, the button's own text colour, which reads as the control being armed
rather than as a new colour arriving.

**No flyout anywhere in this panel, deliberately** — an adorner needs an adorner layer and a
popup is its own root, so any menu here would need its ring hand-drawn. Three links do not need
hiding behind one.

**Tab order follows the tree, not `TabIndex`.** Avalonia's tab navigation walks declaration
order and ignores `TabIndex` on a non-focusable container — measured, not assumed. The right
column is therefore declared first and placed second by `Grid.Column`, so the keyboard reaches
`Play` before it reaches an appid.

---

## 11. The filter panel

Steam's library filter is the reference and not the template. Its shape is six
columns of unlabelled checkboxes plus two free-text fields, and most of what it
asks about — friends, languages, Deck compatibility — is data Hoard does not
have. Copying the shape and greying half of it out would have been worse than
designing for what this product actually knows.

**Two things the reference gets right, kept.** A count beside every option, so a
filter that leads nowhere says so before it is clicked. And one surface you scan
rather than a menu you drill into.

### 11.1 The panel is the right-hand column

`Filters` opens a **276px column to the right of the grid**, on the rail's own
`Surface`. It is not a drawer over the art and not a popover.

**It is on the right because its controls are.** `Filters` and `Clear filters`
both sit in the command bar's right cluster, and while the panel was on the far
left every control was a full window away from the surface it operated. On the
right the toggle sits directly above the column it opens, `Clear filters` and
the `926 → 136` line land against its edge, and the eye that follows a cut ends
up beside the counts that made it.

**Its left edge is the window's other chrome boundary.** Beside the rail this
edge was an internal divider inside one continuous filtering surface. Here it is
the seam between the art and the chrome — the same seam the rail's right edge
is, mirrored — so it takes the same treatment: 1px `Line`, `Surface` behind it,
nothing softer. The window reads as one lit field of covers with a chrome column
on each side, and **the panel is a peer of the rail rather than a second column
of it.**

**Its header is 48px, the command bar's height**, so the rule under `FILTERS`
continues the rule under the command bar straight across the window. On the left
there was nothing to line up with; here there is, and not taking it would leave
a 6px step in a line that crosses the whole screen.

**The rail is still not duplicated, and it is still part of the filter.** The
rail owns the bucket axis; the panel owns every other one; neither offers the
other's. Two controls writing one axis is how a panel starts disagreeing with the
screen behind it. What changed is *where that claim is made*: adjacency used to
make it, and the two no longer touch, so **the cut bar (§11.3) now carries it
alone** — the bucket is a chip there beside the panel's own, and drops like any
other rule. That was always the stronger statement of it; it is now the only one.

**Its right edge is the window's, so §9.1 applies to it and did not before.** Its
scrollbar is no longer an interior one: flush to that edge it would sit inside
the 8px band Windows hit-tests as `HTRIGHT` before the client area sees the
pointer — exactly the visible-and-unreachable scroll position §9.1 exists to
prevent. It takes `ScrollBarEdgeInset` like every other scrollbar at the window
edge, and the column went 264 → 276 to pay for the gutter that buys, so the
option rows keep the 234px they were drawn at.

**Tab order follows the window in reading order** — rail, command bar, grid,
panel — which means the panel is last in the file as well as last on screen. A
`Grid.Column` says where a control sits; its position in the markup says when it
is reached, and §8's keyboard walk reads the second one.

The grid narrows rather than being covered. That costs a column of tiles and buys
a panel you can leave open while you scan — which is the only way the counts pay
for themselves, because their whole value is watching them move.

**Nothing here is a popup**, so §8's focus ring works normally. The facet
checkbox still draws its own ring in its control template, for the reason §10.7
records: one focus treatment across the app beats two.

### 11.2 Counts are residual, and each group lifts its own

The number beside an option is **what you would get if you ticked it** — computed
with every *other* group's selections applied, this group's own selections
lifted, and the rail's bucket, any open list and the search box all in force.

Lifting the group's own selections is the part that is easy to get wrong and
fatal when it is. Options inside a group are an OR, so ticking one genre must not
drop every other genre to zero — a panel that does that is a dead end after a
single click.

An option whose residual count is **0 renders its zero and stops being a click
target and a tab stop**, at the 40% opacity §6 already gives a zero-count bucket.
An option that is *ticked* stays live whatever its count says: the way out of an
empty result has to be the control that caused it.

**Order freezes on the first counts.** A long group leads with its commonest
options and then holds that order for the session. Re-sorting on every recount is
the obvious reading of "commonest first" and it is wrong — every tick anywhere on
the panel moves every count, so the rows would rearrange under the pointer
between one click and the next.

Counts are taken **per tile, not per release**: a release owned on two stores is
two rows in the library and must be two in the count, which is why the panel
tallies its own sets rather than calling `FacetSnapshot.CountsFor`, whose
`Distinct()` collapses exactly that pair.

### 11.3 The cut bar

One strip under the command bar, present only when the grid has stopped showing
the whole library:

```
[ LIVE LIST Co-op, controller-ready × ] [ Shooter × ] [ Horror × ]
                                     926 → 2   Update list   Revert   Clear filters
```

**`926 → 136` is the signature of this screen.** It is the only arrow in the
interface, because this is the only place a number becomes another number. Plex
Mono, tabular; the total in `TextDim`, the result in `Volt`.

The bar exists because *a library that has been cut down and does not say so is
the most expensive confusion this screen can produce* — the panel can be closed
and the rail scrolled past, and then 136 of 926 games look like the whole hoard.
Each chip carries its own dismissal, so undoing one rule never means hunting for
the control that set it.

**Chips are `Volt`-edged, never `Flare`.** A chip is a selection, which is what
Volt is for. There is deliberately **no "has updates" group** anywhere in the
panel: that set is exactly the rail's `Patched since` bucket, and a second door
onto it would need a second marker — and the only marker for unread is Flare.

**Every chip says who set it, and the grammar is the palette's own: `Volt` means
you chose this.** A rule an open live list contributed was not chosen by the user
at all (§12.2), so it drops the Volt edge and takes the neutral `Line` one every
other piece of chrome wears, with its label at `TextDim` — 5.04:1 on
`SurfaceRaised`, which clears §8's floor. There are therefore three families and
only two edges:

| On the bar | Edge | Means |
|---|---|---|
| The open list, leading | `Line`, kind label shown (`LIVE LIST`) | The place you are in. Its × leaves |
| A rule the list brought | `Line`, `TextDim` label | The list set this, not you |
| A rule you set | `Volt`, `Text` label | You set this — inside a list it is an unsaved edit |

The distinction is never carried by the edge alone: each chip's tooltip says it
in words (§8), which is the same rule the dormancy ramp lives under.

**The open list leads the bar, ahead of the bucket.** It is not a rule but the
place the rules belong to — and "which live list am I in" is the question the
strip previously could not answer, because a live list's name appeared nowhere
except a rail row that could be scrolled past. The kind label is on the chip
rather than only in a tooltip for the same reason §12.1 puts it in a heading
rather than a dot: a word survives being read badly.

The bar carries at most four actions at once, and membership actions and list
metadata are mutually exclusive: with rows selected you are editing what is *in*
the list, with nothing selected you are editing the list itself.

### 11.4 What is drawn, and the rule that decides

`genre` · `theme` · `game mode` · `store tag` · `features` · `controller` ·
`store` · `on disk` · `release year`.

**Every group here is a group a live list can store.** That is the rule.
`FacetKinds` also holds player perspective, which `LibraryFilter` has no field
for — so it is not drawn, because a rule that vanishes the moment you save it is
worse than a rule you never had. `FeatureIds` and `ControllerIds` were *added* to
the filter record rather than the groups dropped, which is the same rule pointing
the other way.

Two absences are load-bearing:

- **A dimension with no data draws nothing.** Four columns of greyed checkboxes
  is the wall this panel is not. When none of the metadata-backed groups are
  present the panel says so in a sentence instead.
- **A dimension whose one option is true of every title draws nothing.**
  "STORE · Steam 926" on a Steam-only library is a fact restated as a control
  that cannot change anything. It reappears by itself the day a second store
  lands.

**Release year is two Plex Mono fields, not a slider and not a histogram.** A
year is four characters the user already knows; a range set by dragging is a
range they cannot state exactly. A drawn year distribution was considered and
cut: it would be a second visual language competing with the art two columns
away (§1), and §5 spends this app's one chart budget on the cover wall itself.
The watermarks are the real bounds of the library, so an empty field still says
what there is. A release with no year does not match a bounded range — an absent
fact is not evidence.

---

## 12. Lists and live lists

**A list is one the user fills by hand. A live list is one that holds a rule and
finds its own members.** Never "smart", never "dynamic collection" — §7 names
things by what the user controls, and what they control is whether the thing
keeps up with them. The action on the cut bar is **`Save as live list`**.

### 12.1 Two rail sections, and no second dot

```
── LISTS ─────────────
   Couch co-op night   4
   Finish these first  5
── LIVE LISTS ────────
   Co-op I bounced off 136
   Unplayed adventures 342
```

The kinds are told apart **by heading, not by a coloured mark**. A pip beside a
count was the obvious move and the wrong one: the rail already has exactly one
dot, the `Flare` pip on `Patched since`, and a dot's meaning survives precisely
as long as there is only one of them (§5.2). A heading says it in words, scales
to any number of lists, and is legible to a reader who cannot resolve a 7px mark
at all (§8).

Rows take the bucket treatment — hover fill, 2px `Volt` selection edge — with one
difference: **the name is body type, not Display S caps.** Bucket names are the
application's own vocabulary and are shouted; a list name is the user's own
sentence and is not.

Both kinds recount on every library load. A manual list drops a count when one of
its games is consolidated away or filtered out as a non-game entry; a live list's
number moving on its own *is* the feature.

`LISTS` is the heading that always exists, because it is where a first list
lands; `LIVE LISTS` appears only once there is one. Empty: *"No lists yet. Select
titles and choose Add to list, or filter the library and save the result as a
live list."*

### 12.2 A list composes, a live list restores

**A manual list is one more AND term** over the library, not a separate screen —
so the rail, the panel and the search box all still work inside it. "The ones in
Couch co-op night I haven't installed" is a question the user can ask without
leaving the list.

**A live list adds no term at all.** Opening one pours its saved rules back into
the rail and the panel, so the user is looking at the filter that defines it and
can edit it in place. That difference *is* the two kinds, made visible by the
controls rather than explained in a tooltip.

Editing an open live list turns the cut bar into `Update list` / `Revert` — both
answers by name, because neither is obviously right and neither should happen by
accident.

#### A list is a place, and leaving takes what the place contributed

Pouring the rules in is only half a bargain, and the other half was missing.
Once poured in they were indistinguishable from rules the user had set by hand,
so clicking `All games` cleared the bucket and left the live list's genre, mode
and tag terms silently applied. The user believed they were looking at their whole
library and were looking at a live list with extra filters on top — §11.3's most
expensive confusion, arriving through the one door the cut bar was not watching.

**So a list is a context, not a switch.** You are in exactly one at a time, and
selecting `All games`, a bucket, or another list *leaves* the one you were in and
takes its contribution with it. A live list contributes the rail's bucket, the
panel's groups and the search box, and all three go. A manual list contributes
only membership, so leaving it takes only that — which is why §12.2's composing
behaviour is untouched: rules the user set inside a manual list are the user's,
and they stay.

Three consequences worth stating, because each is a thing that could reasonably
have gone the other way:

- **The panel stays open on the way out.** Closing it would hide the very thing
  that proves the rules left, and the user did not open it — entering the list
  did.
- **Clicking the bucket you are on does not clear it while a live list is open.**
  That escape hatch answers "you clicked this twice", and inside a live list the
  lit bucket was clicked once, by the list. There, clicking it means "give me
  that bucket and nothing else."
- **`Update list` still works exactly as before.** The rules stay in the controls
  and stay editable; they simply do not outlive the context. Provenance is what
  makes that legible rather than magical — the cut bar's three chip families
  (§11.3) say, rule by rule, which of them the list brought and which the user
  added on top.

**The rail carries the same distinction.** The `Volt` edge means *this is where
you are*, and exactly one row ever has it. With a list open that row is the list,
so a bucket in force takes the selection fill with a `TextDim` edge instead of a
Volt one: a rule that is cutting the grid, not a second claim to be where you
are. Previously both drew Volt at once, and clicking `All games` lit a third —
three rows asserting the same thing is how someone ends up certain they are
looking at everything.

One place this model does *not* reach: the panel's own ticks. A checked box is
`Volt` whoever ticked it, because a tick means "in force" and a second tick
treatment would be a third thing to learn on the surface that can least afford
one. The bar carries provenance; the panel carries state. If that ever proves too
far apart, the fix is a marker on the option row, not a second colour of tick.

A manual list opens in **`List order`**, a sort row that exists only while one is
open, and leaving the list puts the previous order back. A hand-built list whose
stored positions are invisible has no reason to store them, and "move up" under
an alphabetical sort is a control that appears to do nothing. `Move up` and
`Move down` go dead at the ends of the list rather than staying lit and doing
nothing.

### 12.3 The action bar, and why there are no flyouts

Naming a live list, picking a list to add to, renaming one and confirming a
delete all happen in **the same strip**, replacing the cut bar while they are up.

This is not a stylistic preference. Avalonia's global `FocusAdorner` does not
render inside a popup — a popup is its own root and has no adorner layer — so
every control in a menu here would need its ring hand-drawn, which is the reason
§10.7 gives for the detail panel having no flyout either. In the window's own
tree, §8's focus ring and a linear tab order both come free, and the question
sits directly above the thing it is about.

`Enter` confirms, `Escape` cancels, and focus follows the prompt into its field.
The save prompt opens with the rules read out as a suggested name ("Bounced off ·
RPG"), because a rail full of "Live list 3" is a rail nobody reads.

**`Add to list` is one control for both views.** The grid selects one tile, the
list view selects many, and the button reads whichever is in force — naming the
number once there is more than one. The picked set is derived from the selection
in the view model rather than in the pointer handler, so arrowing across the wall
arms it exactly as clicking does (§8).

Deleting asks first, and the question says what survives: *"Delete "Couch co-op
night"? The titles stay in your library."* It is the only destructive act in the
application, and `Danger` appears on its confirm button and nowhere else on the
strip.

### 12.4 `Escape` unwinds the cut, one layer per press

Outermost first: the panel closes; then an unsaved edit to an open live list
reverts; then the filters clear — *unless* a live list is open, in which case
they belong to the list and this layer is skipped; then the open list closes,
taking its own rules with it (§12.2); then the bucket clears. One key, and no
press is ever a no-op while anything is still cutting the grid.

The two live-list layers are why the ladder is not simply "clear everything".
Clearing the panel inside a live list is not a step back out — it is a fourth,
emptier version of the list, still labelled as the list. Reverting is a step out
of an edit, and leaving is a step out of the place; both are named acts the cut
bar already offers, and `Escape` reaches them in the order they were entered.

**Every letter key yields to a focused text field.** The keyboard shortcuts used
to test `SearchBox` alone, which was correct while it was the only text box on
screen. It no longer is — the panel has a find field per long group and two year
fields — and typing "f" into "Find a tag" would otherwise close the panel being
typed into.

### 12.5 Motion, and the command bar that had to give way

Nothing here animates except the 120ms fill cross-fade the rail rows already had,
and every `Transitions` value is set **through a style, never as a local value on
an element**. A local `Transitions` outranks any style selector trying to remove
it, which would make §8's reduced-motion rule unenforceable on exactly the
controls that had been given the most care. The panel itself does not slide: a
column that animates costs the grid a reflow per frame, and it buys nothing.

The command bar's search box became a **star-sized column among Auto ones**, and
the window's default width went from 1180 to 1280. A Grid satisfies its Auto
columns before its star one, so the search box is now the only thing that gives
way when the panel takes 276px out of the row. At a fixed 360 it was the Filters
button that got pushed off the right edge — the one control that must never be
unreachable, because it is the way back.

---

## 13. Open gaps, found building the Stores panel (2026-08-26)

The Stores panel (§M4.6) is the app's first prose-heavy, state-heavy surface, and it hit six
places where this document had no answer. Recorded so the next person resolves them once
rather than re-deciding them per screen. Each names what was done in the meantime, so the
provisional choice is visible rather than quietly becoming the standard.

1. **Indeterminate progress has no rule.** §8's reduced-motion guidance names only the hover
   saturation ramp, and there is no reduced-motion setting to hang a spinner off. A spinner
   was deliberately NOT invented; sign-in shows a Volt-edged status field saying where to
   look, plus Cancel. **If an animated indicator ever ships, §8 needs the rule first.**

2. **No colour role for "optional, and deliberately not connected."** §2 assigns Volt, Amber,
   Azure and Danger, all of which mean something is right, degraded, informational or
   dangerous. A connection nobody has made is none of those, and must not read as an error.
   Used a `Line`/`TextDim` pill; it wants to be a named component in §6.

3. **§6 has no single-row rail section that opens a screen.** REVIEW/`SAME GAME?` works
   because the row states a question. SOURCES/STORES is mildly redundant and could not be
   resolved inside the existing pattern.

4. **§7's copy table has no rows for connection state or credential consent.** All of this
   panel's copy was written from the two auth spikes' posture reasoning rather than from
   here. "Not signed in" vs "Disconnected", "Session expired" vs "Error", and "there is
   nothing to sign into" vs a greyed-out button are all decisions the table should own —
   this screen is almost entirely copy.

5. **No reading-measure rule.** §3 tops out at Body 13/18 and §4 sets no maximum measure,
   because until now nothing had a paragraph in it. Used 12/18 capped at 720px. That belongs
   to the system, not to one file.

6. **§8 and §10.7 disagree about focus.** §8 states the global 2px Volt adorner as the floor;
   §10.7 records that it measurably underdelivers and prescribes a brush swap on
   `PART_ContentPresenter` at fixed thickness. §10.7 was followed here (VoltInk on the
   Volt-filled primary). **The two sections should be reconciled** — right now which one is
   authoritative depends on which you read first.

7. **RESOLVED in §14. There is no rule for translucency, and Mica needed one.** Every ink in §2 was chosen
   against a *known* ground, and §8's measurements — `TextDim` 5.88:1 on `Surface`, 5.04:1
   on `SurfaceRaised`, "do not dim further" — presuppose that ground is opaque. A Mica
   window replaces it with the user's wallpaper, which the system cannot see.

   **What was done in the meantime: translucency is confined to the caption strip, and
   nothing else in the application is translucent.** `WellMica` (`Well` at 85%) is the
   window's only non-opaque surface. The decision is not taste; it comes out of the same
   sums §8 is made of, run against a pure-**white** backdrop — the ceiling any wallpaper can
   reach, so the answer holds without assuming anything about how Windows composes Mica:

   | Surface at 85% over white | `TextDim` | Verdict |
   |---|---|---|
   | `Well` — the caption | **5.0:1** | Level with the 5.04:1 §8 already accepts |
   | `SurfaceRaised` — a selected rail row | **3.1:1** | Under the floor, badly |
   | `SurfaceRaised` at 94% | **4.2:1** | Still under AA, and by then invisible anyway |

   The rail's selected row is already sitting on §8's floor with no headroom, so **there is
   no alpha at which a reading surface is both visibly Mica and legible.** The caption
   carries a wordmark and three glyphs and no reading matter, which is why it is the one
   surface that can pay. §1 says the same thing from the other side: a cover wall floating
   on a wallpaper is a busier grid, and the covers need their ground.

   Two facts worth keeping when this becomes a rule. **§9's "unlit lip" survives** — the
   caption stays at or below `Ground`'s luminance for any backdrop up to about mid-grey, and
   measured on a real desktop it sat at 0.41×. And **§5.4's dormancy cross-fade is
   untouched**, verified by diffing the window with Mica against the same window without it:
   every differing pixel fell inside the top 36px.

   What the system still owes: a named role for "chrome that may be translucent" as opposed
   to "surface that carries reading matter", so the next window does not re-derive this;
   and a statement of whether the accents (`Volt`, `Amber`, and `Flare` above all) may ever
   sit on a translucent surface. Today none of them do, and that is an accident of where the
   line fell rather than a rule.


---

## 14. Themes, and the translucency rule (resolves §13 gap 7)

**Dark-only is still true; one-palette is not.** Four themes ship, the default is
unchanged, and a user-toggleable transparency mode sits beside them. Both settings
live on the rail's `SETTINGS › APPEARANCE` screen and persist in `settings`.

### 14.1 What a theme may change, and what it may not

**The role is the invariant; the colour is not.** §2 assigns every hue a job, and a
theme may change which colour plays a job. It may never change what a job means, and
it may never spend one job's colour on a second one.

**`Flare` is the load-bearing case.** It marks unread updates and the bucket that
counts them, in every theme, and no theme's `Volt`, `Amber`, `Azure` or `Danger` may
equal it. `ThemeContrastTests` asserts that per theme, along with a minimum hue
separation from `Danger` (24°, the gap §2 already accepts for the default pair) and
from `Volt` (60°).

**Two rules of construction carry across the table.** Every theme's `Volt` is its own
room at full voltage — §2's argument for a hued neutral rather than grey was that it
makes selection the chrome intensified rather than a decoration on top of it, and that
reasoning is not specific to teal. And every theme's `Flare` is the one hue that
room cannot produce.

**No light theme, deliberately.** §9 inverts the platform's caption order so the first
inch of the window is an unlit lip, §5.3's tile scrim fades to `Ground`, and §5.1's
dormancy floor was calibrated against dark capsules on a dark field. A light theme is
not this table with the steps reversed; it is a second pass over all three, and half
of one would break the ramp that is the product's whole encoding.

| Theme | Why it exists |
|---|---|
| **Hoard** *(default)* | The house look. Green-teal room, mint `Volt`, hot-pink `Flare`. The one tuned against six hundred real capsules. |
| **Cold storage** | For a bright room. The whole neutral family lifts about two steps and cools to blue-steel, so daylight reflections stop competing with the grid. Costs contrast between chrome and art — which is the trade someone in a sunlit room wants. |
| **Nightshift** | For a dark room. The family drops to near-black with most of its chroma drained, so the chrome gives off no light and the covers are the only lit thing. §1 taken literally. |
| **Phosphor** | The arcade register — the dark glass of a green monitor. The one theme whose chrome has a voice, and the one that pushes warm capsules *warmer* instead of cooling them. |

### 14.2 The four grounds

Which surface may admit the desktop is a **token**, not a rule somebody has to
remember. §13 gap 7 asked for "a named role for chrome that may be translucent as
opposed to surface that carries reading matter"; these are it.

| Token | What it backs | Translucent? |
|---|---|---|
| `ShellGround` | The client area below the caption | Paints **nothing** in transparency mode — the columns over it paint their own |
| `WallGround` | Cover wall, merge queue, Stores, Appearance | **Never.** §1: a wallpaper behind six hundred capsules is a second image competing with all of them |
| `TileGround` | Under the art stack inside one tile | **Never** — see §14.4 |
| `ChromeSurface` | Rail, filter panel | Yes |
| `ChromeGround` | Command bar, cut bar | Yes |
| `CaptionFill` | The 36px title lip | Yes |
| `ChromeRaised` | Hover / selection fill inside the rail and the panel | Becomes a veil — see below |

**Popovers keep an opaque fill.** A flyout is its own popup root and never receives the
window's backdrop, so a translucent fill there would sample the *application* rather
than the desktop and give a different answer at every position on screen.

**`ChromeRaised` is a veil, not an ink.** Opaque, a raised row is the ordinary
`Surface → SurfaceRaised` step. Translucent, a *darker* ink over an already-translucent
rail composites downwards, and the selected row comes out darker than the row beside
it — elevation inverted. So the step becomes a 10% veil of the theme's own `Text`,
which lifts whatever is under it by 1.8×–5.8× on every backdrop measured. §6's
"elevation is the `Surface → SurfaceRaised` step" holds as a *relative* claim, which is
what it always was.

### 14.3 Transparency has its own inks

**The previous measurement was right and the conclusion was wrong.** `TextDim` on
`SurfaceRaised` at 85% over white is 3.1:1, and §13 gap 7 read that as "no reading
surface can be translucent". What it actually proves is narrower: *an ink chosen for an
opaque ground cannot have alpha subtracted from it.*

So transparency mode carries its own token set. Each translucent surface takes a
**darker** ink than its opaque twin, at 86–91% alpha, and `TextDim` **brightens** to
pay for what is left. The result clears the opaque numbers rather than falling under
them, against a pure-white backdrop — the ceiling any wallpaper can reach, so the
answer needs no assumption about how Windows composes Mica.

| Default theme, `TextDim` on | Solid | Translucent, worst case (white) | Translucent, measured Mica |
|---|---|---|---|
| Rail | 5.88:1 | **6.54:1** | 9.41:1 |
| Command bar | 6.69:1 | **5.88:1** | 8.79:1 |
| Selected rail row | 5.04:1 | **4.92:1** | 7.41:1 |
| Caption | 5.0:1 | **7.71:1** | 9.96:1 |

The other three themes land higher on every row, because each starts from a lighter
ink or a higher alpha: at the white ceiling the rail reads 7.03:1 (Cold storage),
8.04:1 (Nightshift) and 7.26:1 (Phosphor).

**What it costs, stated rather than buried.** Two rows go backwards at the ceiling and
both stay over AA: the selected rail row 5.04:1 → 4.92:1, and the command bar
6.69:1 → 5.88:1. `Flare` on the rail drops 4.91:1 → 4.14:1
in that same worst case. It is a 10px dot with a `Ground`-coloured ring and a rail count
and a tooltip behind it (§5.2, §8), not text, and it stays the loudest thing in the
column. And §9's unlit lip is a *relative* rule: over the measured Mica composite the
caption sits at 0.35× `Ground`, but a backdrop brighter than about mid-grey lifts it
above the body. The wall stays opaque, so that is the whole of the visible cost.

**Requested is not active.** Windows 10, a remote session and a compositor that refuses
all end with `ActualTransparencyLevel` reporting something other than `Mica` — and
Avalonia's Win32 backend falls back to `Transparent`, not the `None` that was asked for,
so the test must be positive. When the answer is no, the **opaque** token set is applied
and the settings screen says so in words. The preference is remembered either way.

### 14.4 The dormancy ramp over a translucent window

§5.4's ramp is a two-layer opacity cross-fade, and the two layers are only opaque
*together*. Between the first bitmap decoding and the second, a dimmed tile is a partly
transparent tile — and on a translucent window that means the desktop showing through
the ramp's floor. Each tile therefore paints `TileGround` under its art stack, opaque in
every theme and both states, so the ramp composites over exactly the ground it was
calibrated against. That is a fact of construction, not a measurement that could drift.

### 14.5 Every themeable brush is declared as an attribute

`<SolidColorBrush x:Key="X">#16282A</SolidColorBrush>` and
`<SolidColorBrush x:Key="X" Color="#16282A"/>` look identical and are not: Avalonia's
XAML compiler constant-folds the first into an `ImmutableSolidColorBrush`, whose colour
cannot be written. A theme change works by writing `Color` on the brush objects the
views already resolved — `StaticResource` looks up once and never again — so a folded
brush is a token the theme system silently cannot reach. Measured, not assumed: the
first build had thirty-five of them, and the symptom was a window that half repainted.
