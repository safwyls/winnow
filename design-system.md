# Winnow — Design System

**Applies to:** Avalonia 11+ desktop client, dark-only
**Companion files:** `src/Winnow.App/Themes/tokens.axaml` (the token dictionary),
`mock-library.html` (visual target)

This document owns the palette, the type, the layout, the dormancy encoding, the components,
the copy, the accessibility floor, the themes, translucency and the two layouts. Why a value
is the value it is, where the reasoning is longer than the rule, is in `docs/decisions.md`.

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
| `Well` | `#050D0E` | Scrollbar track, modal scrim, and the ground under the panes in the floating layout |
| `Ground` | `#0F1C1E` | The art field — deep green-teal ink, never black |
| `Surface` | `#16282A` | Rail, filter panel, the list view's column-header strip |
| `SurfaceRaised` | `#1D3437` | Hover, selection, popovers |
| `Line` | `#2B4A4C` | Dividers, borders, tile outlines |
| `Text` | `#F0EDE7` | Primary text — warm off-white |
| `TextDim` | `#8FA5A0` | Labels, metadata — sage, the room's own colour |
| `Flare` | `#FF4D93` | **Patched since you played** — the unread signal |
| `Volt` | `#4DE8C2` | **Active / recent / selected** |
| `Amber` | `#FFB63D` | Attention: high playtime, "played out", warnings, and a live measurement that has crossed a stated line |
| `Azure` | `#57A8F0` | Informational, links, secondary counts |
| `Danger` | `#E04B45` | Destructive affordance: the window close button's hover fill, and the confirm button on the one destructive act in the application (§12.3) |

The room is a hued neutral rather than grey on purpose: grey would make `Volt` a decoration
sitting on top of the chrome instead of the chrome's own colour intensified. The default dark
app purple was tried and rejected, which for a library about your own hoard is exactly the
wrong register; `docs/decisions.md` records why.

### Discipline

`Flare` is the rarest colour in the interface and appears **only** on unread-update markers,
the bucket that counts them, and the gap rail's marks in the detail view (§10.2), which are
the same fact plotted in time. The instant it becomes a generic accent, the badge stops
meaning anything and the product loses its point.

`Volt` carries selection and recency. `Amber` carries attention. `Azure` is the neutral one
and does the boring work. `Danger` sits at hue 2° against `Flare`'s 336° so that a red the
size of a caption button can never be mistaken for a 10px unread dot.

**Hierarchy is carried by temperature as well as lightness.** `Text` is warm off-white and
`TextDim` is a cool sage: primary text reads as paper laid on the room, metadata as part of
the room. Do not "fix" this by neutralising either one.

**Never tint cover art with brand colour.** The art is content; the interface stays out of it
except through the saturation ramp in §5.

---

## 3. Typography

Three roles, three families. All SIL OFL, bundled as `AvaloniaResource`. There is no
system-font fallback: the display face is load-bearing.

| Role | Face | Usage |
|---|---|---|
| Display | **Bricolage Grotesque** Bold | Bucket names, screen titles, tile titles in list view |
| Body / UI | **Plus Jakarta Sans** Regular / Medium / SemiBold | Labels, buttons, prose, tooltips |
| Data | **IBM Plex Mono** Regular / Medium | Playtime, dates, counts, durations, fractions |

**Bricolage Grotesque is the voice.** Slightly irregular, optically quirky, sharp — it reads
like game packaging rather than a dashboard.

**Bundle static instances, never a variable font.** Avalonia 11 has no API for variable-font
axes: `FontFeatures` maps to HarfBuzz OpenType *features*, not `fvar` axes, so a variable TTF
renders at its default light instance and every bold display style comes out wrong. There is
consequently no `wdth` to set; Bricolage's static Bold is `wdth` 100, which is the widest cut
the face has. `src/Winnow.App/Assets/Fonts/README.md` lists the exact files.

**Every number is Plex Mono with tabular figures** (`FontFeatures="tnum"`). This is not
optional in list view, where a playtime column that does not align vertically is unreadable
at scan speed.

### Scale

```
Display L    22px / 26  Bricolage Bold
Display S    12px / 15  Bricolage Bold, +0.06em, uppercase
Body L       15px / 22  Jakarta Medium
Body         13px / 18  Jakarta Regular
Label        11px / 14  Jakarta SemiBold, +0.04em, uppercase
Data         12px / 16  Plex Mono Regular, tnum
Data S       10px / 12  Plex Mono Regular, tnum
```

`TextBlock.LetterSpacing` is in device pixels, so convert the `em` figures above at the size
they are applied.

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
Default 148×222, gutter 16px. The density slider spans 108×162 → 200×300; the grid reflows on
available width and does not use fixed column counts.

**Radius:** 6px on tiles, 4px on controls, 8px on a floating pane (§15.3). The three rank by
the size of the object they round.

**Elevation.** Tiles get a real drop shadow on hover (`0 8px 24px rgba(0,0,0,.5)`) plus a 2px
lift. This is the one place shadow is permitted; everywhere else, elevation is the
`Surface → SurfaceRaised` step, which is a *relative* claim about a fill and its neighbour
rather than a pair of fixed tones (§14.2).

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

**Clamp at `0.22 / 0.68` — never fully grey.** A cover you can't identify is a cover you can't
choose, and the point is to make forgotten games *findable*, not invisible. **Saturation, not
brightness, is what carries the dormancy signal**, which is why the floor is set high.

A **−6° hue rotation is part of the floor.** The matrix is Rec.709 luma desaturation, then the
hue rotation, then a uniform brightness scale; brightness is a scalar and commutes, so it is
folded in last. `CoverImaging.FloorMatrix` is the implementation and
`PlaceholderArt.ToFloor` is the same arithmetic per channel — the placeholder and the real
cover must fade to the same endpoint, or a tile visibly jumps when its cover arrives.

The hue term is what makes dormant art read as *cool* rather than merely grey. Small, but
load-bearing: Steam capsules are mostly warm and dark, and without it the floor lands on a
neutral-warm mud that looks like a rendering fault instead of an encoding.

**Hover restores full saturation over 140ms.** The game wakes up under the cursor. This is the
single most important interaction in the app: it makes the dormancy encoding legible by showing
you the before and after, and it feels good.

### 5.2 Unread badge

10px `Flare` dot, top-right, 8px inset, with a 2px `Ground`-coloured ring so it reads against
any cover. Optional soft outer glow at 30% opacity.

Present only when a major update landed after the user's last session — both signals from
`game-library-design.md` §4.5, build push *and* announcement.

**Never on a game with zero recorded playtime; an unplayed game has nothing to be behind on.**
The line is drawn on *playtime*, not on a bucket name. `game-library-design.md` §6.1's
`Never played` bucket happens to be the same set today, because that bucket means never
opened, but the two are separate claims and the badge must not start reading a bucket. The
update poller's eligibility filter draws the same line, for the same reason, on the same
field.

Clicking the badge opens the patch notes for the updates you missed, from the `url` stored on
the event row. This is the feature that closes the loop: notice → context → launch.

### 5.3 Hover overlay

Bottom third of the tile, gradient scrim to `Ground` at 92%. Title in Body L, playtime and
idle time in Data S. A single primary action, `Play`, in `Volt`.

**Stores are a chip row**, one chip per store the game is owned on, unchanged in appearance for
a single-store tile. A multi-store tile additionally carries a compact one-letter-per-store
mark at rest on the front, which fades out over 140ms as the overlay rises, exactly as the
baked placeholder title does. The chips are the one "where you own it" fact, drawn once, so
the four-fact cap is not breached. The resting mark uses initials because the density slider's
floor is 108px and a row of word-chips is wider than the tile there; the words are reachable on
hover, on the back face, in the modal and in the automation name, which satisfies §8's
decorative-redundant rule.

**Do not show more than four facts.** The tile is a decision surface, not a detail view.

### 5.4 How the ramp is drawn

Avalonia has no CSS `filter`, and **it has no public API for authoring custom effects** — the
effect pipeline is closed, so a shader approach is not available. The dormancy ramp is drawn
as a **two-layer continuous cross-fade** between two pre-computed bitmaps: the full-colour
cover and one floor variant generated at `0.22 / 0.68` with the −6° rotation baked in.
`α = (S − 0.22) / 0.78`, taking `S` from §5.1's saturation column.

Escalate to per-state bitmap variants, or to a matrix path, only if profiling shows the doubled
bitmap memory is unacceptable. **Do not attempt per-frame pixel manipulation on the UI thread.**

A settings toggle disables the ramp entirely by forcing `α = 1` (§8).

**Covers are virtualized and decoded off-thread at display resolution, not full size.** A
1,200-tile grid of 600×900 source bitmaps decoded eagerly will exhaust memory.

**The cover wall is `src/Winnow.App/Views/CoverWall.cs`, and
`Avalonia.Controls.ItemsRepeater` must not be reintroduced.** `UniformGridLayout` charges every
item in a row for a trailing gutter when it computes items-per-line for the scroll anchor, but
packs rows greedily when it places them, so §4's flush-row geometry made the two disagree by one
column at every window width. `CoverWall`'s remarks carry the measurements.

---

## 6. Components

**Rail bucket.** Display S name, Data count. Selected: `ChromeRaised` fill, 2px `Volt` left
edge. The `Patched since` bucket is the only one carrying a `Flare` dot next to its count.
Zero-count buckets render at 40% opacity rather than hiding, so the rail never reflows.

**List view.** Same data, no art dependency: title, store, playtime, idle, unread dot. 44px
rows on `PaneGround`, `Line` rules, `Volt` selection edge, and a column-header strip on
`ChromeSurface` — so the list has the same structure the grid does, a chrome bar above and a
field below. Row fills take `ChromeRaised` for a selection and `ChromeRaisedHalf` for a hover;
they must not take `SurfaceRaised`, which is an ink and would composite downwards over an open
field and invert the elevation (§14.2).

This is the power-user view — sortable columns, multi-select, bulk list assignment. Everything
the grid cannot do densely lives here, which is how the analytics capability stays available
without dominating the default experience.

**Merge confirm queue.** Two covers side by side at 200×300, signal diff between them (title
distance, year delta, publisher). Actions are `Same game` / `Different games` — never
"Merge"/"Cancel", which asks the user to reason about the data model instead of about games.

**Each member states its store.** The store is the fact that decides whether a pair is one
game on two storefronts. Every member carries its stores in the same outlined chip the tiles
wear: 1px `Line`, radius 3, body face 9px, `TextDim`. Placement differs by density because the
space does:

- **Pair layout** (`MergeMemberTemplate`, a fixed 200px column): the chips take their own line
  under the year and entry numbers, in a `WrapPanel` so three chips (123.1px) never clip at
  200px.
- **Roster rows** (`MergeRosterRowTemplate`): the chips lead the metadata line, ahead of year,
  entries and publisher, so down a roster the stores form a column at one constant x.

Members with no ownership row draw no chip row and keep the two-part automation name.

**The card has a maximum width of 840px and is centred.** The roster density sets the ceiling,
not the pair: card chrome 44 + cover 200 + gutter 28 + roster row minimum 526.0 (member chrome
30, checkbox 16 + 14, chip cover 64, two 14px margins, the condensed evidence line at 271.7,
and the "Keep this title" radio at 102.3) = 798. 840 clears that with slack for shaping
variance, sits on §4's 4px grid, and is twice the 420px feed card measure. The pair layout
needs only 750. Both densities take the one ceiling, and the primary keeps its 200×300 capsule
at both, so the card's outer geometry never changes between them. Widths were measured against
the bundled OFL faces at the exact sizes, weights, letter-spacing and padding the markup sets.
This is a two-column comparison and not prose, so no reading measure governs it.

**Session journal prompt.** 400×220 frameless, bottom-right, `SurfaceRaised`, with the game's
cover at 60×90 on the left. Title, duration in Data, one text field, 5-dot rating in `Volt`.
Appears at most once per session, never steals focus.

---

## 7. Copy

Plain and specific. The app knows something faintly embarrassing about the user — they own
1,247 games and have opened 412 of them zero times — and must never be smug about it.

| Context | Write | Don't write |
|---|---|---|
| Bucket: updates missed | `Patched since` | `Needs attention` |
| Bucket: never opened | `Never played` | `Pile of shame` |
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

The last one matters: store metadata backfill takes hours, so the interface promises a
browsable library immediately and art later. **Render placeholder tiles with the title set in
Bricolage on a `Surface` field — never a spinner, never an empty grid.**

The table does not yet carry rows for connection state or credential consent; the Stores
panel's strings were written from the auth spikes instead. TASK-81.

---

## 8. Accessibility floor

- **The saturation ramp is decorative-redundant.** Idle time also appears as text on hover and
  as a sortable column in list view. A user who cannot perceive the fade loses nothing. The
  unread badge is likewise backed by the rail count and a tooltip.
- **Focus is a brush swap on a border whose thickness never changes** (§10.7). It is drawn per
  control rather than left to Avalonia's global `FocusAdorner`, which measurably underdelivers
  and does not render inside a popup at all. Every focusable control carries a visible ring;
  `Volt` everywhere except on a `Volt` fill, where it is `VoltInk`.
- Full keyboard grid navigation: arrows, `/` to search, `Enter` to launch.
- **Reduced motion disables the hover saturation animation** — state snaps instead of fading.
  There is no rule yet for an indeterminate indicator, and none may ship before there is:
  TASK-79.
- `TextDim` on `Surface` measures **5.88:1**, and on `SurfaceRaised` — what a selected list row
  puts under the store and idle columns — **5.04:1**. **Do not dim further.** `Text` on
  `Surface` is 13.1:1, `Azure` 6.03:1, and `Volt` on `Ground` 11.3:1.
- **A watermark the user is expected to read is `TextDim`, not `TextFaint`.** `TextFaint`
  measures 4.13 / 3.69 / 3.58 / 4.12 across the four themes on the *opaque* ground, which is
  under AA before transparency exists. `TextFaint` is for disabled arrows and decoration.
- A settings toggle disables the dormancy ramp entirely for users who prefer uniform art. The
  badges and buckets carry the signal without it.
- The caption buttons are real buttons, reachable by Tab like anything else. `Danger` is never
  the only thing distinguishing close: it has its own glyph and its own tooltip.

---

## 9. Window chrome

The app draws its own title bar. Avalonia's `ExtendClientAreaToDecorationsHint` with
`ExtendClientAreaChromeHints="NoChrome"` puts the client area over the decorations; the caption
and its three buttons are ordinary controls in the window's own tree.

**The caption's fill is stated once, per layout:**

- **Flush:** the caption *is* `ChromeSurface` — the rail's own ink at the rail's own alpha, at
  every position on the slider. The two meet at a corner, and two tones meeting at a corner is
  a seam, so they are one material and the cover wall is a field recessed inside them.
  `ThemeContrastTests.The_caption_is_the_rail` asserts both halves.
- **Floating:** the caption paints **no fill at all** past `SOLID`, and `ShellGround` shows
  through it. The caption and every gap are one surface rather than two that agree.
  `FloatingLayoutTests.The_caption_is_the_ground` asserts it.

The rule both serve is that **the caption must not be the brightest thing in the window, and
the art must be the first thing on screen with light in it.** Flush satisfies it outright: the
caption is a chrome tone at the pane tier, above the art by the palette's own step. Floating
satisfies it at `SOLID` and over a dark desktop, and **does not satisfy it over a bright
wallpaper** — the ground is the most open surface in the window, so the caption and the gaps
are its brightest band together. That is the two-tier structure being visible rather than a
regression hiding inside it, and it is why the caption sets the AA mark in that layout (§14.3).

`Well` is one step below `Ground` and backs the surfaces where a tone *under* the art field is
the point: the scrollbar track, the detail modal's scrim, and the window ground in the floating
layout.

The mark at the left is two 2:3 capsules, one behind the other: the app's own atom, and what a
hoard of them looks like. Nothing else lives in the caption — no menu, no search, no status.
**It is a lip, not a toolbar.**

**Behaviour the system used to provide is now ours, and all of it is load-bearing.** Drag uses
`BeginMoveDrag`, which hands the press to Windows' own move loop; that is what buys Aero Snap,
the edge previews and Win+Arrow rather than a hand-rolled imitation. The cost is that the loop
is modal and owns the pointer until release, so the second press of a double click may or may
not carry an intact click count. The title bar therefore tests both the framework's count and
its own press clock — 500ms, 8px, in *screen* coordinates, so that a click after a drag is not
read as a double. **Do not also wire the gesture to `DoubleTapped`:** Avalonia raises that from
the tunnelling half of the same press, and a second handler toggles the window twice and lands
it back where it started.

**`OffScreenMargin` is applied to the window's root panel.** Windows sizes a maximised window
past the work area by the resize border, and with the client area extended that overhang is
ours to absorb; without it the caption and the first column of tiles are clipped the moment the
window is maximised.

**The middle button says what it will do, not what state the window is in.** Maximised, it
draws the two-square restore glyph and offers "Restore down".

**Scrollbars** keep Fluent's `ScrollBar` theme and only repaint it, by overriding the resource
keys its template reads. `Application.Resources` outranks `Application.Styles` in Avalonia's
lookup, so the token file wins over the theme without forking a template that would then have
to be maintained. Two changes beyond colour: the stepper arrows are hidden, and the resting
thumb is widened from Fluent's 2px hairline to 4px, because a scroll position you have to hunt
for is not a scroll position on a 606-tile wall. **The thumb is neutral, never `Volt`** — a
scrollbar is chrome, and spending the selection colour on it would make every scroll position
look like a selection.

### 9.1 The resize inset — nothing interactive lives at the window edge

Extending the client area over the decorations does not hand back the pixels the resize border
sits on. Windows still answers `WM_NCHITTEST` for the outer band with
`HTRIGHT` / `HTBOTTOM` / `HTBOTTOMRIGHT`, **before the client area sees the pointer at all**,
so a control flush to the edge is drawn by us and hit-tested by the OS. Measured on this
window: the band is exactly **8px** on the right and 8px on the bottom.

**`ScrollBarEdgeInset` (`0,0,10,10`) is the rule that follows: no interactive control may sit
inside the 8px the OS owns, and anything that would have is stepped 10px in.** The rule is
about **which edge a control is on, never about which control it is.** Interior scrollbars —
the rail's, the detail modal's — opt out with `ScrollViewer.inner`, because their edge is a
divider of ours rather than the window's.

Two consequences of stating it that way:

- **The filter panel takes the inset**, because its right edge is the window's (§11.1). Its
  column went 264 → 276 to pay for the gutter that buys, so the option rows keep the 234px they
  were drawn at.
- **The rule is dropped entirely under the floating layout** (§15.4), because floating moves
  every one of these scrollbars off the window's edge and onto a pane's. Eight pixels of gap
  plus the pane's own border is already outside the band, so the inset would be a second,
  visible 10px gutter inside an 8px-inset card.

Widening the thumb does not help: the border wins above Avalonia's hit testing entirely, so a
wider control is a wider unreachable control. Hooking `WM_NCHITTEST` would buy the scrollbar
back by taking away edge-resize down the whole right side of the window.

---

## 10. The detail view

§5.3 caps the tile's hover overlay at four facts. This is the detail view that cap presupposes.
It stays a **modal over the library**, opened by `Enter` or a double click, dismissed by
`Escape` or a click on the scrim. The library is a scanning surface and the panel is a decision
the user made about one tile in the middle of a scan; a modal keeps the wall's scroll position,
so `Escape` returns them to exactly the row they were reading.

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
object: its art, the id Steam calls it, where it lives on disk. Right is your relationship with
it. The divider spans the right column only, because the left one keeps going. That split also
fills the ~130px of nothing a game with no last-played date used to leave beside a 300px cover,
which read as broken rather than as sparse.

### 10.2 Signature: the gap rail

**The one thing Winnow can draw that nothing else can.** Storefronts hold your last-played date
and they hold a game's patch history; nobody puts them on the same axis. The rail runs from
your last session to now, with the updates that landed in between marked on it.

- **The rule is §5.1's dormancy ramp turned on its side** — `Volt` at the last-played end
  fading to `Line` at today, half faded at the half-way point. The user has been looking at
  desaturating capsules for weeks; this is the one screen with room to say why.
- **Marks are `Flare`**, legal here and only here in the panel, because they are literally
  §5.2's unread signal plotted in time instead of stacked in a tile corner. **Capped at 14**;
  past that a rail is a smear, and the list below stays the exhaustive record.
- **The rail is normalised, never scaled to duration.** A 14-day gap and a 9-year gap draw the
  same length, with the span stated as a number beside it. Scaling would make most rails
  invisible and would be a second, competing encoding of a fact the digits already carry.
- **Everything it draws is restated in words underneath** (§8). A user who cannot resolve a 7px
  dot loses nothing.
- **No last-played date, no rail.** Two different absences, kept apart by the copy: *"You've
  never opened this."* and *"Steam has no date for your last session."*

**It is deliberately not a playtime chart.** The obvious move is a line through
`playtime_snapshots`, and on a real library that table holds one reading per game — measured,
611 of 616 — so a line through one point is a decoration pretending to be evidence. What the
snapshots honestly support is a sentence: *"Checked 12 times since 23 Aug 2026 — up 1h 7m."*
The delta is between the first and last reading Winnow holds, which is the part it actually
watched happen, not the total Steam already knew. At one reading it says so; at zero it says
nothing at all.

### 10.3 Getting in

`steam://run/<appid>` when the game is on disk, `steam://install/<appid>` when it is not — and
**the button is named for which one it is**, `Play` or `Install`. A button reading "Play" on an
uninstalled 60GB game promises something the next hour will not deliver. **No appid means no
primary action at all, never an inert button.**

Beside it, `Store page` and `All patch notes` in `Azure`, and `Open folder` when there is a
path. The folder goes through the launcher's directory entry point as a path, never a `file:`
URI.

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
| No summary yet | `No description yet. Winnow fills the year, publisher and summary in from IGDB as it works through your library.` | `No data` |

Two of those are load-bearing. **"No updates recorded in that stretch"** and not "nothing has
shipped": update polling is staggered across days, so an empty rail can mean a quiet decade or
a turn that has not come round yet, and the interface may only claim the one it can support.
**"Checked"** and not "sampled" or "snapshotted": name the thing by what the person recognises,
not by the table it lives in.

### 10.5 What is absent, and why

`acquired_at`, `license_type` and `price_paid_cents` are in the schema and are populated only
for a user who has run the saved-page import; `platform` and `edition_note` are empty for every
row Steam's local files produce. **None of them is bound in this panel.** Purchase facts belong
to the account stats screen, which is where they are read, and a row that appears for some
users and not others in a panel about one game is worse than a row that is simply elsewhere.
`account_ref` is populated and still absent, because showing a user their own Steam account id
is noise.

**Achievements are not here.** No data exists yet, and `game-library-design.md` §6.2's rule
stands regardless: never a blended cross-platform completion figure. When they land they are
per-release rows, not an average.

### 10.6 Text is selectable

Titles, summaries, install paths and appids are `SelectableTextBlock`, not `TextBlock`. Three
consequences worth recording:

- **`tokens.axaml`'s text styles select on `:is(TextBlock)`, not `TextBlock`.** An Avalonia
  type selector matches the exact type, so a bare `TextBlock.body` silently skips
  `SelectableTextBlock`. The failure is not a build error; it is unstyled text in a system font.
- **Selection needs no focus; `Ctrl+C` does.** `SelectableTextBlock` arrives focusable from its
  own control theme, so every selectable line is a Tab stop unless told otherwise. The four
  worth stopping on keep it; everything else sets `Focusable="False"`. With all of them
  focusable it took five Tab presses to reach `Play`.
- **Focused text gets a raised field, not a ring.** A 2px outline around a five-line paragraph
  is a box, not an indicator.

### 10.7 Focus is drawn, not adorned

**The ring is a brush swap on a border whose thickness never changes.** Thickening a border on
focus reflows the row it sits in, and buttons that shuffle sideways as you tab through them are
worse than no ring. It is set on `PART_ContentPresenter` rather than on the Button, because
Fluent's own state styles write that presenter directly and a `TemplateBinding` loses to them
on hover.

This is the rule the whole application follows, and §8 states it as the floor. Avalonia's
global `FocusAdorner` was measured on the running window and delivered a few stray pixels at
the corners of one button and nothing on the rest.

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

Steam's library filter is the reference and not the template. Its shape is six columns of
unlabelled checkboxes plus two free-text fields, and most of what it asks about — friends,
languages, Deck compatibility — is data Winnow does not have.

**Two things the reference gets right, kept.** A count beside every option, so a filter that
leads nowhere says so before it is clicked. And one surface you scan rather than a menu you
drill into.

### 11.1 The panel is the right-hand column

`Filters` opens a **276px column to the right of the grid**, on `ChromeSurface`. It is not a
drawer over the art and not a popover.

**It is on the right because its controls are.** `Filters` and `Clear filters` both sit in the
command bar's right cluster; on the right the toggle sits directly above the column it opens,
`Clear filters` and the `926 → 136` line land against its edge, and the eye that follows a cut
ends up beside the counts that made it.

**Its left edge is the seam between the art and the chrome** — the same seam the rail's right
edge is, mirrored — so it takes the same treatment: 1px `Line`, `Surface` behind it, nothing
softer. **The panel is a peer of the rail rather than a second column of it.**

**Its header is 48px, the command bar's height**, so the rule under `FILTERS` continues the
rule under the command bar straight across the window at y=92, measured on the running window,
under two 48px headers. This holds in both layouts.

**The rail is not duplicated, and it is still part of the filter.** The rail owns the bucket
axis; the panel owns every other one; **neither offers the other's.** Two controls writing one
axis is how a panel starts disagreeing with the screen behind it. The cut bar (§11.3) is what
carries that claim: the bucket is a chip there beside the panel's own, and drops like any other
rule.

**Its right edge is the window's, so §9.1 applies to it.**

**Tab order follows the window in reading order** — rail, command bar, grid, panel — which
means the panel is last in the file as well as last on screen. A `Grid.Column` says where a
control sits; its position in the markup says when it is reached.

**The grid narrows rather than being covered.** That costs a column of tiles and buys a panel
you can leave open while you scan, which is the only way the counts pay for themselves, because
their whole value is watching them move.

**Nothing here is a popup**, so the focus ring works normally. The facet checkbox still draws
its own ring in its control template, for §10.7's reason: one focus treatment across the app
beats two.

### 11.2 Counts are residual, and each group lifts its own

The number beside an option is **what you would get if you ticked it** — computed with every
*other* group's selections applied, this group's own selections lifted, and the rail's bucket,
any open list and the search box all in force.

Lifting the group's own selections is the part that is easy to get wrong and fatal when it is.
Options inside a group are an OR, so ticking one genre must not drop every other genre to zero.

**An option whose residual count is 0 renders its zero and stops being a click target and a tab
stop**, at the 40% opacity §6 already gives a zero-count bucket. **An option that is ticked
stays live whatever its count says:** the way out of an empty result has to be the control that
caused it.

**Order freezes on the first counts.** A long group leads with its commonest options and then
holds that order for the session. Re-sorting on every recount is the obvious reading of
"commonest first" and it is wrong: every tick anywhere on the panel moves every count, so the
rows would rearrange under the pointer between one click and the next.

**Counts are taken per tile, not per release.** The grid is one tile per game rather than one
per ownership, so the rule reads as "tiles that include this store": a twice-owned game counts
under both platform options, and the Platforms screen and the filter panel compute the same
relation and therefore agree. **The per-store figures consequently sum to more than All Games,
by exactly the number of extra store memberships.** The panel tallies its own sets rather than
calling `FacetSnapshot.CountsFor`, whose `Distinct()` collapses exactly that pair.

### 11.3 The cut bar

One strip under the command bar, present only when the grid has stopped showing the whole
library:

```
[ LIVE LIST Co-op, controller-ready × ] [ Shooter × ] [ Horror × ]
                                     926 → 2   Update list   Revert   Clear filters
```

**`926 → 136` is the signature of this screen.** It is the only arrow in the interface, because
this is the only place a number becomes another number. Plex Mono, tabular; the total in
`TextDim`, the result in `Volt`.

The bar exists because *a library that has been cut down and does not say so is the most
expensive confusion this screen can produce* — the panel can be closed and the rail scrolled
past, and then 136 of 926 games look like the whole hoard. Each chip carries its own dismissal,
so undoing one rule never means hunting for the control that set it.

**Chips are `Volt`-edged, never `Flare`.** A chip is a selection, which is what `Volt` is for.
There is deliberately **no "has updates" group** anywhere in the panel: that set is exactly the
rail's `Patched since` bucket, and a second door onto it would need a second marker, and the
only marker for unread is `Flare`.

**Every chip says who set it, and the grammar is the palette's own: `Volt` means you chose
this.** A rule an open live list contributed was not chosen by the user (§12.2), so it drops
the `Volt` edge and takes the neutral `Line` one, with its label at `TextDim`. Three families,
two edges:

| On the bar | Edge | Means |
|---|---|---|
| The open list, leading | `Line`, kind label shown (`LIVE LIST`) | The place you are in. Its × leaves |
| A rule the list brought | `Line`, `TextDim` label | The list set this, not you |
| A rule you set | `Volt`, `Text` label | You set this — inside a list it is an unsaved edit |

**The distinction is never carried by the edge alone:** each chip's tooltip says it in words.

**The open list leads the bar, ahead of the bucket.** It is not a rule but the place the rules
belong to, and "which live list am I in" is the question the strip previously could not answer.
The kind label is on the chip rather than only in a tooltip for the same reason §12.1 puts it
in a heading rather than a dot: a word survives being read badly.

**The bar carries at most four actions at once**, and membership actions and list metadata are
mutually exclusive: with rows selected you are editing what is *in* the list, with nothing
selected you are editing the list itself.

### 11.4 What is drawn, and the rule that decides

`genre` · `theme` · `game mode` · `store tag` · `features` · `controller` · `store` ·
`on disk` · `release year`.

**Every group here is a group a live list can store.** That is the rule. `FacetKinds` also
holds player perspective, which `LibraryFilter` has no field for, so it is not drawn: a rule
that vanishes the moment you save it is worse than a rule you never had. `FeatureIds` and
`ControllerIds` were *added* to the filter record rather than the groups dropped, which is the
same rule pointing the other way.

Two absences are load-bearing:

- **A dimension with no data draws nothing.** Four columns of greyed checkboxes is the wall
  this panel is not. When none of the metadata-backed groups are present the panel says so in a
  sentence instead.
- **A dimension whose one option is true of every title draws nothing.** "STORE · Steam 926" on
  a Steam-only library is a fact restated as a control that cannot change anything. It
  reappears by itself the day a second store lands.

**Release year is two Plex Mono fields, not a slider and not a histogram.** A year is four
characters the user already knows; a range set by dragging is a range they cannot state
exactly. A drawn year distribution would be a second visual language competing with the art two
columns away (§1). The watermarks are the real bounds of the library, so an empty field still
says what there is. **A release with no year does not match a bounded range** — an absent fact
is not evidence.

---

## 12. Lists and live lists

**A list is one the user fills by hand. A live list is one that holds a rule and finds its own
members.** Never "smart", never "dynamic collection": §7 names things by what the user
controls, and what they control is whether the thing keeps up with them. The action on the cut
bar is **`Save as live list`**.

### 12.1 Two rail sections, and no second dot

```
── LISTS ─────────────
   Couch co-op night   4
   Finish these first  5
── LIVE LISTS ────────
   Co-op I bounced off 136
   Unplayed adventures 342
```

**The kinds are told apart by heading, not by a coloured mark.** A pip beside a count was the
obvious move and the wrong one: the rail already has exactly one dot, the `Flare` pip on
`Patched since`, and a dot's meaning survives precisely as long as there is only one of them.

Rows take the bucket treatment — hover fill, 2px `Volt` selection edge — with one difference:
**the name is body type, not Display S caps.** Bucket names are the application's own
vocabulary and are shouted; a list name is the user's own sentence and is not.

Both kinds recount on every library load. A manual list drops a count when one of its games is
consolidated away or filtered out as a non-game entry; a live list's number moving on its own
*is* the feature.

`LISTS` is the heading that always exists, because it is where a first list lands; `LIVE LISTS`
appears only once there is one. Empty: *"No lists yet. Select titles and choose Add to list, or
filter the library and save the result as a live list."*

**The rail's grammar, which any rearrangement must preserve:** everything above the divider is
a subset of ALL GAMES; below it, content precedes work queue precedes configuration.

### 12.2 A list composes, a live list restores

**A manual list is one more AND term** over the library, not a separate screen, so the rail,
the panel and the search box all still work inside it.

**A live list adds no term at all.** Opening one pours its saved rules back into the rail and
the panel, so the user is looking at the filter that defines it and can edit it in place. That
difference *is* the two kinds, made visible by the controls rather than explained in a tooltip.

Editing an open live list turns the cut bar into `Update list` / `Revert` — both answers by
name, because neither is obviously right and neither should happen by accident.

**A list is a context, not a switch.** You are in exactly one at a time, and selecting `All
games`, a bucket, or another list *leaves* the one you were in and takes its contribution with
it. A live list contributes the rail's bucket, the panel's groups and the search box, and all
three go. A manual list contributes only membership, so leaving it takes only that.

Three consequences, each of which could reasonably have gone the other way:

- **The panel stays open on the way out.** Closing it would hide the very thing that proves the
  rules left, and the user did not open it — entering the list did.
- **Clicking the bucket you are on does not clear it while a live list is open.** That escape
  hatch answers "you clicked this twice", and inside a live list the lit bucket was clicked
  once, by the list. There, clicking it means "give me that bucket and nothing else."
- **`Update list` is unaffected.** The rules stay in the controls and stay editable; they
  simply do not outlive the context.

**The rail carries the same distinction.** The `Volt` edge means *this is where you are*, and
**exactly one row ever has it.** With a list open that row is the list, so a bucket in force
takes the selection fill with a `TextDim` edge instead: a rule that is cutting the grid, not a
second claim to be where you are.

One place this model does *not* reach: the panel's own ticks. **A checked box is `Volt` whoever
ticked it**, because a tick means "in force" and a second tick treatment would be a third thing
to learn on the surface that can least afford one. The bar carries provenance; the panel
carries state.

A manual list opens in **`List order`**, a sort row that exists only while one is open, and
leaving the list puts the previous order back. `Move up` and `Move down` go dead at the ends of
the list rather than staying lit and doing nothing.

### 12.3 The action bar, and why there are no flyouts

Naming a live list, picking a list to add to, renaming one and confirming a delete all happen
in **the same strip**, replacing the cut bar while they are up.

This is not a stylistic preference. Avalonia's global `FocusAdorner` does not render inside a
popup — a popup is its own root and has no adorner layer — so every control in a menu here
would need its ring hand-drawn, which is §10.7's reason for the detail panel having no flyout
either. In the window's own tree, the focus ring and a linear tab order both come free, and the
question sits directly above the thing it is about.

`Enter` confirms, `Escape` cancels, and focus follows the prompt into its field. The save
prompt opens with the rules read out as a suggested name ("Bounced off · RPG"), because a rail
full of "Live list 3" is a rail nobody reads.

**`Add to list` is one control for both views.** The grid selects one tile, the list view
selects many, and the button reads whichever is in force, naming the number once there is more
than one. The picked set is derived from the selection in the view model rather than in the
pointer handler, so arrowing across the wall arms it exactly as clicking does.

**Deleting asks first, and the question says what survives:** *"Delete "Couch co-op night"? The
titles stay in your library."* It is the only destructive act in the application, and `Danger`
appears on its confirm button and nowhere else on the strip.

### 12.4 `Escape` unwinds the cut, one layer per press

Outermost first: the panel closes; then an unsaved edit to an open live list reverts; then the
filters clear — *unless* a live list is open, in which case they belong to the list and this
layer is skipped; then the open list closes, taking its own rules with it; then the bucket
clears. **One key, and no press is ever a no-op while anything is still cutting the grid.**

The two live-list layers are why the ladder is not simply "clear everything". Clearing the
panel inside a live list is not a step back out; it is a fourth, emptier version of the list,
still labelled as the list.

**Every letter key yields to a focused text field.** The panel has a find field per long group
and two year fields, and typing "f" into "Find a tag" would otherwise close the panel being
typed into.

### 12.5 Motion, and the command bar that had to give way

Nothing here animates except the 120ms fill cross-fade the rail rows already had, and **every
`Transitions` value is set through a style, never as a local value on an element.** A local
`Transitions` outranks any style selector trying to remove it, which would make §8's
reduced-motion rule unenforceable on exactly the controls that had been given the most care.
The panel itself does not slide: a column that animates costs the grid a reflow per frame, and
it buys nothing.

The command bar's search box is a **star-sized column among Auto ones**, and the window's
default width is 1280. A Grid satisfies its Auto columns before its star one, so the search box
is the only thing that gives way when the panel takes 276px out of the row. At a fixed 360 it
was the `Filters` button that got pushed off the right edge — the one control that must never
be unreachable, because it is the way back.

---

## 13. Reserved

This section number is retired. It held a register of open design gaps found while building the
Stores panel; four of them are now TASK-79 through TASK-82, one is TASK-42, and two were closed
in place — focus is §10.7's brush swap, and translucency is §14.

---

## 14. Themes and translucency

**Dark-only is still true; one-palette is not.** Four themes ship, the default is unchanged,
and a transparency **slider** sits beside them. Both settings live on the rail's
`SETTINGS › APPEARANCE` screen and persist in `settings`.

### 14.1 What a theme may change, and what it may not

**The role is the invariant; the colour is not.** §2 assigns every hue a job, and a theme may
change which colour plays a job. **It may never change what a job means, and it may never spend
one job's colour on a second one.**

**`Flare` is the load-bearing case.** It marks unread updates and the bucket that counts them,
in every theme, and **no theme's `Volt`, `Amber`, `Azure` or `Danger` may equal it.**
`ThemeContrastTests` asserts that per theme, along with a minimum hue separation from `Danger`
(24°, the gap §2 already accepts for the default pair) and from `Volt` (60°).

**Two rules of construction carry across the table.** Every theme's `Volt` is its own room at
full voltage. And every theme's `Flare` is the one hue that room cannot produce.

### 14.1.1 The four themes, and the axes that separate them

A room is separated by four things, and hue is the least of them. A set that differs in hue and
value alone reads as four settings of one theme.

| Axis | What it decides | Where it lands |
|---|---|---|
| **Temperature** | Which end of the wheel is ground and which is signal | Winnow and Nightshift cool · **Tungsten warm** · Box art neutral |
| **Chroma strategy** | How much colour the chrome is allowed at all | Winnow committed · Nightshift almost none · **Box art none, and the art is the only colour in the window** |
| **Value structure** | Where the contrast lives — stepped surfaces, or flat ones with the edges doing the work | Winnow 1.8x art→chrome · **Nightshift 1.4x, flat** · Tungsten 1.8x with the faintest edges · **Box art 4.8x, stark** |
| **Material** | What the chrome reads as | inked board · black glass · felt · mount card |

**The test of the set is that a thumbnail of the rail alone identifies the theme, with no
label.** If two are distinguishable only by hue, one of them is not earning its slot.

| Theme | What it is, in one sentence |
|---|---|
| **Winnow** *(default)* | An inked green-teal stage, stepped evenly, dark enough that the cover art is the only lit thing in the window. The one tuned against six hundred real capsules. |
| **Nightshift** | Black glass: the surfaces stop stepping apart and every boundary becomes a drawn line, so the window is one dark pane with the layout scribed on it. |
| **Tungsten** | A warm room lit by one lamp — the only theme that is not cool. Edges nearly disappear and warm cover art settles into the field instead of standing off it. |
| **Box art** | A neutral mount with a 4.8x drop into a near-black art field. The chrome gives up colour entirely, so the covers — and the unread dot — are the only hues on screen. |

Nightshift's argument is **where the contrast lives**, not how dark it is: `Line` runs at
2.46:1 against the rail, the brightest edge in the set and nearly twice Winnow's, while the art
field, the rail and the caption sit within 1.4x of each other. Tungsten is the same idea
inverted, with the faintest edges in the set at 1.38:1.

Box art is §1 taken to the end of its argument. `Volt` is cold white light rather than a
colour, because a neutral room at full voltage is not a hue. Only the two colours that mean
*stop* and *unread* keep their saturation, which makes it the one theme where §2's rule is
literally visible.

**Two costs, stated rather than hidden.** Tungsten spends the warm end of the wheel on the
ground, so `Volt` (brass, 43°) and `Amber` (ember, 16°) sit 27° apart, told apart by lightness
and by where each appears. Box art has no second saturated colour to spend, so `Volt` and
`Azure` sit 29° apart and are separated by lightness instead: `Volt` is a near-white at 17:1
against the art field, `Azure` a mid steel.

**No light theme, deliberately.** §9 inverts the platform's caption order, §5.3's tile scrim
fades to `Ground`, and §5.1's dormancy floor was calibrated against dark capsules on a dark
field. A light theme is not this table with the steps reversed; it is a second pass over all
three, and half of one would break the ramp that is the product's whole encoding.

### 14.2 Two tiers, and one free quantity

**The window runs at two levels, not three.** Which surface may admit the desktop is a
**token**, not a rule somebody has to remember.

A surface painted on another surface **stacks alphas**: what the desktop finally contributes is
the product. The window's ground is the only surface with nothing above it, so it is the only
free quantity, and everything under it is forced by

```
alpha = 1 − (1 − MinWallAlpha) / (1 − containerAlpha)
```

| Tier | Admits | Paints | Forced by |
|---|---|---|---|
| `ShellGround` — the gaps, and the caption in floating | **85%** | `MinShellAlpha` **0.15** | nothing. This is the choice (§14.3) |
| Any pane — rail, filter panel, art field, settings screens, the list view | **35%** | `MinPaneAlpha` **0.588** | the ground |
| Any input field, in a pane or in the panel | **35%** | `MinFieldAlpha` **0** | the pane it is cut into |

**`MinWallAlpha` is `0.65`**, and it names an *admission* rather than a paint: `1 − 0.65 = 0.35`
is what reaches the eye through a pane. Its derivation is the polarity argument in §14.6.

The rail and the filter panel are **panes**, not chrome. §11.1 calls the panel a peer of the
rail, and the rail owns the bucket, list and settings axis; nothing about either is chrome
except a token name. `ChromeSurface` is therefore a pane token that carries the theme's own
unwalked `Surface` for an ink, and `PaneGround` **is** `WallGround` — the same alpha, the same
ink, and the same *setting*.

| Token | What it backs | Translucent? |
|---|---|---|
| `ShellGround` | The client area below the caption, and every gap in the floating layout | Yes, at the ground tier |
| `WallGround` | The field the covers hang in | Yes, at the pane tier, and only when `appearance.wall` asks for it |
| `PaneGround` | Merge queue, Stores, Appearance, the library's list view, the empty state | Exactly `WallGround` |
| `TileGround` | Under the art stack inside one tile | **Never** (§14.4) |
| `ChromeSurface` | Rail, filter panel, the list view's column-header strip | Yes, at the pane tier |
| `CaptionFill` | The 36px title lip | Flush it *is* `ChromeSurface`. Floating it paints nothing (§9) |
| `ChromeRaised` | Hover / selection fill inside the rail, the panel and the list | A veil, see below |
| `ChromeRaisedHalf` | A *hovered* row where `ChromeRaised` is a selected one | The same veil at half strength |
| `ChromeFieldOnGround` | An input on the command bar or cut bar. Its container is the library pane | Paints nothing past the ink ramp |
| `ChromeFieldOnSurface` | An input in the filter panel | Paints nothing past the ink ramp |

**A pane composites over the ground exactly once.** `FloatingLayoutTests` walks that at every
position, in both layouts, in both reach states. A second element declaring `ShellGround` would
put every figure the Appearance screen prints out by the same factor, and nothing else would
catch it.

**Popovers keep an opaque fill.** A flyout is its own popup root and never receives the
window's backdrop, so a translucent fill there would sample the *application* rather than the
desktop and give a different answer at every position on screen.

**`ChromeRaised` is a veil, not an ink.** Opaque, a raised row is the ordinary
`Surface → SurfaceRaised` step: an ink that *replaces* what is under it. Translucent, a
*darker* ink over an already-translucent pane composites downwards and the selected row comes
out darker than the row beside it — elevation inverted. Interpolating between the two walks
through mid grey at high alpha, which is neither, and it crushed the metadata ink on a selected
row to 4.2:1 six percent into the track.

Only one veil is backdrop-independent. Solving `a·(V − pane) = λ·(Text − pane)` for every
possible `pane` gives `V = Text` and `a = λ`, so **the veil is `Text` and the only free
parameter is its strength.** It starts at exactly the strength that reproduces the theme's own
`Surface → SurfaceRaised` step over an opaque pane, derived per theme, so leaving zero moves
nothing; and it grows to 10% as the pane opens up and there is more under it to lift.

### 14.3 Transparency is a quantity, and it has its own inks

**The slider is 0 to 100, stored as a whole percent under `appearance.transparency`.** A stored
`true` migrates to 25; a stored `false` to 0.

**Zero is a real position, not an off state dressed as one.** It is the default, it is
bit-for-bit the opaque palette with nothing carrying alpha, and it is the answer for anyone who
wants §8's floor with no argument — which is why the label under that end of the track is a
word, `SOLID`, and not an absence.

#### What fixes the ground

The ground answers to one thing: the caption, which carries the wordmark and three window
glyphs and is the only reading matter on it. Walked per theme against white:

| Ground opens to | AA ceiling (Winnow / Nightshift / Tungsten / Box art) | |
|---|---|---|
| 0.12 | 29 / 30 / 30 / 30 | Nightshift loses a point |
| 0.14 | 29 / 31 / 30 / 30 | the marginal value — two themes exactly at par |
| **0.15** | **30 / 31 / 31 / 31** | **chosen** |
| 0.20 | 32 / 33 / 33 / 33 | more range, less window |

`0.15` is the round step past the boundary, it buys 1 to 5 points on top, and it states as a
pair of numbers the Appearance screen prints: **the ground admits 85%, a pane admits 35%.**

#### The two ramps, and why they are not the same ramp

Each translucent surface walks from its opaque token toward a **darker** ink, and `TextDim`
**brightens** to pay for what is left. The alpha and the inks travel on *different* curves, and
that is load-bearing:

- **Alpha falls linearly** across the whole track.
- **The inks finish in the first quarter** (`InkRampSpan = 0.25`) and then hold.

Alpha coming off lightens a dark surface over any brighter backdrop *immediately*, while a
compensation arriving in proportion is always behind it. Front-loading the inks is worth
several points of range on every theme.

**The pane's own alpha rides the ink ramp too**, which is what makes a two-tier window linear.
Two stacked alphas multiply, so a pane on a proportionally-fading ground would admit a
quadratic — 8.75% at the middle of the track where it should admit 17.5% — and the tiers would
sit twice as far apart through the part of the slider anybody uses as they do at its end. The
ground's share is already linear in the slider position, so the moment the pane's factor stops
moving the product is linear at exactly the wall's rate:

```
t     0.10   0.20   0.25   0.40   0.60   0.80   1.00
pane  1.4%   5.6%   8.8%   14.0%  21.0%  28.0%  35.0%
0.35t 3.5%   7.0%   8.8%   14.0%  21.0%  28.0%  35.0%
```

Sub-linear under the first quarter, which is the safe direction, meeting the linear part
exactly at `InkRampSpan`.

The ground's ink bleeds into the panes and it was measured: some fraction of every pane is
`Well` rather than its own tone, 9.5% at the far end and at most 34% in the middle where the
pane is still nearly opaque. Against the same pane painted straight onto the desktop the worst
tone difference is **1.06 to 1.11:1**, under the `Well`-to-`Ground` step itself.

#### What it measures

Walked per theme against white, the ceiling any wallpaper can reach:

| Last whole percent still clearing 4.5:1 | Winnow | Nightshift | Tungsten | Box art |
|---|---|---|---|---|
| **The reported AA ceiling** | **30** | **31** | **31** | **31** |
| The caption on the ground (this sets the mark) | 30 | 31 | 31 | 31 |
| The caption in flush | 56 | 69 | 61 | 57 |
| A selected rail row | 40 | 54 | 47 | 41 |
| The rail's own labels | 56 | 69 | 61 | 57 |
| A pane's `TextDim` | 63 | 71 | 68 | 74 |
| A selected list row in a pane | 48 | 56 | 53 | 56 |
| The field's polarity floor | 34 | 47 | 41 | 44 |

**The mark is about whichever surface is most open and carries text**, which is the caption on
the window's ground, in the floating layout. `Colorimetry.AaCeiling` **walks both layouts and
reports the worse**, so the mark means one thing whichever layout is up and flipping the layout
can never invalidate it.

**Over a dark desktop the number never gets worse**, at any position, in any theme: the
composite is darker than `Ground`, so opening a surface deepens the ground its labels sit on.
`ThemeContrastTests` asserts that across the range.

**The range past the mark is a choice the user is allowed to make.** Being protected from it is
not a service, and being ambushed by it is not either — so the Appearance screen draws the mark
on the track and reports **both** numbers live, in Plex Mono `tnum`, with the worst-case figure
turning `Amber` and naming the line it crossed once it does. `Amber` and not `Danger`: §2 gives
`Amber` attention and `Danger` the one destructive act, and a setting chosen with the number in
front of you is neither an error nor something to be undone for you.

**Requested is not active.** Windows 10, a remote session and a compositor that refuses all end
with `ActualTransparencyLevel` reporting none of the levels that count, and Avalonia's Win32
backend falls back to `Transparent` rather than the `None` that was asked for, which is a
genuinely see-through window with nothing behind it. **So the test names the levels that count
positively — `== Mica`, `== AcrylicBlur`, `== Blur` — and never "not `None`".** When the answer
is no, transparency is treated as zero and the settings screen says so in words. The preference
is remembered either way.

### 14.4 The dormancy ramp over a translucent window

§5.4's ramp is a two-layer opacity cross-fade, and the two layers are only opaque *together*.
Between the first bitmap decoding and the second, a dimmed tile is a partly transparent tile,
and on a translucent window that means the desktop showing through the ramp's floor.

**Each tile paints `TileGround` under its art stack, opaque in every theme and every setting**,
so the ramp composites over exactly the ground it was calibrated against. That is a fact of
construction, not a measurement that could drift, and since the art field can open up it is the
only thing holding — so the tests assert it in both reach states rather than in the default one.

Verified by pixel diff: at the far end of the slider with the wall open, 187,192 pixels in the
wall region differ from the same capture with the wall solid, and every one of them is the
field or within 2px of a tile's antialiased edge. Not one pixel inside a tile changed.

### 14.5 Every themeable brush is declared as an attribute

`<SolidColorBrush x:Key="X">#16282A</SolidColorBrush>` and
`<SolidColorBrush x:Key="X" Color="#16282A"/>` look identical and are not: Avalonia's XAML
compiler constant-folds the first into an `ImmutableSolidColorBrush`, whose colour cannot be
written. A theme change works by writing `Color` on the brush objects the views already
resolved — `StaticResource` looks up once and never again — so **a folded brush is a token the
theme system silently cannot reach.** Measured, not assumed: the first build had thirty-five of
them, and the symptom was a window that half repainted.

### 14.6 Which material, and how far it reaches

The slider says **how much**. Two smaller decisions say **what of** and **how far**, and both
sit on the same Appearance card as qualifiers of the one quantity, not as two more rows.
Neither is drawn at all while the slider is at `SOLID`, because at `SOLID` neither does
anything.

#### Acrylic or Mica, said in the UI and measured on the screen

**The head of the hint list is the user's choice**: acrylic asks `[AcrylicBlur, Mica, None]`,
Mica asks `[Mica, AcrylicBlur, None]`. **Acrylic is the default**, because it is the one the
slider can be seen through.

**Mica is described by what it does, not sold as a lesser acrylic.** Back-solved from the pixel
on screen, at 45% over the same wallpaper in the same window position:

| Backdrop the window actually received | Under the lit rock | Under the sky |
|---|---|---|
| **Acrylic** | `#CC6E3A` | `#636573` |
| **Mica** | `#2D1C17` | `#201F24` |

Windows composes dark Mica by tinting toward its own near-black base so hard that the wallpaper
contributes almost nothing: it lands within a couple of units of the same near-black in both
places, which is `#201F1E` measured a second time. Acrylic carries the wallpaper and changes
across the window. That table *is* the argument for offering both, and a condensed form of it
is on the Appearance screen beside the choice.

**A substitution is a third answer, not the second one.** Mica needs Windows 11 and acrylic
works further back, so the window still falls through — but **the material that came back is
reported by name**, in an `Amber` field. Falling through is right; doing it silently is how a
user concludes the choice does nothing.

#### The field may open up; the tiles may not

Covers sit solid on an open field and the desktop shows in the gutters between them. §14.4's
`TileGround` is what answers the cross-fade, not keeping the field opaque.

**The wall admits 35%, and the constraint that fixes it is polarity, not contrast.** §5.1's
ramp is dark capsules on a dark field and only reads that way while the field stays *darker*
than the capsules on it. Over white the field climbs and eventually passes the dormancy floor
of an ordinary dark cover, after which a dimmed tile reads as a hole punched in a lit field.
The wall does not have to hold across the whole slider — past the AA mark the user has already
been told the labels stop clearing 4.5:1 — **it has to not fail first:**

| Per theme (Winnow / Nightshift / Tungsten / Box art) | |
|---|---|
| The reported AA ceiling | 30 / 31 / 31 / 31 |
| Field inverts the ramp, at `MinWallAlpha` `0.60` | 25 / 40 / 33 / 38 — **Winnow fails early** |
| Field inverts the ramp, at `0.62` | 27 / 42 / 35 / 40 — the loosest floor that clears all four |
| Field inverts the ramp, at `0.65` | **34 / 47 / 41 / 44** — chosen |

`0.65` is taken over the marginal `0.62` because it buys 4 to 16 points of margin on top.
**Polarity clears the mark by that margin in every theme**, so `MinWallAlpha` survives
untouched.

**Measured on the running window, this is not only a white-wallpaper argument.** At 45% over a
real photograph the acrylic composite behind the wall back-solves to `#8E6251` under the rock
and `#9B827D` under the sky. At the pane tier the field lands at luminance **0.020–0.024**,
under the dormant capsule's **0.031** and under the rail beside it at **0.036**. At the old
chrome tier's reach the same field would land at **0.033–0.045**: above the dormant capsule,
and level with or above the rail, losing both invariants at once on an ordinary desktop.

**The Appearance screen prints both numbers** — how much of the window's ground is desktop, and
how much of a pane — in Plex Mono `tnum`, so the relation is visible rather than asserted. It
is a ratio and not a second slider on purpose: two percentages on one screen that mean
different things is a worse screen than one quantity with a stated relation.

Both preferences persist beside theme and transparency, under `appearance.backdrop`
(`acrylic` / `mica`, unset reads as acrylic) and `appearance.wall` (unset reads as *off*).

### 14.7 The panes take one ramp, and the fields take none

**An input field is a child of the pane it sits in, so the two alphas stack.** The identity in
§14.2 is what governs, and the constant is whatever the identity gives once you say honestly
which surface the field is drawn on. Both fields in the window are cut into panes, so both
solve to zero: **past the ink ramp a field paints no fill at all.** `ThemeContrastTests` asserts
the identity rather than the constant, so retuning either end of the slider cannot leave a
stale number behind.

**A field follows the wall's setting rather than the slider's.** With the art field solid the
pane under it is solid, the identity is vacuous — nothing is being admitted for the field to
match — and a field that faded anyway would lose its step for no gain.

**A field cut into an un-walked ground must be un-walked too**, or the step between them changes
size across the slider. `PaneGround` does not walk at all: it is the theme's own `Ground` at an
alpha, at every position.

**A field is found by its border, and lit by its ring.** Over a dark desktop the fill and the
pane converge — 1.05:1 at the far end — so a tone one step from another tone over the same
backdrop was never what drew a field. `Line` draws it and `Volt` says it has the caret. Focus
is §10.7's brush swap on a border whose thickness never changes; thickening it would reflow the
whole command bar every time the caret landed. The ring clears AA on the field to
**89 / 94 / 91 / 100** per cent of the slider against white.

**The pane's ink does not walk, and it must not.** A chrome ink ramp was a compensation for a
tier that opened to 70% and paid for it with a darker ink. `TranslucentSurface` is *below*
`Ground` in three of the four themes, so at the alpha the rail now shares with the art field a
walked rail would sink under the field beside it: measured over white, the walked rail is at or
below the wall at **87 to 89 of the 101 slider positions** in Winnow, Nightshift and Tungsten;
the unwalked one at none of them, in any theme.

§14.2's recess — the art hangs *below* the chrome — is therefore carried by the **ink**:
`Surface` over `Ground`, both unwalked, at one shared alpha, in every theme at every position.
`TranslucentSurface` stays on the record and in the theme format so that no user theme needs
editing; nothing reads it.

**Three surfaces do not open at all.** `TileGround`, because §14.4 is construction. The
popovers, because a flyout is its own popup root. And **polarity does not reach the panes**:
the merge queue is the only pane that shows cover art, it shows it inside an opaque
`Border.card`, and it applies no dormancy ramp, because the question there is identity and not
recency.

### 14.8 The honest costs

**Two panes at the same tier can still be in different states.** `appearance.wall` gates the
art field and the screens beside it, while the rail and the filter panel follow the slider
alone, so with the reach off you get a translucent rail beside a solid library pane. It is not
fixed here because the alternative is worse: the flush layout has no visible ground, so gating
the side panes on the reach setting too would leave a fresh install with transparency up
showing nothing translucent but a 36px lip. The Appearance screen says what the setting does in
words instead.

**The typed text in the filter panel's fields runs out at 96% and 97%** on Winnow and Box art,
four points short of holding AA across the whole slider, because the panel's field paints no
fill at all and the ink under the caret sits on the panel's own `Surface` rather than on a
`Ground` step cut into it. Four points at the very top of the track, on a pure white wallpaper,
three times past the mark. The only fill that would buy it back is one that makes the field
less open than the pane around it.

**The caption gives up seven points of its own range in the floating layout** — 38% to 31% on
Nightshift. That is the trade a ground and a caption that are one field buy, at the price of the
caption being measured on the most open surface in the window.

---

## 15. The floating layout

**A second arrangement, behind a setting, default off.** The panes may meet edge to edge as
they always have, or the **content** regions may detach into rounded cards with a uniform gap
around each, on a window ground that runs unbroken behind the caption and every gap.

**It is structure, and the two settings it sits beside are not.** §14's theme is *material* and
its slider is *quantity*. This is neither: it applies in every theme at every position on the
slider including `SOLID`, and it changes no colour that either of those two was measured
against.

### 15.1 What floats, and what stays flush

**The line is content against chrome, not big against small.**

| Region | Floating | Why |
|---|---|---|
| **Caption** | Flush, full width | Chrome. It is a lip, not a pane (§9) |
| **Command bar** | Inside the library card | It operates the library and nothing else, so it is its header |
| **Cut bar** | Inside the library card | Same rule |
| **Rail** | **Card** | Content — the feed, bucket and list axis |
| **Cover wall / list view / empty state** | **Card** | Content |
| **Merge queue · Stores · Appearance** | **Card** | Content; they replace the library pane and take its island |
| **Filter panel** | **Card** | Content, and a peer of the rail (§11.1) |
| **Detail modal** | Full bleed | A modal covers everything, gaps included |

**The command bar and the cut bar are the library pane's header in *both* layouts**, because
which pane a control belongs to is a fact about what the control does and not about whether the
panes are inset. Search, layout, density, display, sort and `Filters` all act on the library
and on nothing else. Three things follow:

- **One top edge.** The rail, the library and the filter panel all begin on the same scanline
  immediately under the caption.
- **The caption is a lip**, which is all §9 asks it to be. It is the only strip left on the
  window ground.
- **A visibility rule became a fact of composition.** The merge queue, Stores and Appearance
  replace the library *specifically* so they do not sit under a command bar whose search and
  sort mean nothing to them. The bar is inside the library's own `Border`, so no arrangement of
  those four panes can put a settings screen under the library's controls, and there is no
  parallel `IsVisible` rule left to keep in step.

The cut bar sits under the command bar and above the art, so the order reads downwards as
cause, claim, consequence: the controls, then what they did, then the result. **Both bars keep
their 1px rule in both layouts**, because in both layouts there is art directly under them.

`Filters` toggles a sibling island from inside the library pane. Slightly odd, and left alone:
the panel's own edge is directly under the toggle either way.

### 15.2 The ground the panes lie on

**`ShellGround` is inked `Well`** — the tone §9 keeps for where a tone under the art field is
still the point, joining the scrollbar track and the modal scrim in a third use that is the
same use. The deepest tone is the right one for the space behind everything, and it is what
makes a gap read as a recess rather than as a missing pane.

The caption takes no fill at all in this layout, so this ground is what shows through it: the
caption and every gap are one *surface* at every position on the slider, not two that agree.

The order that follows is one direction and holds in every theme:

```
Well  <  Ground  <  Surface
gap      the art    the chrome
         field      panes
```

**§5.1's polarity is untouched.** The wall island is `WallGround` exactly as in the flush
layout, and the capsules sit on exactly the field they were calibrated against. What is new is
that the field now has something *below* it, which is a fact about the gaps and about nothing
else.

### 15.3 Geometry: 8 and 8, and why each

**The gap is 8px.** It is §4's own spacing step, it is the smallest step on that scale that
reads as a gap rather than as a badly-drawn rule at 100% scaling, and — the part that decides
it — **it is exactly the width of the resize band §9.1 measures.** One number solves two
problems: a pane inset by it is a pane none of whose controls the OS can eat.

**One pane owns each gap.** Half the gutter from each of two neighbours is wrong for a reason
that shows up in exactly one state: the filter panel is not always open, so a library pane
carrying half a gutter on its right came out with four pixels between it and the window edge
whenever the panel was closed. So the rail gives up its right margin, the library pane owns
both of its own gutters, and the filter panel gives up its left one. **Every gap is 8 in every
state, and no gap is the sum of two margins that can go out of step.**

**The radius is 8px**, above the tile's 6 and the control's 4. Radius reads as a proportion of
the corner it turns: 6px on a 750px-tall column is a chamfer, not a round.

**The rail's column becomes `Auto` with the pane carrying its own 220.** Taking the margins out
of a fixed 220 column would take them out of the rail's content, so every label in the rail
would move when the layout changed. The column widens; the rail does not narrow.

### 15.4 §9.1 is retired here

See §9.1: `ScrollBarEdgeInset` is about which edge a control is on, and floating moves every one
of these scrollbars onto a pane's edge rather than the window's. **It is dropped under this
layout and kept under the other**, which is the rule doing what it says rather than an exception
to it.

### 15.5 Where the setting lives

Its own `LAYOUT` section on the Appearance screen, **under THEME and above TRANSPARENCY.**

The two controls under TRANSPARENCY are *qualifiers*: they are meaningless at `SOLID`, which is
why they are not drawn there at all. Layout is not a qualifier of anything, so a fourth row
inside that card would say it depended on a quantity it does not depend on. Structure is also
what you read first.

**It is drawn the way THEME is drawn and not the way the qualifiers are.** A qualifier is a
*consequence* and the honest way to show a consequence is to say it in a sentence; a layout is a
*shape*, with no colour and no number in it, so **the miniature is not an illustration of the
setting — it is the setting at 1/8 scale.** Two cards, one template, and exactly the four values
the layout changes bound out of the view model: the ground, the margin, the radius, and where
`Line` falls.

**A layout card is repainted from whichever theme is up.** A theme card draws its own fixed
palette, because four of them side by side ask *which room*; two layout cards ask *what would
this arrangement look like in the room I am already in*, and a card frozen in the default
palette would answer a question nobody asked.

Persisted under `appearance.layout` (`flush` / `floating`; unset reads as flush). The debug
capture flag is `--layout=flush|floating`, session-only and sealed against writing.

### 15.6 What it moves

**The layout moves two tokens, `ShellGround` and `CaptionFill`.** `FloatingLayoutTests` asserts
every other token is bit-for-bit identical between the two layouts, at every position on the
slider, with the wall in and out.

The two layouts no longer fail in the same place, so **`Colorimetry.AaCeiling` walks both and
reports the worse** (§14.3). The polarity floor and the dormancy ramp are layout-free.

### 15.7 The honest costs

Two, and neither is fatal:

- **The gap tone does almost no work under the library pane.** Measured, `Well`-against-`Ground`
  comes out at 1.13:1 in Winnow and 1.02–1.06:1 in the other three, so what makes the wall
  island float is its **1px `Line` border**, not the gap. Against the rail the gap does read on
  its own (1.28:1 in Winnow, 1.29:1 in Box art) because `Surface` is two steps up. Fixing it
  would mean lifting `Ground`, which is the tone §5.1's polarity is calibrated against, so it is
  stated rather than fixed. **Tungsten is the weakest of the four**: second-faintest gap tone
  and, by design, the faintest `Line` in the set at 1.58:1 against the gap. Nightshift and Box
  art draw loud lines and come off best.
- **§11.1's rule across the window is three collinear segments rather than one.** Floating
  breaks continuations by construction; that is what a gap is. The filter panel takes the
  *rail's* top margin rather than the wall's, which puts its header rule and the command bar's
  own rule inside the library card on the same scanline, y=92. So a header rule still meets a
  header rule, at the same height, under two headers of the same 48px — a continuation in
  everything except the 8px the gaps take out of it.
