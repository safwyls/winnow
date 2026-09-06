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
| `Danger` | `#E04B45` | Destructive affordance: the window close button's hover fill, and the confirm button on any destructive act (§12.3, §16) |

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

### Prose measure

`ProseMeasure` = 410px in `tokens.axaml`. 66 characters of Plus Jakarta Sans Body 13/18 and
72 characters of the paragraph body at 12/18 — both inside the 45-75 character band a reading
measure is drawn from, so one number serves both prose sizes. Measured against the
repository's own font files, shaped by Skia: 66 characters at Body 13 measure 410px, 45
characters at Body 13 measure 275px, 66 characters at Body 12 measure 379px.

The `.prose` class applies it: `MaxWidth` from the token, `HorizontalAlignment="Left"` so the
maximum does not centre the paragraph away from the column's left edge under the default
Stretch, and `TextWrapping="Wrap"`. It governs a prose run — a paragraph the reader reads. It
does not govern the merge card's 840px (§6), which is a two-column comparison width, and it
does not govern the Stores panel's 720px, which is a card width holding controls and rows.

---

## 4. Layout

**Grid is the default view. List is a toggle**, remembered per-session.

```
┌──────────────┬────────────────────────────────────────────────────────┐
│  RAIL 220px  │  search           ▦ ▤   density ──○──   sort ▾         │
│              ├────────────────────────────────────────────────────────┤
│  1,247       │   ┌────┐  ┌────┐  ┌────┐  ┌────┐  ┌────┐  ┌────┐       │
│              │   │    │● │    │  │▓▓▓▓│  │    │● │▓▓▓▓│  │    │       │
│  ● Patched 94│   │    │  │    │  │▓▓▓▓│  │    │  │▓▓▓▓│  │    │       │
│    Never 412 │   └────┘  └────┘  └────┘  └────┘  └────┘  └────┘       │
│    Started186│    vivid   vivid   faded   vivid   faded   vivid       │
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

Bottom-aligned gradient scrim to `Ground` at 92%. Title in Body L, playtime and
idle time in Data S. A single primary action, `Play`, in `Volt`.

**Stores are a chip row**, one chip per store the game is owned on, unchanged in appearance for
a single-store tile. Chips always occupy their own row below the stats. The stat text wraps
to at most two lines with ellipsis and a full-text tooltip, so it cannot paint over a chip at
the density floor. A multi-store tile additionally carries a compact one-letter-per-store
mark at rest on the front, which fades out over 140ms as the overlay rises, exactly as the
baked placeholder title does. The chips are the one "where you own it" fact, drawn once, so
the four-fact cap is not breached. The resting mark uses initials because the density slider's
floor is 108px and a row of word-chips is wider than the tile there; the words are reachable on
hover, on the back face, in the modal and in the automation name, which satisfies §8's
decorative-redundant rule.

**A tile that folded an expansion carries a second resting mark**, bottom-right, in the same pip
as the store mark and with the same fade: a plus and a count. It is drawn only while the library
grid's expansion-grouping preference is on, which is off by default. It exists because folding a
pack removes its own tile and with it its place on the Never played rail, so the base game states
what the fold took away; the words, including whether one of the folded packs has never been
played, are in the tooltip and the automation name. Neither resting mark is a fact of the hover
overlay, so the four-fact cap counts neither. Never `Flare`, which marks unread and nothing else,
and never `Volt`, which is selection.

**Do not show more than four facts.** The tile is a decision surface, not a detail view.

**The back's actions stay pinned below its facts.** At the 108px density floor, a wrapped
title and multiple store chips can exceed the space above Play/Install, Add to list and
Details. The facts therefore scroll inside that remaining space, with the inner scrollbar
gutter; they cannot draw over the buttons or intercept their clicks. Buttons and the facts
scrollbar own repeated presses. The cover's double-click gesture applies only outside those
controls.

**The flip keeps the back's hit targets still.** The front squashes over 80ms, then the back
fades in over 80ms at its final size. Interactive back controls never scale during the turn;
a click near a visible button edge must hit that button while it is appearing. Reduced motion
snaps both faces.

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

### 5.5 Art-backed surfaces

**The tile's back face and the detail modal lay the game's own art behind their
information.** Both surfaces used to be flat `Surface`, so a game lost its identity the moment
the user turned it over or opened it.

**The construction is identical on both.** Opaque `Surface` at the bottom, then the game's art,
then a veil — `ArtVeil`, the theme's own `Surface` at `ArtVeilAlpha` — then the text. The opaque
base stops a half-decoded cover showing the window through the gap between the dormancy ramp's
two layers; the same argument §14.4 makes for `TileGround`.

**The veil IS `Surface`, and that is the whole trick.** Over the opaque `Surface` each surface
already paints, a veil of `Surface` composites back to `Surface`, bit-for-bit. A game with no
art draws no image and gets the flat treatment exactly — no tone step, and no layout shift,
because the art and the veil are siblings in a `Panel` and take no space of their own.

**`ArtVeilAlpha` is 0.92.** The art contributes 8%. **0.92 is the round step past the boundary,
walked rather than assumed.** Measured per theme, the worst text ink over the brightest cover art
could be (white), at the veil's own alpha:

| | Winnow | Nightshift | Tungsten | Box art |
|---|---|---|---|---|
| at 0.91 | **4.49:1** (TextDim) | 5.50 | 4.74 | 4.62 |
| at **0.92** | **4.62:1** (TextDim) | **5.69:1** (TextDim) | **4.93:1** (Amber) | **4.76:1** (TextDim) |

At 0.91 Winnow lands at 4.49:1, one hundredth under AA. Winnow is the theme that decides the
value, because it has the lightest `Surface` of the cool three. Tungsten is the one theme where
the binding ink is `Amber` rather than `TextDim`.

**Which inks are held to 4.5:1.** `Text`, `TextDim`, `Azure` and `Amber` — the four these two
surfaces set text in. `Flare` is excluded: on these surfaces it is a dot and never a word
(§5.2), and WCAG scores a non-text component against 3:1, not 4.5:1. The hovered update row is
measured too: `SurfaceRaisedFaint` is the one veil that sits between the ink and the field
rather than replacing it, and it moves the worst figure by at most 0.07. Every other hover and
focus fill is opaque `SurfaceRaised`, which covers the art entirely and is already held to §8's
floor.

**Why the proof is exhaustive.** The art is walked as 256 greys. Each channel of the composite
is monotone in the art's own channel, and WCAG relative luminance is monotone in the channels,
so black and white bracket the composite's luminance and the greys hit every value in between.
The walk runs at every whole percent of the transparency slider; `Surface` never carries alpha,
so the veil never walks with it, and `TextDim` brightening under the ink ramp only improves the
figure. Slider zero is the worst case.

**Both surfaces reuse existing image paths.** The tile's back face binds the same
`CoverPresenter.Floor` and `CoverPresenter.Vivid` bitmaps the front face draws, from the same
presenter, at the same `DisplayAlpha`, with §5.1's 140ms restore and the same reduced-motion
snap. One image path, one lease, one decode: the cover wall's memory bound is untouched. The
modal binds `GameDetailsViewModel.Cover`, the 200px bitmap it already asks the cover cache for
at full saturation — §10's rule, that the ramp is a scanning aid and the user has finished
scanning. That bitmap is upscaled to a card up to 1582px wide, and the upscale is what softens
it; Avalonia's effect pipeline is closed (§5.4) and nothing here needs it to be open.

**A user theme can break this.** `ThemeAudit` warns when `Colorimetry.WorstArtBackedContrast`
falls under AA, against `seeds.surface`, because it is the theme's own `Surface` that decides
how much art gets through. It warns and never refuses, like every other check there.

---

## 6. Components

**Rail bucket.** Display S name, Data count. Selected: `ChromeRaised` fill, 2px `Volt` left
edge. The `Patched` bucket is the only one carrying a `Flare` dot next to its count.
Zero-count buckets render at 40% opacity rather than hiding, so the rail never reflows.

**List view.** Every row carries a 24×36 cover on its left edge, drawn from the same cache and
the same two-layer dormancy ramp as the wall tile. The cover takes the resting ramp
(`DormancyAlpha`) rather than the hover-restored one (`DisplayAlpha`), because the list's hover
affordance is the row's `ChromeRaisedHalf` veil; a cover that also woke under the pointer would
be a second hover language on one row. A game with no cover gets the grid's placeholder
gradient pair — floor under vivid — not a gap; the placeholder's baked Bricolage title is
omitted, because at 24px wide no title is legible and the row already names the game in Display
type beside the art. Corner radius is `RadiusControl`, not `RadiusTile`, on §4's rule that the
three radii rank by the size of the object they round. Title, store, playtime, idle, unread
dot. 44px rows on `PaneGround`, `Line` rules, `Volt` selection edge, and a column-header strip
on `ChromeSurface` — so the list has the same structure the grid does, a chrome bar above and a
field below. Row fills take `ChromeRaised` for a selection and `ChromeRaisedHalf` for a hover;
they must not take `SurfaceRaised`, which is an ink and would composite downwards over an open
field and invert the elevation (§14.2).

This is the power-user view — sortable columns, multi-select, bulk list assignment. Everything
the grid cannot do densely lives here, which is how the analytics capability stays available
without dominating the default experience.

**Merges.** The screen that proposes which library entries are one game and asks the user to
confirm each proposal and pick which entry becomes the header. The reference is
`docs/merge_queue_design/README.md`. Three bands: a 56px header (`Merges`, the pending count in
Data, `Sort ·`, `Accept N exact matches`, and the one filled button, `Merge N selected`); a 40px
cut bar on `ChromeSurface` with a six-segment kind filter, a cut chip while filtered, and the
count at the right, `14 → 6` while filtered, the only arrow in the interface; and the queue,
one scroll of five outlined sections, ACROSS STORES · EDITIONS · EXPANSIONS · PARTS · TEST
BUILDS, each with the count of its pending cards and a one-sentence blurb.

A proposal card is a composition on `Surface` with a 1px `Line` edge that turns
`VoltEdgeSoft` while checked: a header grid (checkbox, the header title in Bricolage at 15, the
unread dot, a confidence word on `SurfaceRaised`, the roll-up line in Data), one 64px candidate
row per entry (a 2px `Volt` edge and a `ChromeRaised` fill on the header row, `ChromeRaisedHalf`
under the pointer, a 34×51 cover under the tile gloss with the dormancy ramp, the title in
Bricolage at 13 followed by HEADER in `Volt`, NESTS UNDER in `TextFaint` or LEFT OUT in
`TextFaint`, a store chip, playtime and idle time in Data right-aligned, and the 8px unread
dot), and a reason slot on `SurfaceRaisedFaint` that shows the match's own reason in `TextDim`
and the hovered row's detail in `Text`. Two controls flank the row: a radio at its head makes
the row the header, and a checkbox at its end decides whether it joins the roll-up, so a group
that arrived with one wrong member is answered without refusing the rest. A row left out
recedes (title in `TextFaint`, cover at 40%) and its proposals with the linked rows are recorded
as answered no. Neither control is drawn where it would assert nothing: no radio on an
expansion card, whose base is the header by the shape of the relation, and no checkbox on the
header. Clicking the row itself opens the game's details, the library's own modal over the
pane, so the entries can be compared before answering. A row is a work, so an entry already
owned on two stores is one row wearing two chips. Resolved, the card collapses to a 44px strip:
`Volt` edge, the header title, `N entries · Nh · nested, nothing deleted`, and `Separate
again`. The strip stays in place so the list never reflows under the pointer.

Confidence is a word, never a score: EXACT MATCH in `Text`, LIKELY in `TextDim`, WORTH A LOOK in
`Amber`, the one Amber on the screen. Flare marks only the unread dots. The screen's two
transitions are the 140ms row-fill restore and the 120ms reason cross-fade. The answers on a
card are `Same game` / `Different games`. An answer writes a link act and deletes nothing. The
dock card at bottom-left reports what the last act did, carries Undo, dismisses itself after 7
seconds, and never takes focus. Past link acts appear as resolved strips in their section, so
the queue is the retraction surface for every relation and there is no history list.

Keyboard: Up and Down walk the candidate rows across every pending card, Space makes the row
the header, `S`/`Enter` answers Same game, `D` answers Different games, and `Escape` returns to
the library. The radio and the checkbox are Tab stops of their own.

**Session journal prompt.** 400×220 frameless, bottom-right, `SurfaceRaised`, with the game's
cover at 60×90 on the left. Title, duration in Data, one text field, 5-dot rating in `Volt`.
Appears at most once per session, never steals focus.

**Fetch status field.** In the rail's pinned bottom, above the settings gear, a `Well` field
with a `Volt` edge — the same pattern the Stores panel's `Border.note.working` uses. It names
what the enrichment pass has left to do as a real count that falls a slice at a time: the pass
reads the whole backlog before the first slice and commits in slices of 40, so every value the
field shows was true when it was written. Words only — no spinner, no animation, no
`Transitions` anywhere in it. That is the conforming answer to §8, not a shortcut: §8 says
the interface states what it is doing in words, in a status field, and motion may be added over
a status field but may never replace one. Because there is no motion, reduced motion has
nothing to disable and the surface is identical in both motion settings — an accessibility
floor with no branch in it cannot be got wrong. The field never appears on a warm library:
`EnrichAsync` returns early on an empty target list before it reports anything, so every launch
after the first shows nothing at all. There is deliberately no Cancel: §8 says "and offers
Cancel where there is one", and this pass is not something the user started; nothing can
restart it before the next launch, so a Cancel here would be a one-way stop dressed up as a
choice. It is cleared in a `finally`, so a run cut short by shutdown takes the field away
rather than leaving a stale count on screen. Exactly one of it for the whole window, because
it lives in the rail's grid rather than on any screen.

---

## 7. Copy

Plain and specific. The app knows something faintly embarrassing about the user — they own
1,247 games and have opened 412 of them zero times — and must never be smug about it.

**An explanation on screen is a short phrase.** A control carries a label and, where it earns
one, a single short tooltip — never a paragraph of rationale. What may run longer, because the
user cannot get it anywhere else: an error naming what failed and what to do; an empty state,
which must distinguish "nothing here yet" from "nothing matched"; consent copy, where the
button press is the consent; connection state; a stated default; the consequence of a
destructive act; and an accessibility or automation name (§8), which names the control and is
never trimmed for brevity. Reasoning goes in the design documents; where a paragraph on screen
carries one fact wrapped in reasoning, the fact stays and the reasoning moves here or to
`game-library-design.md`. Automation names name the control; they do not explain it.

| Context | Write | Don't write |
|---|---|---|
| Bucket: updates missed | `Patched` | `Needs attention` |
| Bucket: never opened | `Never played` | `Pile of shame` |
| Bucket: refund line to retired | `Started` | `Barely played`, `Bounced off` |
| Bucket: high playtime | `Played out` | `Completed` |
| Bucket: unrunnable | `Won't run` | `Dead` |
| Badge tooltip | `3 updates since you played` | `New content available!` |
| Journal prompt | `How was that?` | `Rate your session!` |
| Card answer | `Same game` / `Different games` | `Merge records` / `Cancel` |
| Merges header | `Merge 3 selected`, `Rolled up under Hades.` | `Confirm identity link` |

`Merge` is the screen's name and its bulk verb. The answer on a card is still `Same game`,
which asks about games rather than records.

**Empty states are directions, not moods.**

- Patched, empty: *"Nothing's been patched since you last played. This fills up on its own."*
- Never played, empty: *"You've played everything you own past the refund window. Genuinely rare."*
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
  unread badge is likewise backed by the rail's `Patched` count, by the same bucket name on the
  back of the tile, and by the tile's accessible name, which states the badge in words and
  gives the number of updates.
- **Focus is a brush swap on a border whose thickness never changes** (§10.7). It is drawn per
  control rather than left to Avalonia's global `FocusAdorner`, which measurably underdelivers
  and does not render inside a popup at all. Every focusable control carries a visible ring;
  `Volt` everywhere except on a `Volt` fill, where it is `VoltInk`.
- Full keyboard grid navigation: arrows, `/` to search, `Enter` to launch.
- **An accessible name belongs on a control that has an automation peer of its own** — in
  practice the `UserControl` root, the `Button`, the `TextBox`, the `CheckBox` — and never on a
  `Border`, a `Panel` or a `Grid`. Avalonia gives those a `NoneAutomationPeer` and Windows
  prunes it from the control view, the tree a screen reader walks; the element's children still
  appear, so what is lost is the name alone. Where there is no peer-bearing control to move the
  name to, the element states `AutomationProperties.AccessibilityView="Control"`, which is
  consulted before the peer's own answer and puts it back in that tree with its name intact.
  Verified against Avalonia 11.3.20.
- **A name on a `TextBlock` is discarded.** `TextBlockAutomationPeer` returns the `Text` and
  never reads `AutomationProperties.Name`, so a `TextBlock` says its `Text` and nothing else.
  Put the words in the `Text`.
- **A value that changes while its surface is on screen travels on
  `AutomationProperties.ItemStatus`, or on a bound `TextBlock`'s `Text`.** Changing a name at
  runtime raises no UIA event, so the new one is never announced; `ItemStatus` is the one
  attached property that raises one.
- **A count is spelled into the name string** — `Patched since you played: 3 updates.` —
  because `AutomationProperties.PositionInSet` and `SizeOfSet` compile and are read by nothing.
- **These four are enforced by a test rather than by review.** The failure is silent: the
  element keeps its children and nothing throws, so a name that stops arriving looks exactly
  like a name that does. `AutomationNameReachabilityTests` scans every `.axaml` under
  `src/Winnow.App` and fails when a name sits where UIA will drop it.
- **Composed controls name themselves explicitly.** A button containing a layout panel does
  not inherit the text drawn inside that panel. Rail navigation, list choices, filter options,
  theme cards and glyph buttons bind their visible label as their name; list and filter counts
  are included in words and also travel on `ItemStatus` when they change. Primary screens are
  named groups, screen titles are level-1 headings and section titles are level 2. The details
  surface itself has a `Window` role and a name identifying the game, because its focusable
  root must be named as well as its inner bands. `InteractiveControlNameTests` checks the
  authored controls; `docs/spikes/accessibility-navigation.ps1` exercises the Windows UIA
  provider on an isolated sample library.
- **Reduced motion disables the hover saturation animation** — state snaps instead of fading.
- **When the interface cannot state a proportion, it says what it is doing and what it is
  waiting for, in words, in a status field, and offers Cancel when there is one.** This is the
  Stores panel's pattern — a `Volt`-edged status field naming where to look, plus Cancel —
  generalised. §7 already says the same thing about the first-run grid: placeholder tiles with
  the title set in Bricolage on a `Surface` field, never a spinner, never an empty grid. When
  nothing is yet known, the surface says what it is waiting for; it never draws an empty box or
  a placeholder that claims a measurement it does not have. Because the indicator is words,
  reduced motion has nothing to disable and the surface is the same in both motion settings.
  An accessibility floor with no branch in it cannot be got wrong.
- **Motion may be added to a status field; a status field may never be replaced by motion.**
  Four conditions on any animated indeterminate indicator: (a) the words are the indicator and
  the motion is decoration over them, so a screen reader reads a state rather than nothing;
  (b) at most one moving element on a screen; (c) it is removed entirely under reduced motion,
  leaving the words — removed by a style, never present as a local `Transitions` value, which
  is §12.5's rule; (d) it is never the only thing saying that work is happening.
- **A determinate indicator stays shown and stays accurate under reduced motion** — it is
  information, not decoration. What goes is the continuous movement: values step at a coarse
  cadence rather than updating thirty times a second, and the transition that smoothed them is
  removed by a style, never present as a local value (§12.5).
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
│  200×300   │  IGDB USERS 78 / 1,204 · STEAM 91% / 41,203    │
│            │  [STEAM] [Patched] [Not installed]               │
│            │                                                  │
│            │  37h    SINCE YOU PLAYED               9y 7mo    │  2 MY HISTORY
│            │  PLAYED ┆▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁┤     │    lifetime axis
│            │         Dec 2015                       today     │
│            │                                                  │
│            │  [Install] [Store page] [All patch notes] [More] │  3 GET ME IN
│ STEAM APPID├──────────────────────────────────────────────────┤
│ 383120     │  UPDATES                              (scrolls)  │  4 THE REST
│ ON DISK    │  ● v1.19.2 Patch        11 Aug 2026  Patch notes │
│ C:\…       │  ABOUT                                           │
│ ACQUIRED   │  …text…   [thumb] [thumb] [thumb] →              │
│ 4 Nov 2016 │  ALSO COVERS · EXPANSIONS · LISTS                │
│ Gift       │                                                  │
└────────────┴──────────────────────────────────────────────────┘
```

**Two columns, split by what they are about rather than by where the art fit.** Left is the
object: its art, the id Steam calls it, where it lives on disk. Right is your relationship with
it. The divider spans the right column only, because the left one keeps going. That split also
fills the ~130px of nothing a game with no last-played date used to leave beside a 300px cover,
which read as broken rather than as sparse.

**The card scales against the window, not the display.** Two named `ScaledLength` resources
at the top of the view — `CardWidthCap` and `CardHeightCap` — each bound to
`$parent[Window].Bounds`. A `ScaledLength` carries a `Fraction`, a `Least` floor and an
optional `Most` ceiling, so each cap is one named object rather than a number buried in a
layout attribute. Both are unit-tested at seven window sizes
(`tests/Winnow.Tests/DetailsModalScaleTests.cs`).

- `MinWidth` = 700, unchanged. `Margin` = 40, unchanged.
- `MaxWidth` = half the window's width, never below 860, never above 1582.
- `MaxHeight` = two-thirds of the window's height, never below 720. No ceiling.

The two floors are exactly what the card had when it carried fixed caps, so no window size the
app allows (its own minimum is 1200x640) produces a smaller card than shipped. The card is
content-sized between its floor and its cap: it is only as wide as its content asks for within
that range. Why the window and not the display: the window is what the user sized, and a
display-relative card would overflow a small window on a large screen. Why `Window.Bounds`:
they never depend on what is inside the window, so a cap bound to them cannot feed back into
layout, while a cap bound to the modal's own host could.

**1582 is retained rather than derived.** It was the card width at which the in-modal
screenshot hero reached the native 1280x720 of IGDB's `t_screenshot_huge`; that hero has moved
to the lightbox (§10.7), which is sized against the window rather than against the card, so the
number no longer follows from anything inside the card. Nothing else in the card rewards more
width: the left column is a fixed 200px, and prose is bounded by the reading measure (§3). A
future pass wanting a different ceiling would have to measure a new reason for one; leaving the
number where it is costs nothing and re-deriving it buys nothing. See
`docs/spikes/details-modal-scale.md` for the measurements that produced it.

**There is deliberately no height ceiling.** Nothing in the card has a native height that
stops rewarding growth: the rest band and the left column are bounded scroll regions, so more
height is more content on screen rather than more empty card.

**What a window produces:**

| window | card cap | right column |
|---|---|---|
| 1200x640 (the app's own minimum) | 860 x 720 | 580 |
| 1280x820 (default) | 860 x 720 | 580 |
| 1600x900 | 860 x 720 | 580 |
| 1920x1080 | 960 x 720 | 680 |
| 2560x1440 | 1280 x 960 | 1000 |
| 3440x1440 | 1582 x 960 | 1302 |
| 3840x2160 | 1582 x 1441 | 1302 |

**What the right column's width means for the next addition.** The right column is 420px at
the card's `MinWidth` and never narrower. That is the width every measurement in this section
and in §10.3 was taken against. A control or a row added to the right column must still fit
420px and may assume nothing wider. Above the minimum the column is 580px at the width floor
and 1302px at the ceiling. The reception line is one row at 580px and at every width above it,
and two rows at 420px, so the `WrapPanel` is still the right panel and no figure changes.

**Each column's lower part is a bounded scroll region.** The right column scrolls the rest band;
the left column scrolls the facts under the cover. Both sit in star rows so each is bounded by
whatever height the card has and scrolls inside it. An Auto row is measured against infinity, so
a ScrollViewer inside one takes its content's full height and never scrolls — that is what let
long content draw past the card and be cut off at the window edge (measured on Avalonia 11.3.20).

Avalonia's Fluent ScrollViewer draws its scrollbar over the content while auto-hide is on: the
content presenter is given both spans, so the bar takes no column of its own (verified against
Avalonia 11.3.20's own theme). Four inner scroll regions in the modal carry the same
problem. In the right column's rest
band, the close glyph on each disclosed section's header row (§10.9, §10.10) and the per-row
Separate, Ungroup and Patch notes buttons sit under the bar. In the IGDB candidate list
(§10.9), a bounded region with a bar of its own drawn inside the rest band's content, the
swelled 12px track covered roughly 8px of each row's assign control behind a 4px content
margin; its gutter does not double-count against the rest band's, because the two regions
scroll independently. In the left column, a wrapped ON DISK path could run under its bar. In
the screenshot thumbnail strip in ABOUT, a horizontal bar is drawn over the foot of the strip
rather than over its trailing edge. The three vertical regions carry the same 20px right margin
on their content; the token is `InnerScrollGutter`. The horizontal strip carries the same 20px
as a bottom margin; the token is `InnerScrollGutterBottom`. 20
is 12, the width Fluent's track swells to under the pointer, plus 8, §4's own spacing step —
the 8 is what makes the clearance read as deliberate space rather than as a control that merely
stopped touching the bar. In the rest band the gutter is one margin on the band's content
rather than one per header, so every section the band carries now and every section added later
is clear of the bar without solving it again. The left column's 18px top margin — the gap
between the cover and the facts — moved onto the ScrollViewer itself, because a `Thickness`
token cannot be composed with a second value in XAML and a `Margin` on a ScrollViewer is not
the inert `Padding` case; the gap no longer scrolls away with the content. It is not carried
by the `ScrollViewer.inner` class: that class is §9.1's
opt-out, saying this scrollbar's edge is a divider of ours rather than the window's, and the
only property it could set for a gutter is the ScrollViewer's own `Padding`, which
`ScrollContentPresenter` ignores in measure and in arrange. The cover wall already records the
same finding by setting `Padding="0"` and clearing the bar with the wall's own margin; a
class-level style reaching the content instead would lose to the local `Margin` values that
content already carries, which is worse than an explicit margin because it would fail silently
on exactly the regions that need it. The modal's own close button, beside the title in Band 1,
is outside the scroll region and needs none of this.

**Below the year and publisher, the identity block carries a reception line.** Up to three
attributed figures, in this order: IGDB's own user rating, IGDB's aggregation of external
critics, and Steam's review summary. Each draws a short uppercase source attribution, the
value, and the count of people behind it — IGDB USERS 78 / 1,204 ratings; IGDB CRITICS 85 /
42 critic scores; STEAM 91% / 41,203 reviews. The count is on the line, not only in the
tooltip, because a 9 from four people and a 9 from four thousand are different claims. The
three are never blended and Winnow computes no verdict of its own: they are three populations
answering three different questions. Steam's own words ("Very Positive") are not on the line;
they arrive with the percentage and the count on hover. A source with no figure writes no row
in `work_ratings`, so it contributes nothing, and when no source has a figure the line is not
drawn at all — never a zero, never an empty scale. The value takes `Text` and the attribution
and count take `TextDim`. `TextFaint` is not available here for the reason §10.3 already gives
for section headings. The line is a `WrapPanel`: measured, the three figures sum to 564px and
the right column is 420px at the card's `MinWidth`, so the line takes a second row at that
width and a single row at 580px (`docs/spikes/details-modal-additions-width.md`). A second row
is cheaper than trimming a count away.

**The rest band's order is:** corrections (IGDB MATCH, EDIT DETAILS), UPDATES, ABOUT with
screenshots inside it, ALSO COVERS, EXTENDS, EXPANSIONS, LISTS. The governing rule: **a
`label section` heading in Band 4 is earned by a list of rows the user can act on. ABOUT is
the single prose exception. A fact about the game goes in Band 1; a fact about this copy goes
in the left column; a picture goes inside ABOUT; an act goes in the More menu with its status
on the strip.** UPDATES moved to the top of the band because it is what Band 2's axis
summarises and the two should be adjacent. Its heading is the constant `UPDATES`, whether or
not anything landed since the last session. It previously took `SINCE YOU PLAYED` in one state
and `UPDATE HISTORY` in the other, and the first of those is Band 2's own rail label, so one
modal said the same words about two different things; the rail keeps the name.

**Screenshots sit inside ABOUT, not in a section of their own.** A horizontal thumbnail strip
at 120x68, with a caption naming the count and the source. Picking a thumbnail opens the
lightbox (§10.7). Each thumbnail is a real `Button`, so it is a Tab stop with the panel's drawn
ring, and a thumbnail reached by Tab is scrolled into view — the same arrangement the IGDB
candidate list (§10.9) uses. A game with no screenshots draws nothing at all, never an empty
frame; that is a property of the data: no ids in `work_images` means no view model. The images
ride the existing cover cache under a `CoverKey.IgdbScreenshot`, which resolves to
`t_screenshot_huge` — an IGDB cover is 3:4 and a screenshot is 16:9, so the provider is what
picks the size token; there is no second image path. The strip is a bounded horizontal scroll
region, the fourth such region in the modal.

**Accessibility: the modal's own tree.** Band 1, Band 2, Band 3 and the reception line are
named groups — `AutomationProperties.Name` plus `AccessibilityView="Control"`, which is what
un-prunes a panel whose own peer reports itself out of the control view;
`AutomationControlType.None` maps to the UIA Group type. The title is a level-1 heading and
every section heading is level 2, through `AutomationProperties.HeadingLevel`. Update rows and
screenshot thumbnails carry `ControlTypeOverride="ListItem"` on the DataTemplate root — never
on the ItemsControl, whose containers are ContentPresenters that report themselves out of the
control view, so a list addressed at the control reports no items (§8). An update row's name
states in words whether it landed since the last session, because the `Flare` dot is a mark
and §8's decorative-redundant rule wants the same fact as text. `AutomationProperties.Name` is
never placed on a `TextBlock`: its peer ignores the property and returns `Text` instead (§8).
All four attached properties — `AccessibilityView`, `HeadingLevel`, `ControlTypeOverride` and
`LiveSetting` — were verified wired to Windows UIA in the Avalonia 11.3.20 source.

### 10.2 The lifetime axis

**The one thing Winnow can draw that nothing else can.** Storefronts hold your playtime and
they hold a game's patch history; nobody puts them on the same axis. For a game with a release
year and at least two month-end playtime readings, Band 2 draws one time axis from the game's
release to today, in two zones.

- **The left zone is play whose amount Winnow knows and whose shape it does not.**
  `SteamPlaytimeBackfillService` reconstructs a month-end cumulative series from Steam Replay,
  and everything before the first covered month is stamped as one figure at one instant — the
  floor point in `PlaytimeSeriesReconstruction.cs`. It is drawn as a flat band with a dashed
  boundary, never as bars and never as a slope, because a slope across that span would invent a
  month-by-month pattern nobody measured.
- **The right zone is one bar per closed month.** A bar spans the true time between two
  consecutive readings and its height is the play gained between them, so a stretch the backfill
  did not cover draws as one wide bar carrying the whole stretch's hours rather than being
  silently compressed into the ordinal sequence.
- **Only month-end readings are differenced.** A live snapshot is written only while Winnow is
  running, so a user who closes it for three weeks gets three weeks of accumulated play stamped
  on one instant; a chart from those deltas would draw a spike on the day the app reopened, not
  on the days the play happened. Snapshots that are not stamped at a month end contribute no
  bar at all.
- **Sessions are not mixed in.** They exist only from Winnow's own install and only for
  processes it watched, so overlaying them would make a game heavily played for four years
  before that install look dormant for those four years. Two data sets, two coverage windows,
  two questions.
- **The last session is a `Volt` stop mark on the axis.** Update marks are `Flare` on the
  baseline, capped at 14, the same signal the gap rail carried and placed on the whole axis
  rather than on the gap alone.
- **The bars ride §5.1's ramp turned on its side**, `Line` at the release end to `Volt` at
  today. This runs the opposite way to the gap rail's own ramp: the gap rail encodes a
  dormancy that begins at a single known moment, the last session, so `Volt` sits there; the
  lifetime axis has no such single moment and encodes recency, so `Volt` sits at today. The
  gap rail's rule is unchanged where the gap rail still draws.
- **What is drawn is the user's own hours.** Per-game player-population activity is not
  obtainable — the whole finding is in `docs/spikes/activity-graph-data-availability.md` — and
  the copy under the axis says whose hours these are so the reader cannot mistake it for a
  population curve.
- **Everything it draws is restated in words underneath** (§8). A user who cannot resolve a 7px
  dot or a 3px bar loses nothing.

**Four states.**

1. Measured months exist. The axis draws both zones, the bars carry the ramp, and the copy
   states the user's own hours and their coverage.
2. Every hour predates the record and the measured months are all zero. The flat band fills
   the axis, the bars are empty, and the copy says so.
3. No release year, or fewer than two month-end readings. The shipped gap rail draws unchanged:
   normalised from the last session to today, `Volt` at the last-played end fading to `Line` at
   today, with update marks in `Flare`, capped at 14, and the span stated as a number beside
   it. The rail is normalised, never scaled to duration — a 14-day gap and a 9-year gap draw
   the same length — because scaling would be a second, competing encoding of a fact the digits
   already carry. The record sentence still stands: *"Checked 12 times since 23 Aug 2026 — up
   1h 7m."* The delta is between the first and last reading Winnow holds, which is the part it
   actually watched happen, not the total Steam already knew. At one reading it says so; at
   zero it says nothing at all.
4. No last-played date at all. The sentence is the whole of Band 2 and there is no rail of any
   kind. Two different absences, kept apart by the copy: *"You've never opened this."* and
   *"Steam has no date for your last session."*

**The axis starts at 1 January of `works.first_release_year`**, and the axis's left label is
that year. Winnow stores a release year, not a release date, so the axis cannot start at a
month and does not pretend to. A series or a last session that predates the stated year extends
the axis back to that month rather than being clipped off the left edge.

### 10.3 Getting in

`steam://run/<appid>` when the game is on disk, `steam://install/<appid>` when it is not — and
**the button is named for which one it is**, `Play` or `Install`. A button reading "Play" on an
uninstalled 60GB game promises something the next hour will not deliver. **No appid means no
primary action at all, never an inert button.**

Beside it, `Store page` and `All patch notes` in `Azure`, and the `More` control — four
controls on the strip for Steam. Other stores have fewer, because their launchers expose less:

| Store | Installed | Not installed | Links |
|---|---|---|---|
| Steam | `Play` — `steam://run/<appid>` | `Install` — `steam://install/<appid>` | `Store page`, `All patch notes` |
| GOG | `Play` — `goggalaxy://launchGame/gog_<id>` | `Install` — `goggalaxy://installationScreen/<id>` | `Store page` when cached, `Show in GOG Galaxy` |
| Epic | `Play` — `com.epicgames.launcher://apps/<ns>%3A<catalogItemId>%3A<appName>?action=launch&silent=true` | `Install` — `com.epicgames.launcher://apps/<ns>%3A<catalogItemId>%3A<appName>?action=install` | `Store page` when a slug is cached |

Neither Epic action is drawn unless Winnow holds all three ids — namespace, catalog item id and
artifact id.

**Epic's own protocol-activation documentation names the wrong verbs.** The working install
route is `?action=install`, verified by execution against Epic Games Launcher build 20.2.9 on
2026-09-05: the launcher refreshed the entitlement, resolved the catalog item, dispatched the
install and opened its own install-location selector; Epic does the downloading after the user
confirms there, not Winnow. That verb is undocumented. The documented `?action=installer` routes
to the optional-components screen for an already-installed app and is a no-op on an uninstalled
one; the documented `?action=updatecheck` is not registered at all in build 20.2.9 and is
rejected before dispatch. See `docs/spikes/store-actions-per-launcher.md` for the full evidence.
Epic's cached namespace-to-slug map supplies `https://store.epicgames.com/p/<slug>`; unresolved
namespaces draw no link. GOG's cached product response supplies its store-page URL and patch
notes. When notes exist, a collapsed `GOG patch notes` disclosure in the scrollable rest band
opens readable text; it is absent when the response has no changelog. The API and cache are
background work, so opening details never waits for either store.

**When a store entry has no primary action and no links, Band 3 says why** instead of showing
a strip whose only control is `More`. The sentence takes `Text`, not `TextDim`, on the same
reasoning as the no-rail sentence: it is the whole of what Band 3 says about getting in when
there is nothing to press. Two sentences, one per cause: the install state was never read, or
the store's identifier is not held.

`More` opens a menu whose rows are `Open folder`, `Refetch metadata`, `Wrong game?`,
`Edit details` and `Hide`, in that order. A row is drawn only when it has something to do:
`Open folder` when the game is on disk, `Refetch metadata` when enrichment services are
registered, `Wrong game?` and `Edit details` when their controls exist, `Hide` when the library
handed its command over. A row with nothing behind it is not drawn rather than drawn inert. The
trigger's face does not change — the menu owns whether it is open, so the button always reads
`More`. Its tooltip is `Folder, metadata, corrections and hide`.

**A row's name does not change with the state of what it opens.** The row opens; the surface
it opens carries its own close control — a `×` glyph in the trailing Auto column of the
section's header row, beside a heading that names the section. The close control's tooltip
names the section; it does not say "(Esc)", because Escape closes the whole modal, not a
section. Choosing a row whose section is already open scrolls the section into view rather
than closing it: closing is the close control's job. The close control returns focus to the
`More` trigger — the control the section was opened from, on the strip outside the rest band's
scroll region, so it is always drawn.

**A heading that names a section is set in `TextDim`**, the same ink as the `×` glyph beside
it, so the header reads as chrome rather than as the section's own content. The set is IGDB
MATCH, EDIT DETAILS, UPDATES, ABOUT, ALSO COVERS, EXTENDS, EXPANSIONS and LISTS.
A label that names a value — PLAYED and SINCE YOU PLAYED on the gap rail, STEAM APPID, ON
DISK, the coverage total's label, and the per-field labels in the editor — is a different thing
and is not in that set. The ink is stated at the heading by the class `label section`, declared
once in `tokens.axaml`, rather than left to `label`'s own default, so a section heading and the
close glyph beside it cannot drift apart if the value labels are ever retuned. `TextFaint` is
not available: §8 already says do not dim further, and the figures confirm it. `TextFaint`
measures 3.63 / 3.60 / 3.31 / 3.28 across Winnow / Nightshift / Tungsten / Box art on the
flat card, and 2.86 / 3.01 / 2.67 / 2.60 over the brightest cover the art-backed card of §5.5
can carry — under AA in every theme before the art is involved at all. `TextDim` measures
5.88 / 6.82 / 6.44 / 6.10 flat and 4.63 / 5.71 / 5.19 / 4.83 over that same brightest cover.
A heading at label size is 11px SemiBold, which is not WCAG large text (large text begins at
14pt bold), so the 4.5:1 bar applies and the 3:1 large-text allowance does not.
`ThemeContrastTests.The_section_heading_takes_the_quietest_ink_that_still_clears_AA` pins both
halves per theme.

**The complement: a run in this panel takes `Text` where it is prose and keeps `TextDim` where
it is a status line, a label on a value, or metadata.** The deciding line is what the run is,
not where it sits. A paragraph the user reads, a sentence standing in a prose slot, or a blurb
sitting under a heading is prose. A value label, a status line, a landed-act confirmation, or
metadata beside a value is not.

Ten runs take `Text`. The gap rail's caption and its longitudinal record line, and the same
record line in the no-rail branch — §10.2 says everything the rail draws is restated in words
underneath, and §8's decorative-redundant rule makes those words the carrier of the fact; the
carrier of a fact is primary text. The no-rail sentence itself ("You've never opened this." /
"Steam has no date for your last session."), which is the whole of what Band 2 says when there
is no rail. The EXTENDS blurb ("A separate game, grouped for display.") and the EXPANSIONS
blurb ("Counted separately. Not added above."), both directly under a section heading — the
pair that prompted the change. The LISTS empty state ("Select titles in the library and choose
Add to list on the action bar."), a direction the user acts on (§7). The ABOUT summary — the
game's own description — and the empty-body line that stands in the same slot ("No description
yet. Metadata fills in automatically."). The metadata editor's intro ("Each field tracks its
own source, so editing one leaves the rest alone."). The no-way-in sentence in Band 3
("Winnow has not read this copy's install state yet." / "Winnow does not yet hold the
identifier this store needs to reach this game."), which is the whole of what Band 3 says
about getting in when there is nothing to press. None carries a local `Foreground` override;
the ink comes from the `.body` type style, which §2 already gives `Text`. A prose run that
states no ink of its own is correct by default.

Status lines, landed-act confirmations, value labels and metadata keep `TextDim`. The IGDB
section's note holds either a standing pin label ("Matched by you.") or a confirmation that an
act landed ("Now using <name>.", "Linked with <name>."); the metadata editor's own note and its
per-row status lines are the same kind of string. The identity line's year and publisher, the
bucket and install-state chips, the coverage total's note and the candidate row's platform list
are metadata. §10.8's no-notes line and §10.9's no-results and id-miss lines are already pinned
by name elsewhere in this section.

Two runs are genuinely ambiguous and are left `TextDim` on stated reasoning rather than left to
a future reader's inspection. The provisional-title note ("Name not yet available. Showing the
app id until metadata loads.") is two sentences the user reads, but it reports the state of the
value above it and says so in its own copy — "until metadata loads" — which is the shape of a
status line, and it sits inside the identity block among the metadata that qualifies the title.
The two flag-control captions ("Removes from Patched. A newer patch puts it back." and "Marked
read. A newer patch will flag it again.") are each drawn beside their own button and repeated
verbatim as that button's tooltip, which makes each an annotation on a control rather than
prose of the panel's own.

`Text` on the flat card and over the brightest cover the art-backed card of §5.5 can carry, at
slider zero, in the order Winnow / Nightshift / Tungsten / Box art: 13.11 / 16.44 / 14.76 /
13.42 flat, 10.34 / 13.75 / 11.90 / 10.61 over art. Brightening prose cannot lose a figure
the label ink already held, in any theme, on either surface. `Text` is already one of the four
inks §5.5 holds to 4.5:1 and is already walked over all 256 greys at every slider position by
`ThemeContrastTests.Art_behind_the_back_face_and_the_modal_keeps_text_over_AA`;
`ThemeContrastTests.The_modal_prose_takes_the_primary_ink` pins the pair per theme so the prose
ink cannot drift back down.

**The disclosure is `Button.secondary`, not `Button.link`.** `Store page` and `All patch notes`
are outbound links and draw in `Azure`; the disclosure acts here rather than leaving, so it
takes the panel's `Text`-ink treatment. `Button.secondary` and `Button.link` have identical
geometry, so the choice costs no width.

**The menu's presenter wears the `actions` class** from `Themes/controls.axaml` — the same
treatment the library grid's context menu wears, one set of setters covering both. The modal
and the grid agree instead of being two grammars.

**The menu floats.** It is not in any row of the card's grid, so opening it reflows nothing:
Band 3's height does not change and the rest band is not pushed. A further action costs one
row of menu height and no width at all. Measured, the menu card is 134 x 103px at three rows
and 134 x 134px at four — about 31px per row, with no cost to the modal's layout at all.

**The mark on the row the keyboard is on is drawn inside the item template**, never by the
adorner layer (§10.7): one step of fill above the menu's own ground (`SurfaceHigh`) plus a
2px `Volt` edge on a border whose thickness never changes. It answers two states, because a
menu opened from a button puts focus on its first row without selecting it: `:selected` covers
the pointer and the arrow walk, `:focus` covers where the keyboard lands when the menu opens.
Measured, not assumed — `docs/spikes/details-action-band-menu.md`.

**What earns a place on the strip.** The band is "GET ME IN". A control belongs on the strip
only if pressing it moves the user toward playing this game now: the primary action, and the
outbound links that answer what this is and what changed before launching. Everything else
joins the menu. A new control joins the menu by default; putting one on the strip requires
both that it passes that test and that the strip is re-measured and still fits 420px. See
`docs/spikes/details-action-band-width.md` for the measurements.

**Keyboard.** Tab order on the strip follows declaration order (§10.7): primary action,
`Store page`, `All patch notes`, `More`. The five menu rows are never Tab stops; they are
reached by opening the menu. Opening it puts focus on the first row that is drawn, skipping
any that is not. Up and Down walk every drawn row in declaration order and wrap round rather
than dead-ending. Enter runs the row and closes the menu. Escape closes it. Both routes hand
focus back to the trigger.

**Refetch metadata is in the menu, not on the strip.** Re-asking a source moves nobody closer
to playing this game now, which is the strip's own test stated above. Its status is reported in
words on a line in Band 3, outside the rest band's bounded scroll region, so an act started
from the menu is answered where it can always be seen. The line is absent entirely at rest. It
is `TextDim` while running and after a result lands, and `Amber` for a refusal, which is the
register the rest of the panel already uses. Progress is words and never a percentage: no total
is knowable in advance. A refetch that wrote something reloads the library and reopens the
modal carrying its confirmation, the same arrangement §10.9 already describes for a landed
assignment and for the same reason — the reception line and the cover are computed when the
library loads.

**The refetch status field is a live region.** Changing an `AutomationProperties.Name` at
runtime raises no UIA event, but `TextBlockAutomationPeer` raises a Name change whenever `Text`
changes and `AutomationNode` turns that into a live-region event while `LiveSetting` is not
`Off` — verified against the Avalonia 11.3.20 source. The field is therefore a `TextBlock`
whose `Text` is bound and which sets no `AutomationProperties.Name` at all.

The folder goes through the launcher's directory entry point as a path, never a `file:` URI.

**Every outbound target is built by `GameLink.Create` and nothing else.** Five schemes are
allowed — `https`, `http`, `steam`, `com.epicgames.launcher` and `goggalaxy`, the three
launcher protocols plus the web — and everything else is refused, including the ones that look
harmless: `file:`, `javascript:`, `data:`, anything relative, anything carrying a control
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
| Provisional title | `Name not yet available. Showing the app id until metadata loads.` | *(nothing)* |
| No summary yet | `No description yet. Metadata fills in automatically.` | `No data` |
| No way in, unknown state | `Winnow has not read this copy's install state yet.` | `Install state unknown` |
| No way in, no id | `Winnow does not yet hold the identifier this store needs to reach this game.` | `Missing store id` |

Two of those are load-bearing. **"No updates recorded in that stretch"** and not "nothing has
shipped": update polling is staggered across days, so an empty rail can mean a quiet decade or
a turn that has not come round yet, and the interface may only claim the one it can support.
**"Checked"** and not "sampled" or "snapshotted": name the thing by what the person recognises,
not by the table it lives in.

### 10.5 What is absent, and why

The left column's ACQUIRED block draws the date and the licence type in words when the parser
recognises one — Steam Store, Complimentary, Gift or guest pass, Retail key. An unrecognised
licence says nothing rather than showing a stored token. It draws only for a user who has run
the saved-page import, and is absent rather than empty for everyone else. It is in the object
column because it is a fact about this copy — this ownership row — rather than about the game,
which is the same split §10.1 draws between the two columns.

**`price_paid_cents` is deliberately never bound in this modal.** §7's "never be smug" is the
reason: "$59.99 · never opened" is the sentence this product must not write. Price belongs to
the export and the account stats screen. `platform` and `edition_note` are empty for every row
Steam's local files produce and are not bound. `account_ref` is populated and still absent,
because showing a user their own Steam account id is noise.

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

**This panel has exactly one popup: the action band's menu (§10.3).** It is allowed because a
menu draws its own mark inside the item template and therefore never needed the adorner layer.
Everything else stays in the modal's own tree — the IGDB search and its candidate list
(§10.9), the per-field editor (§10.10), the list ticks — because those are surfaces to read
and type in, where a hand-drawn ring per control would be the whole cost of the surface.

**The screenshot lightbox is an overlay, not a popup.** §10.7's ban is on popups: a popup is
its own root with no adorner layer, so `FocusAdorner` draws nothing inside one and every ring
would have to be hand-drawn per control. The detail modal is not a popup either —
`MainWindow.axaml` hosts `GameDetailsView` as a child spanning all columns of the window's own
`Grid`, and that is exactly why its focus rings work. The lightbox is the same pattern one
layer up: an ordinary child of that same Grid, declared after the modal so it draws over it, in
the window's visual tree. The rings draw there for the same reason they draw in the modal, and
nothing is hand-drawn. The lightbox is therefore not a second exception alongside the action
band's menu. The menu is an exception because it is a popup that draws its own mark; the
lightbox needs no exception at all. A `Popup` or a `Flyout` here would be the mistake, and it
is held by a test (`tests/Winnow.Tests/Enforcement/ScreenshotLightboxStructureTests.cs`) rather
than by review, because the failure is silent — the rings would simply stop drawing.

**The ground.** The `ModalScrim` token, the same one the modal's own scrim takes, lying over
that scrim rather than replacing it. Two stacked passes of the theme's Well at 84% compose to
roughly 97%, which is what puts the library and the card out of the way. No second token, so
nothing new has to be kept in step across the four themes.

**The frame.** Capped at 1282 x 722: 1280 x 720 plus the 1px border on each side. 1280x720 is
the native size of IGDB's `t_screenshot_huge`, the rendition `CoverKey.IgdbScreenshot` resolves
to, so past it every pixel is upscale — the same argument that produced the card's own width
ceiling, now applied where it belongs. `Stretch="Uniform"`, so a window too small to give the
shot its native size shrinks the whole frame rather than cropping it; `UniformToFill` is the
crop the user reported and must not come back. There is deliberately no window fraction here:
the cap is the picture's own size, not a share of the window, because a full-window overlay has
room to spare from a very ordinary window upward.

The image and its controls share a centered container. Close overlays the top-right corner;
back and forward overlay the left and right edges, vertically centered, all inset by 12px.
Controls use `Surface` at 70% opacity, rising to `SurfaceRaised` at 85% on hover or keyboard focus.
Each button is 36px square, with a centered vector X or a 24px chevron icon.
The image and position caption are centered together, with the count 8px below the image.
The overlay keeps at least 24px of outer space.

**The decode.** `CoverImaging.WidthBuckets` used to top out at 640 pixels, so the in-modal hero
was already a 640-wide decode upscaled — at 3840x2160 it was drawn 1148px wide from a 640px
bitmap. A 1280 bucket was added so the lightbox draws the shot at its native size rather than
at a two-times upscale. 1280 is the native width of `t_screenshot_huge`, the only asset the
application draws larger than a cover, and nothing else reaches it; decoding never upscales
past the source, so a 1200x1800 Steam capsule asked for at 1280 still decodes at 1200.

**Controls and keyboard.** A close control, and back/forward navigation across that game's
shots. Navigation wraps in both directions, the answer §10.3 already gives for Up and Down in
the action menu. A game with a single screenshot draws no navigation at all rather than two
inert controls, which is the rule §10.3 already applies to that menu's rows. `Escape` closes,
`Left` and `Right` navigate; they are answered by the window in a layer above the modal's own,
so one press of `Escape` closes the lightbox and leaves the modal standing — §12.4's
one-layer-per-press rule applied here. Only a press that lands on the scrim itself closes, the
same guard the modal's own scrim carries.

**Focus.** `KeyboardNavigation.TabNavigation="Cycle"` on the overlay panel — the same trap the
modal's card already carries, one layer up — so Tab cannot reach the modal beneath. Focus moves
to the close control without an initial highlight (`NavigationMethod.Pointer`). Tab navigation
still shows the keyboard focus ring. On close, focus returns to the thumbnail the lightbox was
opened from, and that thumbnail is scrolled back into view because the strip scrolls sideways.
It returns to the originating thumbnail rather than to the one now showing; the strip's own
mark follows the overlay, so the two can differ after the user has navigated, and the mark is
what shows where they got to.

**Accessibility.** The surface says it is a dialog and which shot of how many is showing.
Avalonia 11.3.20 has no `AutomationProperties.IsDialog`, so the overlay takes
`AutomationProperties.ControlTypeOverride="Window"` — verified to compile against 11.3.20 —
and both the role and the count are spelled into `AutomationProperties.Name`, which is the same
answer §8 already gives for a count when `PositionInSet` and `SizeOfSet` are read by nothing.
Because changing a `Name` at runtime raises no UIA event, navigating would otherwise be silent,
so the position also rides the caption under the image: a bound `TextBlock` with
`LiveSetting="Polite"` and no `AutomationProperties.Name` of its own, exactly the arrangement
§10.3's refetch status field uses and for the reason recorded there.

See `docs/spikes/screenshot-lightbox-scale.md` for measurements of the original layout,
which reserved separate rows and columns for the controls.

**Tab order follows the tree, not `TabIndex`.** Avalonia's tab navigation walks declaration
order and ignores `TabIndex` on a non-focusable container — measured, not assumed. The right
column is therefore declared first and placed second by `Grid.Column`, so the keyboard reaches
`Play` before it reaches an appid.

### 10.8 The patch notes panel

A patched game's `Patch notes` button on an update row, and the `All patch notes` link beside
`Store page`, open the notes in an embedded browser window rather than in the system browser.
Reading an update no longer leaves the app. The host is the same WebView2 browser Winnow
already ships for the Epic consent and Steam sign-in windows, differently constrained.

**It is a separate top-level window, not an overlay.** The reason is the airspace problem the
sign-in window already records: a hosted native browser HWND paints over Avalonia content
regardless of z-order, so no Avalonia chrome could appear above the browser rectangle. A
window of its own sidesteps it entirely.

**Non-modal and owned by the main window.** The library keeps scrolling, the detail modal keeps
its place, nothing is blocked. `Escape` dismisses it, so does the close button, and so does the
page asking to close itself. One window at a time — opening a second note navigates the open
window and brings it forward.

**Chrome.** The system title bar, titled with the game. Across the top of the client area a
`Surface` strip with a `Line` rule under it: the current page's host on the left in Data S,
and on the right one quiet action that hands the page to the user's own browser. When the
embedded browser cannot start, an `Amber` line appears in that strip and the window stays
dismissable. 1024x820, the same size as the sign-in browser window.

**Appearance travels by class name only** — `Window.notes`, `DockPanel.notes`,
`Border.notes-bar` and `.notes-problem`, declared in `src/Winnow.App/Themes/controls.axaml` —
because `Winnow.Auth.WebView` references Avalonia and `Winnow.Core` and nothing else. That is
the same seam the consent window uses, and a theme picked in settings is already in force when
the panel opens.

**The origin gate is the load-bearing part.** The address that opens a panel must be https, on
exactly one of four origins:

- `store.steampowered.com`
- `steamstore-a.akamaihd.net`
- `steamcommunity.com`
- `www.steamcommunity.com`

with `/news/` or `/announcements` in its path. Origins are compared as scheme, host and port,
exactly. Steam's own news API hands out `steamstore-a.akamaihd.net/news/externalpost/...`,
which redirects onto a community announcement; that is why all four are named.
`update_events.url` is captured from a network response, so the gate is an allowlist, for the
same reason §10.3 gives.

Once open:

- An allowlisted origin renders.
- Any other web address is cancelled and handed to the user's own browser.
- Anything that is not a web address at all — `data:`, `blob:`, `file:`, `javascript:`, a
  launcher protocol, any custom scheme — is refused outright.
- A popup takes the same decision.
- A subframe takes a stricter one: off the allowlist it is blocked rather than opened
  externally, so a third-party embedded video does not load. A cost, taken deliberately.

The whole decision is `PatchNotesPolicy`, built on the same `AuthFlowPolicy` the Epic sign-in
and the Steam account-page harvest run on, so there is one origin mechanism in the application
rather than two.

**§10.3's rule is unchanged and still first.** Every outbound target is built by
`GameLink.Create`, and a target that fails validation renders no button. The panel's gate is a
second gate after that one, not a replacement for it.

**Nothing is injected into the page.** No host objects, no web-message channel, no developer
tools, no context menu, no downloads, and every permission request is denied. The browser
profile is in-private and lives under the run's own data directory, so `--data-dir` redirects
it with everything else. Script runs — a storefront news page is an ordinary web page, and
there is nothing in the panel for it to talk to.

**A game whose updates carry no page says so.** One `TextDim` line under the update list,
stating only that there is no page to read — not that nothing shipped, which is the same
distinction §10.4 draws for its own empty state.

**Where the panel is unavailable** — no WebView2 runtime on the machine — the buttons keep
their prior behaviour and open the system browser. Nothing is greyed out and nothing announces
itself.

### 10.9 IGDB override

When fuzzy resolution picks the wrong IGDB entry for a game, or none at all, the cover,
summary, release year, publisher and genres stay wrong with no way to correct them. This is
the recourse: search IGDB by title from the modal, pick the right entry, and that choice is
pinned so later automatic enrichment passes leave it alone. Clearing the pin returns the game
to automatic resolution.

**`Wrong game?` is a row in the action band's menu (§10.3)**, beside `Open folder`,
`Edit details` and `Hide`. The search field and the candidate list draw full width in the
right column's rest band, the scrolling star row, which is what makes the bounded-scrolling
behaviour structural rather than arithmetic. Only the Clear control is in the left column,
under the cover and ON DISK — §10.1's object column, where the identity facts live.

**The section carries an `IGDB MATCH` heading and its own close control** (§10.3's rule). The
`×` glyph sits in the trailing Auto column of the header row, beside the heading; its tooltip
is `Close IGDB match`. The heading names the surface: the menu row that opened it closes
itself as the section appears, so nothing else on screen would identify it. Choosing the row
while the section is already open scrolls it into view and puts the caret back in the search
field; nothing standing in the section is discarded — a same-game offer the user may be
part-way through answering, and any search results, survive. The two places the section folds
itself on success — a landed assignment and a landed same-game link — are unaffected: those
reload the library and reopen the modal, so focus belongs to the reopened modal, not to the
trigger.

**The surface is inline, never a flyout.** The search field and the candidate list draw in
the modal's own tree, not inside the menu that opens them. §10.7's rule, applied again: these
are surfaces to read and type in, and a hand-drawn ring per control would be the whole cost
of the surface. Only the control that opens them moved into the menu, where the mark is drawn
in the item template (§10.3).

**What a candidate row draws.** Four facts: a 34x51 cover at `RadiusControl` — §4's rule that
the three radii rank by the size of the object they round, and §6's list-view precedent — the
name, the year in Plex Mono, and the platforms in Jakarta: the modal's own identity-line split,
§3's rule that every number is Plex. Those four facts are what separate Prey (2006, Xbox 360)
from Prey (2017, PlayStation 4), which is the failure the whole control exists to fix. The row
draws full width in the right column on one line: the 34x51 cover, then the name over the year
and platforms, then the assign control in a trailing Auto column. The platforms trim with an
ellipsis inside the text column and carry the full list as a tooltip, because a trimmed platform
list is how a user tells *Fortnite* (2018, Android/PC) from *Fortnite* (2020, everything); the
row's height comes from the cover, so a short and a long subtext measure the same. An entry IGDB
gave neither a year nor a platform draws no second line. The covers ride the existing image path and add no new one:
IGDB's cover URL carries the asset's image id, and an image-id cover key is one the registered
IGDB cover source already answers without credentials. They draw at full saturation — the
dormancy ramp is about your own library and none of these candidates is in it yet.

**The candidate list is a scroll region of at most 238px.** IGDB search returns up to 20
results, so more than three is the normal case for a common title. A row is 68px (the 51px
cover plus padding and rule), so the region shows three rows and half of a fourth; the cut row
together with the scrollbar is what says there is more rather than the list ending silently.
The list's content carries `InnerScrollGutter` (§10.1) so the bar does not cover the assign
controls at the trailing edge. The section's close control is the first Tab stop in the
section. Each row's assign control
follows, and a row reached by Tab is scrolled into view, so focus is never left off screen.

**Six states.** Assigned reloads the library and reopens the modal on the same ownership,
carrying its confirmation across, so the user sees the corrected cover, title, year and
summary where they asked for it; that is the same arrangement retracting a link already uses,
for the same reason. Four refusals — the game is no longer in the library, IGDB had no details
for that entry, another game already holds that entry, and the write failed — each keep the
controls in place under their own `Amber` sentence. Three of the four are dead ends. The third
— another game holds that entry — becomes the same-game offer described below whenever the
holder can be named, and falls back to the bare `Amber` sentence when it cannot (no link
repository, no holder found, or the holder is this same work). A search that matched nothing is
the sixth state and is `TextDim`, not `Amber`: it is not a failure. `Amber` and not `Danger`,
per §2: attention, not a destructive act.

**The status field is words.** §8, applied: while the search is out or the choice is being
written, the control says so in words in a status field. No spinner and no `Transitions`, so
reduced motion has nothing to disable and the surface is identical in both motion settings.

**A live IGDB pin outranks the store capsule for that work.** The cover-key precedence is:
(0) user-set art, when `works.cover_url` holds a `winnow://user-art/<token>` reference
(migration 0027, §10.10); (1) a live IGDB pin on this work, when the work's `cover_url`
yields an IGDB image id; (2) the Steam portrait capsule for this release's appid; (3) the
image id in the work's stored `cover_url`. Rung 0 outranks the pin because under the
field-source model the value in `cover_url` *is* the user's — there is nothing for it to
outrank — and a later metadata fetch replaces that value rather than layering over it. A user
reaching for the wrong-game control is not only saying the metadata is wrong, they are saying
the storefront art is wrong, so the pin wins. The ladder is not the grid's alone: both
surfaces that derive a game's art from a release use it — the library load and the Merges
queue. The queue previously had its own store-first ladder with neither rung 0 nor rung 1, so
an imported cover drew on the grid and in the details modal but not in the queue — the same
failure this paragraph already settled for the store capsule. The queue reads the pin set once
per load, and reads the pin off the release's own work row, never the resolved work: the pin
and the `cover_url` it rewrote are columns of the same row, and resolving through the
same-game map would pair one work's pin with another work's URL. The assignment service is
optional on the queue: without it rungs 0, 2 and 3 stand and only rung 1 is lost. A pinned
entry that IGDB gave no cover keeps the store capsule — the user is no worse off than before
the pin, and a placeholder tells them less than the wrong art. Nothing is evicted from the
cover cache: a `CoverKey.Igdb` names the artwork asset itself, so pinning moves the tile to a
key that has never been fetched, and clearing returns it to the Steam key whose cached bytes
are still the right bytes.

**Clear is drawn only while a pin stands**, read when the modal opens. It sits in the left
column, under the cover and ON DISK, at the foot of the identity facts and inside that
column's own scroll region; the search surface it used to sit above stays in the right column,
because a candidate row carrying cover, name, year and platforms does not fit 200px and a
single link-styled button does. Clearing writes no metadata — it only stops the pin — but it
reloads the library and reopens the modal, because dropping the pin changes the cover key back
to the store capsule and only a reload draws it. The metadata the pin wrote stays in place and
the next automatic pass fills what is empty around it.

**The input field** takes §16.3's field treatment: `Well` cut into the card, found by its
`Line` border and lit by a `Volt` ring on a border whose thickness never changes (§10.7,
§14.7). `Enter` runs the search. **The field takes a title or an IGDB id.** An all-digit query
runs both the id lookup and the title search, because numeric titles are real (*2064*, *1979
Revolution*, *428*). A hit from the id lookup leads the list wearing a chip mark in the
outlined store-chip idiom; the title results follow beneath it, with the id-matched row removed
from them if it appeared there too. An all-digit query that named no IGDB entry gets its own
`TextDim` line above the results, not `Amber`, for the same reason the empty title search is
not a failure. The id-match row draws the same facts as a title result, platforms included.

**The same-game offer.** `works.igdb_id` is UNIQUE. Two works claiming one IGDB entry are the
same game, so a collision is not a dead end — it is an answer. When the holder can be resolved,
the `Amber` refusal is replaced by an offer that names the other game, draws it in the candidate
row's own idiom (34x51 cover at `RadiusControl`, name and year in Plex), and asks whether the
two are the same game. The idiom is reused rather than invented because it is the same
judgement: comparing one game against another by its art, title and year. The border is `Line`,
not `Amber`, per §2: the collision refusal is a failure, but the offer is a question.

Accepting writes the same `same_game` identity link the Merges queue writes —
`IIdentityLinkRepository.LinkAsync`, kind `same_game`, source `user` — with the holder as
the parent. It is the parent because it carries the `igdb_id`, which is the first rung of
the Merges queue's own precedence ladder, and its metadata is the entry the user was reaching
for. Nothing is pinned: pinning the child to an id another row holds is what the UNIQUE
constraint refused, so the link is the whole answer. The user confirms in place and is never
sent to the queue. `game-library-design.md` §5.3 permits a hard external-id join to auto-merge;
naming an exact IGDB id is a hard join, and the in-place confirmation — which names and shows
the other game — supplies the review a queue would otherwise provide.

Declining writes nothing — no pin, no link — and restores the bare `Amber` refusal sentence, so
the user still knows why the assignment did not land. The candidate list stays for another try.

On success the library reloads and the modal reopens on the game the two now are, carrying a
confirmation. The offer is additive and degrades cleanly: with no identity-link repository
registered, no holder found, or a holder that resolves to this same work, the collision draws
the refusal sentence it drew before the offer existed.

### 10.10 Editing a field by hand

The IGDB assignment and the automatic enrichment pass set every field in one go. This is the
other gesture: setting one field and making the user its source, leaving every other field
tracking its own.

**`Edit details` is a row in the action band's menu (§10.3)**, beside `Wrong game?`, because
it is the same kind of act: correcting what Winnow believes about this game. `Wrong game?`
answers which game this is; `Edit details` answers what each of its values should be. The editor draws full width in the right column's rest band, directly
under the IGDB reassignment control's own block. The identity question comes first on the
surface because it is first in fact: assigning an IGDB entry rewrites every field in one pass.
**Inline, never a flyout** — §10.7's rule applied again, for §10.7's own reason. The rest band
is a bounded scroll region and the editor opens below the fold; the scroll that brings it into
view runs whether the editor was just opened or was already open, so choosing the row again is
a way back to the section rather than a way to lose it. Drafts in all six rows survive, and
the editor does not reload. Nothing of this control is in the left column. §10.9 puts only
Clear there, under the cover and ON DISK, and six labelled rows with previews and per-field
buttons do not fit 200px.

**The section carries an `EDIT DETAILS` heading and its own close control** (§10.3's rule).
The `×` glyph sits in the trailing Auto column of the header row, beside the heading; its
tooltip is `Close editor`. The close control is the first Tab stop in the section.

**Each field carries its own source, and that source is the single answer to where the value
came from.** There is no override layer stacked over an automatic value. A metadata fetch
rewrites every field in one pass, because the user is saying "take it all from this record." A
manual edit sets one field and makes the user its source, leaving every other field alone and
still tracking its own.

**There is consequently no form-wide Save.** A single Save over the whole form would be the
take-it-all gesture again, and that gesture already exists next door. Save is per row. Every
row draws its source as an outlined badge in the store-chip idiom: `YOU`, `IGDB`, `STEAM`,
`EPIC`, `GOG`, or `AUTO` for a field no writer has claimed. Each badge carries a tooltip saying
in words what the badge means for enrichment — a user-owned field is left alone, an unclaimed
one will be filled.

**A row carries an `Auto` control that hands that field back to automatic**, drawn only when
the user owns the field. A field nobody has claimed and a field a service owns have nothing to
hand back. It is a different control from §10.9's `Clear`, which drops the IGDB pin, and the
two sit in one modal, so they do not share a word.

**`igdb_id` is deliberately not a field here.** Identity is the pin's question (§10.9), not a
field's. One consequence: §10.9's same-game offer cannot arise on this surface, because the
collision it answers can only be produced by naming an IGDB id.

**Six rows, in this order:** Name, Release year, About, Cover art, Publisher, Background art.
`About` is the one multi-line field. `Release year` is the one numeric field and draws in Plex
Mono with tabular figures, §3's rule. A year outside 1900–2200, or a blank name, is refused
under the field rather than stored. The two art rows take a URL in the field and carry a
`Choose file` button beside it — a web address or a local file, the same two routes, both
landing in the existing cover cache and both honouring `--data-dir`. Each art row previews what
it holds, at full saturation — §10's own rule: the dormancy ramp is a scanning aid and the user
has finished scanning. Cover previews at the 2:3 portrait the whole grid is made of; background
previews at 16:9. A row with no art draws a placeholder saying so, never a hole — §7's rule.

**Note, status and refusal are per row, not per form**, because the message lands under the
field it concerns — §16.3 already draws that line for the hand-added form. Status is words:
loading, saving, clearing, fetching an image, copying an image. No spinner and no
`Transitions`, so reduced motion has nothing to disable and the surface is identical in both
motion settings (§8). Refusals are `Amber`, per §2: attention, not a destructive act. The
controls stay in place under the sentence, so the retry is where the failure was. One busy flag
for the whole editor: a second write cannot start while one is in flight.

**Saving art reloads the library and reopens the modal on the same ownership**, carrying its
confirmation across — the same arrangement §10.9 already describes for an assignment, and for
the same reason: the stored value becomes a user-art reference, the tile's cover key is
computed when the library loads, and only a reload draws the new art on the wall. **A text save does not reload**, and does not need to: the save hands the library the field
key and the value as stored, after the editor's own rows refresh. Only `name` is acted on; the
other three text fields are drawn nowhere outside the modal, which has already refreshed
itself. The library renames every live tile behind that work — several when a same-game link
group sits behind one work — and with it the grid tile, the list-view row, the modal headline,
the tile's filterable row (so search and every live list follow), and any feed card, which
borrows the same tile instance. The provisional-name badge is cleared, because a name save
clears `works.name_is_provisional`. The placeholder gradient is recomputed, because it is
derived from the title. The current sort and filter are re-applied in the same pass, so a
renamed game takes its new place in the order immediately. Every draft in the other five rows
survives, the modal stays open on the same ownership, and the editor stays disclosed — which
is precisely what a reload would have cost. The seam is optional like every other seam on this
modal: unwired, the save is exactly what it was. The carried
confirmation is drawn outside the disclosure's own open/closed gate, because reopening leaves
the editor closed and a confirmation nobody can see is not one.

**Optional in the way every seam on this modal is.** With no edit service registered, or a tile
that resolves to no work id, the link is not drawn at all and the modal is exactly what it was.
Omitting only the image picker costs the `Choose file` route and leaves the URL route
untouched.

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
rail's `Patched` bucket, and a second door onto it would need a second marker, and the
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
`Patched`, and a dot's meaning survives precisely as long as there is only one of them.

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
would need its ring hand-drawn, which is §10.7's standing reason. The detail panel has one popup — the action band's menu —
and it is not a counter-example, because the menu draws its own mark inside the item template
rather than relying on the adorner layer. In the window's own tree, the focus ring and a linear tab order both come free, and the
question sits directly above the thing it is about.

`Enter` confirms, `Escape` cancels, and focus follows the prompt into its field. The save
prompt opens with the rules read out as a suggested name ("Started · RPG"), because a rail
full of "Live list 3" is a rail nobody reads.

**`Add to list` is one control for both views.** The grid selects one tile, the list view
selects many, and the button reads whichever is in force, naming the number once there is more
than one. The picked set is derived from the selection in the view model rather than in the
pointer handler, so arrowing across the wall arms it exactly as clicking does. **The details
modal is a third surface for list membership and a different control:** one checkbox per
hand-built list, ticked when the game is already a member, resolved through `same_game` links
in SQL so the answer is for the game and not the store entry. Live lists are not offered — a
live list finds its own members and there is nothing to tick.

**Deleting asks first, and the question says what survives:** *"Delete "Couch co-op night"? The
titles stay in your library."* `Danger` appears on its confirm button and nowhere else on
the strip. Deleting a hand-added game (§16) is the other destructive act in the application.

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
| `PaneGround` | Merge queue, Stores, Library, Appearance, the library's list view, the empty state | Exactly `WallGround` |
| `TileGround` | Under the art stack inside one tile | **Never** (§14.4) |
| `ChromeSurface` | Rail, filter panel, the list view's column-header strip | Yes, at the pane tier |
| `CaptionFill` | The 36px title lip | Flush it *is* `ChromeSurface`. Floating it paints nothing (§9) |
| `ChromeRaised` | Hover / selection fill inside the rail, the panel and the list | A veil, see below |
| `ChromeRaisedHalf` | A *hovered* row where `ChromeRaised` is a selected one | The same veil at half strength |
| `ChromeFieldOnGround` | An input on the command bar or cut bar. Its container is the library pane | Paints nothing past the ink ramp |
| `ChromeFieldOnSurface` | An input in the filter panel | Paints nothing past the ink ramp |

**A pane composites over the ground exactly once.** `FloatingLayoutTests` walks that at every
position, in both layouts, in both reach states. A second element declaring `ShellGround` would
put every figure this section and §14.3 quote out by the same factor, and nothing but the test
would catch it.

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

`0.15` is the round step past the boundary, it buys 1 to 5 points on top, and it fixes the
two figures the rest of §14 derives from: **the ground admits 85%, a pane admits 35%.**

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
not a service, and being ambushed by it is not either — so the Appearance screen says nothing
at all until the setting actually crosses the line, and then shows one `Amber` sentence naming
the percent at which it crossed. A number visible at every position is not information about
the position you are on, and the AA ceiling is a per-theme constant the user cannot act on
until they have passed it. `Amber` and not `Danger`: §2 gives `Amber` attention and `Danger`
destructive acts, and a setting chosen with the warning in front of you is neither an
error nor something to be undone for you.

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
across the window. That table *is* the argument for offering both; the two option cards on the
Appearance screen carry their own one-line descriptions, and this table is where the
measurement behind them lives.

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

**One slider, not two.** Two percentages on one screen that mean different things is a worse
screen than one quantity with a stated relation, and the pane's share is forced by the ground's
(§14.2) — it is not an independent choice. The ground admits 85% and a pane admits 35%; the
relation is in the table above and the identity in §14.2.

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
| **Merge queue · Stores · Library · Appearance** | **Card** | Content; they replace the library pane and take its island |
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
- **A visibility rule became a fact of composition.** The merge queue, Stores, Library and
  Appearance replace the library *specifically* so they do not sit under a command bar whose
  search and sort mean nothing to them. The bar is inside the library's own `Border`, so no
  arrangement of those panes can put a settings screen under the library's controls, and there
  is no parallel `IsVisible` rule left to keep in step.

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

---

## 16. The settings surface

The gear at the foot of the rail opens `SETTINGS`, which holds three screens in this order:
**PLATFORMS**, **LIBRARY**, **APPEARANCE**. Each is drawn the same way: a 48px header lining
up with the command bar and the filter panel's header, its own scroll, cards on `PaneGround`.

PLATFORMS is the store-connection screen. APPEARANCE is §14 and §15 — theme, transparency,
layout. LIBRARY holds four cards: **ACQUISITION EXPORT**, **EXPLICIT CONTENT**,
**HIDDEN GAMES**, **ADDED BY HAND**. Export saves the stored facts; the other three control
what appears in the library.

It is not under APPEARANCE, which changes material and layout and no data. It is not under
PLATFORMS, which is about connecting to a store; this is about what to do with what arrived.

The acquisition export card offers **Export acquisition CSV**, followed by a polite status
line for completion, cancellation or failure. Its explanation states that missing facts stay
blank and prices are stored cents without a recorded currency. It uses the same card and
action-button styles as its neighbours. Price stays out of the game details modal (§10.5).

### 16.1 Explicit content

A toggle, off by default. Off, works whose stored maturity evidence reads as adults-only are dropped
from the grid, the list view, the feed and every bucket count — one clause in the shared
bucket query all four read. A game with no rating data is not treated as explicit and stays
visible either way; the default is stated beside the control.

Beside the toggle, how many library entries turning the filter on would remove, in the same
`[Data figure] [words]` split the Platforms card's account-scope count uses. The figure
counts tiles that actually disappear, computed by running the bucket query both ways and
subtracting. It reads zero on a library where enrichment has not yet stored a rating, and
says so.

**The rating cap is a separate control and lives in the Display preferences popover on the
command bar**, beside density and the dormancy toggle, not on the LIBRARY settings screen. It
is a slider across `MaturityTiers.Ordered` — Everyone, Preteen, Teen, Mature, 18+, Adults
only — and hides works whose highest stored maturity evidence exceeds the chosen tier. A work
with no rating evidence is never hidden by the cap; `MaturityTiers.IsWithinCap` treats
Unrated as within every cap. The chosen tier is named in words beside the slider and the
number of titles the cap is hiding is stated underneath; a thumb position is not a value
anyone can read off a track (§8). When the cap is at its top step while the 18+ toggle here
is off, a note names which control is still hiding adults-only content, so the user can tell
where a game went.

### 16.2 Hidden games

The one place a hidden game can be found and put back, one at a time. Each row states what
unhiding gives back: the title, how many store entries come with it, and the date it was
hidden.

Hiding is done from the game, in two places: the library's context menu and the details
modal's action band. The context menu acts on the whole picked set and names the number once
there is more than one, exactly as `Add to list` does. The action band places it as a row in the `More` menu (§10.3) rather than on the strip,
because that band is about getting into the game and hiding is the quiet answer behind it;
the context menu remains the route that acts on a whole picked set.

**Hiding takes the game and its whole link group**, so hiding a game does not pop its demo
back into the grid. It deletes nothing: the ownership row stays, and a later ingest of the
same ownership does not bring the game back on screen.

Empty state: nothing hidden, and it says where hiding is done.

### 16.3 Added by hand

Games no launcher on this machine writes to disk — itch.io, Battle.net, a physical or
emulated title, a standalone installer. A hand-added game participates in the library, the
feed and the bucket counts like any other.

One inline form serves both add and edit, in the pane's own tree and not a flyout, for
§12.3's stated reason: a popup is its own root and has no adorner layer, so every focus ring
inside one would have to be hand-drawn.

**Fields:** title (required), year, a free-text platform label, an executable path, an IGDB
id and a Steam appid. Naming an executable is what lets session monitoring record play time.
Either id is what gets the game cover art. An id already belonging to another game in the
library is refused before anything is written, and the message lands under the field that
conflicted.

**"Add from a file" starts the form from an executable.** The user browses for a `.exe` and
Winnow reads its Win32 version-info resource section (file description, product name, company
name), or when the file is not a PE image or cannot be read, derives a title from the folder
name or the file name on the path. The result fills the title and executable fields and
immediately searches IGDB, presenting the same candidate rows the details modal's IGDB
override uses. The user picks one, edits the form, or dismisses the proposal and types by
hand. Choosing a candidate fills the title, year and IGDB id fields; nothing is written until
Save. The status field during a search is words, not a spinner (§8). A search that returned
nothing is not an error and says the form can still be filled by hand.

**Deleting a hand-added game asks first, and the question names what survives** — §12.3's
own rule applied a second time. It removes the ownership, then the release only when no
other ownership hangs off it, then the work only when it has no releases left, so a store
entry that later attached to the same release keeps its game. `Danger` is on its confirm
button and on nothing else on the screen.
