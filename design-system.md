# Winnow — Design System

**Applies to:** Avalonia 11+ desktop client, dark by default with optional light themes
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

A slim browse spine appears immediately left of the native scrollbar in both views. Under `Name A–Z`
or `Name Z–A` it is a `# · A–Z` jump spine in Data S, keeping all 27 stops fixed and dimming
unavailable sections without removing them. Activating a stop preserves the active name-sort
direction and brings the first visible title in that section into view; `#` owns titles that begin
with a number or symbol. Under every other sort, the same 27 positions become short unlabeled
`TextFaint` notches; dragging them moves proportionally through the ordering already in force rather
than inventing labels for playtime or recency. The spine keeps a 24px hit area whose glyphs or marks
rest against the native scrollbar; that scrollbar keeps the window's 10px resize inset, so
neither control overlays cover art nor enters the OS hit-test band. The spine and native scrollbar are
adjacent but independent controls: entering the spine does not engage the thumb, and dragging the
spine scrubs directly among alphabet sections rather than mapping to the scrollbar's extent. An
unavailable alphabet stop resolves to the nearest populated section without moving the pointer
feedback away from the stop under the pointer. Each 11px glyph and each notch shares the same exact
right edge. While the spine is active, the fractional pointer row anchors a compact four-row cosine curve that reaches 13px
into the library and returns smoothly to rest, the desktop version of Niagara Launcher's Wave Alphabet.
It has no transition, so sub-row movement renders directly. The same pointer row places a compact Volt
halo, and three neighbors step outward through Volt, Text and TextDim. The pointer remains the ordinary
arrow. When it leaves, the wave returns to rest and the halo again marks the current visible title.
The spine reverses to
`Z–A` with the sort so downward motion always moves down the library. Each stop remains a named
button with the standard visible focus treatment.

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
│              │   ┌────┐  ┌────┐  ┌────┐  ┌────┐  ┌────┐  ┌────┐       │
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

10px `Flare` dot in the top-left corner, 8px inset, with a 2px `Ground`-coloured ring so it
reads against any cover. Optional soft outer glow at 30% opacity. The opposite corner keeps
the alert clear of the top-right Details fold.

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

Bottom-aligned gradient scrim to `Ground` at 92%. Title in Body L, playtime and idle time in
Data S. The available primary action (`Play` for an installed game, `Install` otherwise) is a
32px icon at bottom-right, in the store-chip row. `Details` is the folded top-right corner: a
40px square hit target whose visible triangle follows the cover edge. The actions appear on
pointer hover and on Tab or directional focus. Each has a tooltip, an accessible name and the
standard visible focus treatment from §8.

**Stores are a chip row**, one chip per store the game is owned on, unchanged in appearance for
a single-store tile. Chips always occupy their own row below the stats. The stat text wraps
to at most two lines with ellipsis and a full-text tooltip, so it cannot paint over a chip at
the density floor. A multi-store tile additionally carries a compact one-letter-per-store
mark at rest on the front, which fades out over 140ms as the overlay rises, exactly as the
baked placeholder title does. The chips are the one "where you own it" fact, drawn once, so
the four-fact cap is not breached. The resting mark uses initials because the density slider's
floor is 108px and a row of word-chips is wider than the tile there; the words are reachable on
hover, in the modal and in the automation name, which satisfies §8's
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

**The cover stays face-up.** Pointer hover and keyboard action focus reveal controls without
replacing, scaling or moving the cover, so their hit targets are stable throughout the 140ms
overlay transition. Pointer focus from clicking an action does not pin the reveal after exit.
The realized `GameTileView` owns that distinction and clears hover, focus and hit targets when
it is rebound or detached; pointer capture is resolved from the pointer's actual tile geometry.
Each icon has at least a 32px square hit target at the 108px density floor. A press on an icon
belongs only to that control; a double click elsewhere on the tile opens Details. Selection,
store marks, expansion marks and the unread badge remain available without revealing actions.
Hover and keyboard action focus also draw the promo site's 2px Volt border highlight. The
ring sits inside the tile with a 1px Ground separator so edge tiles remain unclipped. It
shares the existing selection ring: leaving a selected tile keeps that ring visible.
Tile dimensions and spacing do not change.

### 5.4 How the ramp is drawn

Avalonia has no CSS `filter`, and **it has no public API for authoring custom effects** — the
effect pipeline is closed, so a shader approach is not available. The dormancy ramp is drawn
as a **two-layer continuous cross-fade** between two pre-computed bitmaps: the full-colour
cover and one floor variant generated at `0.22 / 0.68` with the −6° rotation baked in.
`α = (S − 0.22) / 0.78`, taking `S` from §5.1's saturation column.

Escalate to per-state bitmap variants, or to a matrix path, only if profiling shows the doubled
bitmap memory is unacceptable. **Do not attempt per-frame pixel manipulation on the UI thread.**

A settings toggle disables the ramp entirely by forcing `α = 1` (§8). **With the ramp off the
floor variant is not decoded**, because a vivid layer at full opacity covers it exactly; the
stored variant stays on disk and turning the ramp back on asks for it at the width already on
screen, so the toggle costs a decode rather than a reload. Every surface that draws art at full
saturation — the detail modal (§5.5), the screenshot strip and its lightbox (§10.1), the IGDB
candidate rows (§10.9) and the metadata previews (§10.10) — asks for the vivid layer alone for
the same reason.

**Covers are virtualized and decoded off-thread at display resolution, not full size.** A
1,200-tile grid of 600×900 source bitmaps decoded eagerly will exhaust memory.

**The cover wall is `src/Winnow.App/Views/CoverWall.cs`, and
`Avalonia.Controls.ItemsRepeater` must not be reintroduced.** `UniformGridLayout` charges every
item in a row for a trailing gutter when it computes items-per-line for the scroll anchor, but
packs rows greedily when it places them, so §4's flush-row geometry made the two disagree by one
column at every window width. `CoverWall`'s remarks carry the measurements.

### 5.5 Art-backed detail surface

**The detail modal lays the game's own art behind its information.** It used to be flat
`Surface`, so a game lost its identity the moment the user opened it.

**The construction is layered.** Opaque `Surface` at the bottom, then the game's art,
then a veil — `ArtVeil`, the theme's own `Surface` at `ArtVeilAlpha` — then the text. The opaque
base stops a half-decoded cover showing the window through the gap between the dormancy ramp's
two layers; the same argument §14.4 makes for `TileGround`.

**The veil IS `Surface`, and that is the whole trick.** Over the opaque `Surface` the modal
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

**Which inks are held to 4.5:1.** `Text`, `TextDim`, `Azure` and `Amber` — the four inks the
detail surface sets text in. `Flare` is excluded: on this surface it is a dot and never a word
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

**The modal reuses its existing image path.** It binds `GameDetailsViewModel.Cover`, the 200px
bitmap it already asks the cover cache for at full saturation — §10's rule, that the ramp is a
scanning aid and the user has finished
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
cut bar on `ChromeSurface` with a six-segment kind filter, a cut chip while filtered, a
`Prefer · None` platform picker, and the count at the right, `14 → 6` while filtered, the
only arrow in the interface; and the queue,
one scroll of five outlined sections, ACROSS STORES · EDITIONS · EXPANSIONS · PARTS · TEST
BUILDS, each with the count of its pending cards and a one-sentence blurb.

The platform picker uses the sort menu's `ctl` trigger, chevron, `sortmenu` presenter and
6px `Volt` dot for the selected option. Its choices are `None`, `Steam`, `Epic` and `GOG`;
the trigger names the current choice, such as `Prefer · Steam`. Choosing a platform switches
every eligible pending card's header to an entry on that platform, including cards hidden by
the kind filter. Cards without that platform retain their header. Expansion bases and
completed links retain theirs. The preference is remembered for later loads; `None` stops
applying it without resetting the current headers. Users can still choose a header on an
individual card before confirming the merge.
The desktop tooltip reads: “Choose headers from this platform where
available, across all pending proposals. You can still change individual headers. None keeps
the current choices.” The fullscreen sheet is titled `Preferred platform for pending headers`.

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

A same-game candidate is omitted when either work is already an expansion or variant child.
Such a work already has the identity graph's one permitted parent, so presenting it as a new
header offers an answer the repository must refuse. If a card becomes structurally stale after
the queue loads, the refusal removes that card and appears in the dock as a no-change notice;
the notice has no Undo because no link act was written.

Keyboard: Up and Down walk the candidate rows across every pending card, Space makes the row
the header, `S`/`Enter` answers Same game, `D` answers Different games, and `Escape` returns to
the library. The radio and the checkbox are Tab stops of their own.

**Session journal prompt.** 400×220 frameless, bottom-right, `SurfaceRaised`, with the game's
cover at 60×90 on the left. Title, duration in Data, one text field, 5-dot rating in `Volt`.
Appears at most once per session, never steals focus.

**Fetch status field.** In the desktop titlebar, before the window controls, a compact
single line shows the label, a separator and the remaining-title count. It uses the existing
label, data and body typography without a box or animation and remains part of the drag strip.
It names what the enrichment pass has left to do as a real count that falls a slice at a time: the pass
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
it lives in the desktop caption rather than on any screen. Fullscreen continues to hide the
desktop chrome, including this status; returning to desktop shows the current shared count.

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
| Bucket: lifecycle evidence of closure, delisting or abandonment | `Derelict` | `Won't run`, `Dead` |
| Steam account statistics rail row | `STEAM STATS` | `STATS` |
| Badge tooltip | `3 updates since you played` | `New content available!` |
| Journal prompt | `How was that?` | `Rate your session!` |
| Card answer | `Same game` / `Different games` | `Merge records` / `Cancel` |
| Merges header | `Merge 3 selected`, `Rolled up under Hades.` | `Confirm identity link` |

`Merge` is the screen's name and its bulk verb. The answer on a card is still `Same game`,
which asks about games rather than records.

**Empty states are directions, not moods.**

Derelict is a library bucket and a separate feed shelf. It names lifecycle evidence, not a
promise that a game cannot launch. Each feed card states the inferred or explicit status,
confidence and source-based reason; the details modal repeats that information in Overview. Confidence is a heuristic estimate, not a measured probability. Delisted
games may still run. Launch actions keep their existing availability rules.

- Patched, empty: *"Nothing's been patched since you last played. This fills up on its own."*
- Derelict, empty: *"No games have enough lifecycle evidence for Derelict yet. This fills in as metadata arrives."*
- Never played, empty: *"You've played everything you own past the refund window. Genuinely rare."*
- First run, mid-scan: *"Reading your Steam library. Covers and metadata fill in over the next few minutes — you can browse now."*

The last one matters: store metadata backfill takes hours, so the interface promises a
browsable library immediately and art later. **Render placeholder tiles with the title set in
Bricolage on a `Surface` field — never a spinner, never an empty grid.**

The table does not yet carry rows for connection state or credential consent; the Stores
panel's strings were written from the auth spikes instead. TASK-81.

---

## 8. Accessibility floor

### Fullscreen and controller navigation

Fullscreen is a separate TV-distance interface with its own composition, components,
navigation and focus model. The reviewed mock set in `docs/mockups/fullscreen-v2/` guides
this implementation. The desktop specifications elsewhere in this document continue to
govern the desktop path; features must be maintained and verified in both presentations.

The shared identity is the teal palette, three font families, cover art, dormancy and unread
markers. Fullscreen gets its own type and spacing scale. Start with a 1920×1080 reference
canvas, 5% safe margins, 64px game titles, 32px section headings and 28px body text; essential
labels start at 24px at 100% text size. The user's text scale adjusts body labels from that
reference. These are design starting points, not measured distance guarantees.
4K increases rendering resolution rather than content density. Fit ultrawide is an optional
fullscreen preference: it expands the reference canvas horizontally to the display aspect
ratio while keeping the 1080px reference height and uniform scaling. The default retains the
16:9 composition. Validate readability from the actual seating position before accepting the scale.

**For you** opens on a focused recommendation in a horizontal cover shelf. A large title,
one-sentence reason and game artwork above the shelf follow the selection. The reason reserves
two lines at the chosen text size and truncates overflow with an ellipsis, so description
length does not resize the cover shelf. Up/down changes
shelves; left/right moves among their games. The selected cover has the only focus ring.
Text actions have transparent backgrounds and a mint underline on focus. Hover leaves
no underline; current sections and collections use bold text and a neutral underline.
The focused cover retains its outline. No desktop
rail, density control, hover actions or small cover buttons appear here.

**Library** uses a regular cover grid and a short row of collection choices. A dedicated
filter page groups large choices with persistent actions: Y applies and closes from anywhere,
and B discards the draft. Search is a dedicated page
with its own keyboard and results. **Game details** is a full page, with Play as its initial
focus and overview, updates and journal as separate sections. **Activity** and **Settings**
use large ordered rows, with focused values changed directly. Long content is paged or
scrolled within an explicit reading region. Each section remembers its game and focus
position; Back restores the exact origin, including after viewing details.

| Input | Behavior |
|---|---|
| D-pad / left stick | Move through explicit neighbors; no free cursor or inferred desktop tab order |
| LB / RB | Switch For you, Library, Activity and Settings at the root |
| LT / RT | Switch local sections or shelves in Home, Library, Activity, Settings and game details |
| A | Open the focused game or activate the focused action; opening details never launches |
| B | Close the top layer or return one level; root never exits immediately |
| X | Play the selected installed game from a browse screen; unavailable shortcuts are omitted |
| Y | Invoke the labelled contextual action: More on For you, Filter & sort in Library, or Reset page in appearance; confirmation protects destructive changes |
| View | Open search from browsing |
| Menu | Open a quick menu with Settings and Exit fullscreen; controller help is in Settings |

The footer shows actions available in the current state using bundled Kenney vector controller
glyphs. Local section and paging prompts sit at the right edge. The current input sources use Xbox-style
button shapes; device-specific glyph families are not detected. The dragon mark sits beside
the Winnow title. Controller input hides the mouse cursor; mouse movement or a click restores it. Clock and optional battery status sit in a quiet
top corner. Unknown battery state is omitted. Nested sheets trap focus and restore it on
close. Destructive actions require confirmation; unplugging a controller preserves position
and provides a reconnect message with keyboard fallback. Reduced motion removes travel and
zoom while retaining immediate selection feedback.

The main view list stays centered on the canvas independently of controller status and clock
width. Equal side regions hold the wordmark and status; long status text truncates within
its region instead of moving the view list.

Search, staged filtering, text entry, journal editing and a paged file browser use their own
fullscreen pages. Native and third-party windows need separate controller validation;
falling back to a desktop dialog does not satisfy M10.

#### Other fullscreen views

The remaining screens use the same typography, safe area and focus treatment, with a separate
composition for each task. The mock images are design references, not evidence of device testing.

**Library** uses two rows of complete portrait covers. The column count responds to available
width, row height and text size; wider displays show more games instead of stretching or
cropping artwork. Stable 2:3 frames use uniform fitting so user-supplied art keeps its whole
image even when its proportions differ. Padding takes the average color of the adjoining
artwork edge, with separate vivid and dormant colors. Titles sit below the covers, with a mint outline on
the focused game. Unread dots and dormancy retain their shared meaning. A dimmed landscape
backdrop follows the selection. Home and search use the same uncropped art treatment; home
only shows the recommendations actually returned by the feed.

Resizing recomputes page capacity while keeping the selected release anchored. Up from the
first row on the first page reaches the collection choices. At grid edges, down advances a
page and up returns to the previous page, preserving the column where possible. Triggers
cycle All games, Installed, Never played and Patched collections; My lists remains an explicit
picker. Y opens Filter & sort; View opens Search. Opening a game
and returning restores the collection and selected game.

**Game details** uses a landscape backdrop across the full canvas, including the header.
A dark left and top veil protects the title and status text; a vertical fade settles into
Ground before the overview content. Prefer the saved game background, then an available
landscape screenshot, then a quiet cover fallback. Artwork has its own display-sized lease
and high-resolution cache entry; its source quality remains the upper limit on sharpness.
The header shows B and the previous page name plus controller status and the clock. The
root navigation and wordmark return when leaving details.

The game title starts at 96px, uses 72px for long names and wraps to at most two lines in a bounded left region. The
primary action is larger than adjacent actions while retaining transparent underline focus.
Overview is a bounded composition without a scroll fold. A vertical rule separates the
history/return reason and Play history/About game actions from a two-line synopsis and two
visible screenshot previews. About game opens the full description, publisher and reception
in a reading page. LT/RT changes local sections; all management actions remain controller-accessible.
Play history, About game and screenshot previews form one left-to-right focus row matching
their placement; Up returns to the section tabs.

**Activity** is a personal history view. Sessions, Updates and Journal are local choices
reached with directional navigation; bumpers continue switching the main screens. Large
chronological rows occupy the left side and the selected event's art, facts and user note
occupy the right. The implementation reads saved sessions and notes plus raw update signals
for games visible in the fullscreen library. A opens session actions or the update's game;
X edits the selected session's note when applicable. Triggers change local sections;
left/right changes the Monday-based week from the event region and cannot advance past the current
week. Sessions and Journal have distinct empty-state copy: Journal requires a saved note or
rating. Activity uses a quiet open-journal vector with page contours and a bookmark; Settings
uses contour art. Both use theme-colored paths. The note editor offers deliberate Save and
Cancel actions and an optional one-to-five rating. Library summary provides the current
visible game count and separate reading pages for captured Steam account statistics; it
retains the shared rules for mixed currencies and wallet credit.

**Settings** has Appearance, Controller, Library, Platforms and Application sections. Large
rows expose a label and current value; left/right changes bounded values, A opens pickers
or activates toggles, and B returns. Appearance has a readable live sample. Fullscreen owns
its text scale (70–140% in ten-point steps), screen margins (0–10% in one-point steps),
and motion preferences. Text size has separate mouse decrease/increase buttons
and controller left/right adjustment. Theme selection is shared with desktop and updates
both surfaces immediately, including artwork veils. Dim dormant covers is also shared and
updates every dormancy-bearing cover immediately. Fullscreen reset preserves the shared
theme and cover-dimming choice. Defaults are 100%, 5%, motion and cover dimming enabled; Fit ultrawide defaults off.
Boolean settings use visible switch tracks
and thumbs with an On/Off status. Controller help fits one 16:9 screen with five concise
action mappings on either side of a proportional diagram and keyboard fallback below.
Fullscreen sizing, margins and motion do not change desktop appearance, library facts or
recommendations. Content visibility, platform credentials, journal opt-in and application
startup settings use the same value and validation in both UIs. Reset requires a confirmation
naming the affected appearance settings. Library tools has TV-owned forms for manual games,
hidden games and identity proposals. Identity answers use the existing validated operations;
resolved groups offer Separate again. Platform pages show real connection status and sign-in
and sign-out actions. When opened from fullscreen, the secure browser uses a fullscreen window
at 150% browser zoom. Platform status, session health and account identity bind to the shared
connection state and refresh on entry. Vertically stacked consent, API-key and import actions
use up/down navigation; horizontal control groups use left/right.
Up/down moves to the previous/next browser field, A selects, X checks an option,
and triggers page the document. Y opens a masked text composer in a reserved region below the
browser; Done inserts only the newly composed text and B discards it. Page navigation discards
unfinished text so it cannot arrive in a different document. B closes the browser after text
entry is closed. Embedded patch notes use the same window bridge with up/down scrolling and
bumper link navigation. Provider CAPTCHAs, third-party sign-in pages and phone approval can
still require their own input; real provider/controller validation remains required before
claiming complete controller-only sign-in.

The on-screen keyboard uses five QWERTY rows with Backspace beside the number row,
Delete at the upper right, Case at the left of the home row, Enter at its right, and a wide
bottom Space key beside an inverted-T arrow cluster. X backspaces and RT invokes Enter
from any key. Enter inserts a newline in multiline fields; in single-line fields it closes
the keyboard and sends Enter to the original field. B and Done close without submitting.
Desktop and fullscreen share editing behavior, password masking and field constraints,
with separate key sizes for each surface.

Steam configuration includes a masked, surface-local API key draft, save and confirmed
removal, account ownership scope, and the shared sign-in consent with optional purchase
capture. Purchase history supports embedded sign-in and selecting several saved HTML pages;
an explicit Read action starts the latter import and a reading page exposes every reported
count, skipped item and warning. Acquisition CSV export uses the TV directory and filename
chooser with overwrite confirmation. Manual game forms offer executable inspection and
metadata candidates through the shared commands. Identity tools include kind and sort,
selection and confirmed bulk grouping, with exact matching limited by the shared rules.
The identity page also carries the shared `Prefer ·` platform choice in a controller action
sheet, with the same options and pending-header behavior as desktop Merges (§6).
The Steam API key registration link opens the system browser; obtaining that credential is
an external website workflow, while entering and saving it stays inside the TV interface.

| Supporting view | Composition and focus contract |
|---|---|
| Search | Dedicated keyboard and results regions; an explicit control moves between them; return preserves query and selected result |
| Filter & sort | Grouped choices, result count and persistent Apply/Clear/Cancel actions; Y applies and closes, B discards uncommitted edits |
| My lists / live lists | Large list rows with counts; opening restores the list's browsing position; naming uses dedicated text entry |
| Contextual actions | Short ordered sheet for the selected game; cancel restores that game; destructive actions get their own confirmation |
| Game updates | Large update headlines and dates, then a full reading page; marks unread state according to the shared application rules |
| Game journal / note editor | Session-linked entries; editor has a readable text area and deliberate Save/Cancel actions with controller text entry |
| Library tools | Reachable from Library actions: hidden games, add game, possible identity matches and metadata corrections; one focused operation per page |
| Library summary | Reachable from Activity; readable summary pages, with detailed analytics progressively disclosed |
| Platforms / sign-in | Large status, configuration and action rows; controller browser input and fullscreen file selection use separate adapters over shared operations |
| Quick menu | Resume, Exit fullscreen and confirmed Quit; Settings is available at root level so switching cannot discard a nested editor; B restores focus |
| Empty / unavailable / disconnected | One clear explanation and recovery action; preserve selection and never redirect input to an obscured surface |

The image concepts cover the five main screens. Supporting views implement the contracts
above without separate generated mock images. All cover art, history, counts and battery
readings in generated images are illustrative. Generated pixels do not establish actual
sorting, measured text sizes, data provenance or supported device behavior.

### Keyboard and assistive technology

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
  root must be named as well as its content groups. `InteractiveControlNameTests` checks the
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
  badges and buckets carry the signal without it. Dim dormant covers is one persisted choice
  across desktop and fullscreen, including library grids and lists, Feed and Merges. Existing
  detail art, identity search previews and decorative backdrops retain their own treatments.
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
hoard of them looks like. While metadata is being fetched, a passive status line sits before
the window controls.
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

§5.3 caps the tile's hover overlay at four facts. Details expands that view in a **modal
over the library**, opened from a tile's Details control or the library's details command.
Closing it returns to the same library position. Escape closes a focused metadata tool first,
then the modal; the close button and a click on the scrim dismiss the modal directly.

### 10.1 Overview, Activity, Updates, Journal and Library

The modal has a compact persistent header and five tabs. The header carries an 82x123
cover, title, year and publisher, store and install state, and the Play/Install, Add to list
and More controls. The cover keeps its 2:3 geometry and full saturation. Its existing 200px
decode also supplies the subdued backdrop (§5.5).

Play and Install keep their text and carry the matching 16px play or download glyph. Add to
list carries the 16px list-plus glyph. The icons reinforce the verbs; the text and automation
names remain the controls' labels.

```
┌──────────────────────────────────────────────────────────────┐
│ cover   Empyrion: Galactic Survival                       [×] │
│ 82x123  2020 · Eleon Game Studios                             │
│         STEAM · Not installed                                │
│         [Install] [Add to list] [More ▾]                      │
│                                                              │
│ Overview    Activity    Updates ●    Journal    Library      │
├──────────────────────────────────────────────────────────────┤
│ 37h played · Last played 3 years ago                          │
│ ● 4 updates since you played →                               │
│                                                              │
│ ABOUT                                                        │
│ Description… Read more                                       │
│ [screenshot] [screenshot] [screenshot]                         │
│ IGDB USERS 78 / 1,204 · STEAM 91% / 41,203                     │
│                                                              │
│ EXTENDS · EXPANSIONS                              (scrolls)   │
└──────────────────────────────────────────────────────────────┘
```

**The tabs divide the questions the user is asking.**

| Tab | Content, in reading order |
|---|---|
| Overview | Hours and last-played summary, unread-update shortcut, lifecycle evidence when present, description and screenshots, reception, base-game and expansion relationships |
| Activity | Lifetime and tracked-session history |
| Updates | Updates and read controls, GOG patch notes when present |
| Journal | Saved session notes, ratings and inline editing |
| Library | Owned copies and linked-entry breakdown, list membership, acquisition, disclosed installation and identifier facts |

Ordinary Details opens Overview. The shortcut opens Updates in the same modal. All five
tabs remain present when data is sparse; absent integrations do not change their names or
positions. A refresh or correction that rebuilds details on the same ownership keeps the
selected tab. A new ordinary opening starts on Overview again.

**Overview keeps the reason to return visible.** Its summary reuses the stored hours and
last-played date; no date keeps the distinction between never opened and a session whose date
is not recorded. The update shortcut counts unread updates and disappears when none remain.
Updates carries the same unread marker and an accessible name that states the count.
The full timeline lives in Activity, so it does not consume space above every tab.

**Description is readable before it is exhaustive.** A summary longer than 360 characters
opens as a preview ending at a word boundary where possible. Read more reveals the whole
description and Show less restores the preview. The prose keeps `ProseMeasure` (§3), primary
`Text`, and selectable text. A missing summary says “No description yet. Metadata fills in
automatically.” A game with no screenshots draws no placeholder frames.

**Screenshots sit inside ABOUT.** The horizontal strip uses 120x68 thumbnails and a caption
naming the count and source. Each thumbnail is a real button with visible focus; selecting
one opens the existing lightbox (§10.7). Tab brings an offscreen thumbnail into view. Ordinary
wheel, Shift-wheel and horizontal trackpad input scroll the strip horizontally, and input
stays with the strip at either edge. The image path remains `CoverKey.IgdbScreenshot` through
the existing cache and `t_screenshot_huge` rendition.

**Reception follows the screenshots in Overview.** Up to three attributed figures appear in
this order: IGDB users, IGDB critics, Steam. Each shows its value and respondent count on the
line. They are never blended into a Winnow verdict. Steam's descriptive label appears in the
tooltip alongside its percentage and count. A source with no figure contributes nothing.
Values take `Text`; attribution and counts take `TextDim`. A `WrapPanel` lets complete figures
wrap without dropping counts. A base-game relationship remains a single visible row;
expansion rows open behind a collapsed disclosure so long collections do not crowd Overview.

**Activity retains the play evidence.** The tracker follows §10.2. Updates and Journal have dedicated tabs. UPDATES is
the constant list heading, distinct from the tracker ranges. Missing update
records say that no updates are recorded, not that nothing shipped. JOURNAL lists saved notes
newest first with session date, optional rating out of five and note. Editing stays inline,
including the existing five-dot rating control. Deletion asks “Delete this note?” and uses
Danger only on confirmation. Switching tabs preserves an unfinished journal edit. With no
notes, the prompt-enabled and prompt-disabled sentences remain distinct.

**Library keeps each copy's facts together.** The copy rows retain per-entry hours and
last-played dates. Linked-title rows keep their Separate action; any composite total is
explicitly labelled as summed across the listed entries. Achievements, when supplied, remain
per release. List membership uses the existing checkboxes and shared Add to list modal.
Acquisition follows §10.5. Installation paths, Steam app id and the IGDB pin's Clear control
live behind Installation & identifiers, rather than taking permanent header space.

**The card scales against the window.** `CardWidthCap` and `CardHeightCap` bind to
`$parent[Window].Bounds`, whose dimensions do not depend on modal content. Width is half the
window, floored at 860 and capped at 1582; height is two-thirds of the window, floored at 720
without an upper ceiling. `MinWidth` remains 700 and the outer margin remains 40. The caps are
limits, not promises that override the space inside a smaller window. At the app's 1200x640
minimum, the card must still fit inside the window with its header and controls reachable.
The existing scale measurements are in `docs/spikes/details-modal-scale.md`. The card stretches
to the available capped size, so its header does not move when a shorter tab is selected.

**One vertical reading area per tab.** The header and tab row stay outside the scrolling
content. Each tab's ScrollViewer sits in bounded space and retains its offset when switching
tabs. Long content increases the scroll extent, not the card size. The focused metadata and
matching views use the same bounded body area in place of the tabs (§10.9, §10.10).
The matching results retain their own bounded candidate list.

Fluent draws an auto-hidden scrollbar over the content. Each vertical region clears it with
`InnerScrollGutter` on its content; the screenshot strip uses `InnerScrollGutterBottom`.
The 20px clearance is the 12px expanded track plus an 8px spacing step. ScrollViewer padding
does not provide this clearance. `ScrollViewer.inner` opts out of the window-edge resize
inset (§9.1); it does not supply the content gutter.

**Keyboard and accessibility.** The five headers are native TabItems in a TabControl, with
selection semantics and visible focus. Arrow keys move among tab headers; Tab enters the
selected content. Inactive pages are outside the interactive focus path. The modal retains
its named Window role, a level-1 title, level-2 section headings and named identity, history,
actions and reception groups. Update rows and screenshot buttons remain named list items;
unread state is also stated in words. `AutomationProperties.Name` belongs on peer-bearing
controls or explicitly included groups, never on a TextBlock (§8).

### 10.2 The activity timeline

Activity opens a compact tracker with **Lifetime** and **Tracked sessions** controls. The
summary shows total played and the last-played date. The plot is 98px high; dates and a thin
coverage strip sit below it. The range controls, 11–12px supporting text and 30px total stay
readable; condensation comes from spacing and plot height rather than smaller type.

**Lifetime uses equal-width monthly bars.** Every bar represents hours in a calendar month,
with the same width for Steam history and Winnow observations. Stored monthly history uses
`VoltEdgeSoft`; Winnow uses `Volt`. A shared linear hours scale makes the tallest displayed
bar fill the plot, with its value stated above. There is no height clipping or independent
normalisation by source. Session duration remains the height measure in Tracked sessions,
whose bars are up to 10px wide. Dense sessions group spatially: the tallest contained session
determines height and selection states the count, total and longest duration.

**Time remains proportional.** Lifetime begins at the acquisition date when available and
extends earlier for older evidence. Without acquisition it begins at the earliest usable
record or last-played date, labelled without inventing a purchase or release date. Tracked
sessions spans recorded completed sessions through today, with at least 30 days of context.
The baseline uses `Line` until the known last-played tick, then fades from `Volt` to `Line`.
Date ticks adapt to width. A missing last-played date removes the tick and fade.

**Monthly history and sessions cannot be added indiscriminately.** Only consecutive, adjacent
month-end cumulative readings with consistent, nonnegative differences produce imported
monthly hours. Live observations are not redistributed into months. Resets, conflicting
readings and missing months leave unknown coverage. The stored snapshot shape does not carry
source provenance; month-end recognition retains the historical import convention rather
than claiming a separately verified source flag. Winnow session durations are split across
UTC month boundaries. An imported monthly total takes precedence over sessions in that same
month, so the same hours never appear twice. Individual sessions remain available in the
tracked view. Open, invalid or future sessions do not contribute completed duration.

**Coverage is separate from play.** Solid muted segments mark months with usable imported
readings, including measured zeroes. Hatching means no monthly record. Session observations
do not imply uninterrupted monitoring. A zero month has no positive-height bar; the coverage
strip and Recorded hours distinguish it from missing history. An earlier cumulative reading
is stated as an amount recorded by its date, outside the plot, rather than drawn across an
unknown span. Sparse history retains the same controls, states the missing facts, and draws
no plot when there is no temporal evidence. It never falls back to the old normalised gap rail.

**Update marks retain their reading state.** Dated updates sit on the baseline; unread marks
use `Flare`, other recorded updates use neutral ink. Nearby marks group into a count instead
of dropping everything after an arbitrary cap. Selection names the updates and their dates;
a grouped lifetime mark switches to the closer tracked view when that range exists. The full
Updates tab remains the route to patch notes and read controls. Acknowledgement refreshes
the marks without switching the selected range.

**The chart has one ownership scope.** Cumulative counters for linked copies cannot be joined
as one series. Like the prior history reader, this tracker uses the primary ownership's
snapshots; its sessions, acquisition, total and last-played summary use that same copy. Linked
games label this scope beside the total. The Library tab retains the other copies' figures.
Updates continue to include the linked game's release records.

**Every mark is accessible.** Bars and update groups are named buttons with tooltips, keyboard
focus and a selected-detail sentence. Recorded hours discloses the exact dated values. Small
or grouped marks never become the only way to obtain the data. The tracker stays inside the
Activity tab's bounded scroll area, uses the existing theme tokens, and has no animation.
Refreshing the same game's details preserves the selected timeline range.

### 10.3 Getting in

`steam://run/<appid>` when the game is on disk, `steam://install/<appid>` when it is not — and
**the button is named for which one it is**, `Play` or `Install`. A button reading "Play" on an
uninstalled 60GB game promises something the next hour will not deliver. **No appid means no
primary action at all, never an inert button.**

Beside the primary action are `Add to list` and `More`. Outbound destinations live in More,
so store-specific links do not make the header grow. The available primary actions and links
remain store-specific:

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
notes. When notes exist, a collapsed `GOG patch notes` disclosure in Updates
opens readable text; it is absent when the response has no changelog. The API and cache are
background work, so opening details never waits for either store.

**When a store entry has no primary action and no links, the header says why.**
The sentence takes `Text`, because it is the explanation of how to get into this copy.
It distinguishes an install state that has not been read from an identifier Winnow does not
hold. Other available commands, such as Add to list, remain usable.

`More` begins with the available outbound links, then installation management, `Open folder`,
`Refetch metadata`, `Wrong game?`, `Edit details` and `Hide`. A row is drawn only when it has
something to do: local folder when available, enrichment and correction tools when their
services exist, and Hide when the library supplies the command. The trigger includes outbound
links when deciding whether there is a menu to show. Its face always reads `More`.

Installation management is `Uninstall in Steam` for an installed Steam game with an app id,
opening `steam://uninstall/<appid>` so Steam owns confirmation and removal. Epic entries offer
`Manage in Epic Games Launcher`, opening `com.epicgames.launcher://store/library`; GOG entries
with a product id offer `Manage in GOG Galaxy`, opening the game's existing Galaxy page.
These navigation actions do not claim to invoke a game-uninstall protocol. Winnow never
deletes a game's files. Installation management stays in `More`, outside the primary Play/Install strip.

**A row's name does not change with the state of what it opens.** `Wrong game?` and
`Edit details` open a focused body in place of the tabs. Back to details, the tool's own close
control, or Escape returns to the retained tab and puts focus on More. Reopening the same tool
keeps its query, results or unsaved field drafts. The header remains visible throughout.
The tool's close tooltip names the tool; the modal's close button still dismisses the game.

**A heading that names a section is set in `TextDim`**, the same ink as the `×` glyph beside
it, so the header reads as chrome rather than as the section's own content. The set is IGDB
MATCH, EDIT DETAILS, JOURNAL, UPDATES, ABOUT, YOUR COPIES, EXTENDS and LISTS.
Disclosure headers, including EXPANSIONS and Technical facts, use 13px body type in `Text`.
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

The reading text takes `Text`: the gap rail's caption and its longitudinal record line, and the same
record line in the no-rail branch — §10.2 says everything the rail draws is restated in words
underneath, and §8's decorative-redundant rule makes those words the carrier of the fact; the
carrier of a fact is primary text. The no-rail sentence itself ("You've never opened this." /
"Steam has no date for your last session."), which is the whole of what Activity says about history when there
is no rail. The EXTENDS blurb ("A separate game, grouped for display.") and the EXPANSIONS
blurb ("Counted separately. Not added above."), both directly under a section heading — the
pair that prompted the change. The LISTS empty state ("Choose Add to list above to create a
list for this game."), a direction the user acts on (§7). The ABOUT summary — the
game's own description — and the empty-body line that stands in the same slot ("No description
yet. Metadata fills in automatically."). The metadata editor's intro ("Each field tracks its
own source, so editing one leaves the rest alone."). The no-way-in sentence in the header
("Winnow has not read this copy's install state yet." / "Winnow does not yet hold the
identifier this store needs to reach this game."), which is the whole of what the header says
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

**More uses `Button.secondary`.** It opens an in-app menu and takes the panel's `Text`-ink
treatment. Outbound destinations inside that menu use the shared menu-row treatment;
patch-note links within Updates retain `Azure`.

**The menu's presenter wears the `actions` class** from `Themes/controls.axaml` — the same
treatment the library grid's context menu wears, one set of setters covering both. The modal
and the grid agree instead of being two grammars.

**The menu floats.** It takes no row in the card's grid, so opening it does not change header
height or push the reading area. Further destinations cost menu height rather than header
width.

**The mark on the row the keyboard is on is drawn inside the item template**, never by the
adorner layer (§10.7): one step of fill above the menu's own ground (`SurfaceHigh`) plus a
2px `Volt` edge on a border whose thickness never changes. It answers two states, because a
menu opened from a button puts focus on its first row without selecting it: `:selected` covers
the pointer and the arrow walk, `:focus` covers where the keyboard lands when the menu opens.
Measured, not assumed — `docs/spikes/details-action-band-menu.md`.

**The header carries the frequent actions.** Play/Install, Add to list and More remain
visible while reading any tab. Store pages, patch-note hubs, installation management and
maintenance commands belong in More. Further controls join the menu by default.

**Keyboard.** The header's action order is primary action, Add to list, More, omitting
unavailable controls. The menu's rows are reached by opening it. Up and Down walk drawn rows,
Enter activates a row, and Escape closes the menu. Menu dismissal returns focus to More;
opening a focused tool then places focus in that tool.

**Refetch metadata reports outside the scrolling content.** The command lives in More and
its status appears in the modal's persistent footer, absent at rest. Status is words in
`TextDim`, with `Amber` for a refusal. A percentage would claim a total the operation does not
know. A successful write reloads the library and reopens the same ownership and tab carrying
its confirmation, because metadata and cover keys are computed during library loading.

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

The Library tab's ACQUIRED block draws the earliest known acquisition date and the licence
type in words when recognised — Steam Store, Complimentary, Gift or guest pass, Retail key.
An unrecognised licence says nothing rather than showing a stored token. Without acquisition
facts the block is absent. These are facts about the user's copy, so they belong with the
ownership rows rather than in the identity header.

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

**This panel has exactly one popup: the header's More menu (§10.3).** It is allowed because a
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
nothing is hand-drawn. The lightbox is therefore not a second exception alongside the More
menu. The menu is an exception because it is a popup that draws its own mark; the
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

**Tab order follows the tree, not `TabIndex`.** The persistent header is declared before the
tab content, so the keyboard reaches `Play` before the active tab's fields. Technical
identifiers are inside the Library tab's collapsed disclosure.

### 10.8 The patch notes panel

A patched game's `Patch notes` button on an Activity update row, and the `All patch notes`
row in More, open the notes in an embedded browser window rather than in the system browser.
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

**`Wrong game?` is a row in More (§10.3).** Its search field and candidate list occupy a
focused, full-width body in place of the tabs. Only one correction tool is shown at a time.
Clear remains with the copy's technical facts in Library, behind Installation & identifiers.

**The focused view carries an `IGDB MATCH` heading and its own close control.** Back to
details and the close control both return to the tab the user left. Opening the menu row
focuses the query field; choosing it again preserves the query, candidates and any same-game
offer. A landed assignment or link reloads the library, reopens details on the same ownership
and selected tab, and carries its confirmation to the footer.

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
draws full width in the focused body on one line: the 34x51 cover, then the name over the year
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

**Clear is drawn only while a pin stands**, read when details opens. It lives in Library's
Installation & identifiers disclosure. Clearing stops the pin without rewriting metadata,
then reloads the library and reopens details on the same ownership and tab so the cover key
can return to the store capsule. The metadata the pin wrote stays in place and the next
automatic pass fills what is empty around it.

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

**`Edit details` is a row in More (§10.3)**, beside `Wrong game?`. Identity correction
chooses which game this is; the field editor chooses what individual values should be.
The editor occupies a focused, full-width body in place of the tabs. It stays in the modal's
own visual tree, with a bounded scroll region and a Back to details control. Opening it again
preserves drafts in all six rows and does not reload already-loaded fields. Closing it or
pressing Escape returns to the retained tab without saving or discarding those drafts.

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
survives, the modal stays open on the same ownership, and the editor stays open — which
is precisely what a reload would have cost. The seam is optional like every other seam on this
modal: unwired, the save is exactly what it was. A carried confirmation appears in the modal's persistent footer while the tools are closed,
so a successful art save is visible after the rebuilt details returns to its selected tab.

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

`LISTS` and `LIVE LISTS` have collapsible headings with vector chevrons; their expanded state lasts
for the session. The empty rail says: *"No lists yet. Choose New list below to create a static
or live list."* The footer keeps **New list** on the left and the settings cog on the right.
New list pairs its label with a vector list-plus icon and offers **Static list** (choose games yourself) and **Live list** (save the current
library filters, with membership updating automatically), with a tooltip explaining each.

**The rail's grammar, which any rearrangement must preserve:** everything above the divider is
a subset of ALL GAMES; below it, content precedes work queue precedes configuration.

### 12.2 A list composes, a live list restores

**A manual list opens from its stored membership, not the bucket the user happened to be
viewing.** Selecting one clears the previous bucket. The panel and the search box can still
narrow the list after it opens.

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

### 12.3 Shared list modal

Naming a list, picking a list to add to, renaming one and confirming a delete use a shared
modal in the window's visual tree. The cut bar continues to describe the current filters.
The modal has a bounded, vertically scrollable list of 44px targets. Each row has a vector
list icon, name, mono count and trailing add icon. The rows and new-list field share both
edges; the scrollbar occupies the card padding. A divider separates existing lists from
creation. Long names truncate with their full name in a tooltip. Cancel / confirm sit below.
Focus stays within the modal and returns to its invoking control when it closes. Adding to
an existing or new list preserves the current view, scroll position and selection.

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

Feed cards and every game details view also offer **Add to list**, opening the same modal
for that game independently of library selection. Existing static lists and a new-list name
are available; live lists remain excluded. Adding a game from details refreshes its membership
checkboxes and leaves details open. Escape dismisses only the list modal.

Feed feedback occupies a dedicated right-hand column: bookmark-plus **Add to list** in Azure,
clock **Not now** in Amber, and circle-minus **Not interested** in TextDim. Each 32px icon
button has a tooltip and accessible name. Install / Play has its own line below the card text.
The bookmark and its inset plus use a 1px optical correction to share the apparent centerline
of the circular feedback icons.

In a static list, the game context menu offers **Remove from list**, acting on the selected
games and leaving them in the library. Live lists determine their own membership.

**Deleting asks first, and the question says what survives:** *"Delete "Couch co-op night"? The
titles stay in your library."* `Danger` appears on its confirm button and nowhere else on
the modal. Deleting a hand-added game (§16) is the other destructive act in the application.

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

**Nine themes ship; Winnow remains the default palette.**
A transparency **slider** sits beside them. Both settings live on the rail's
`SETTINGS › APPEARANCE` screen and persist in `settings`.

### 14.1 What a theme may change, and what it may not

**The role is the invariant; the colour is not.** §2 assigns every hue a job, and a theme may
change which colour plays a job. **It may never change what a job means, and it may never spend
one job's colour on a second one.**

**`Flare` is the load-bearing case.** It marks unread updates and the bucket that counts them,
in every theme, and **no theme's `Volt`, `Amber`, `Azure` or `Danger` may equal it.**
`ThemeContrastTests` asserts that for the original four calibrated themes, along with a minimum hue separation from `Danger`
(24°, the gap §2 already accepts for the default pair) and from `Volt` (60°).

**Two rules of construction carry across the original four themes.** Each one's `Volt` is its own room at
full voltage. And every theme's `Flare` is the one hue that room cannot produce.

### 14.1.1 The four calibrated themes, and the axes that separate them

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

**Five authored themes also ship:** Bottle green, SilkCircuit, SilkCircuit Dawn, Rosé Pine,
and Rosé Pine Dawn. Their palettes and appearance defaults match the authored theme files.
The Dawn variants are light and default to solid backgrounds. The contrast and dormancy
measurements for the original four do not certify these authored palettes: §5.1's dormancy
floor was calibrated against dark capsules on a dark field. Theme audit warnings remain
available in one disclosure, collapsed by default, labelled **Some themes may affect legibility.**
Theme file errors remain visible without expanding it.
An existing local theme with the same authored ID takes precedence without adding a duplicate
choice. Removing that local file restores the bundled palette.

In Appearance, restoring focus after inactive-window scrolling preserves the scroll offset.
Tab and directional keyboard navigation still bring the focused control into view.

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

**Zero is a real position, not an off state dressed as one.** It is
bit-for-bit the opaque palette with nothing carrying alpha, and it is the answer for anyone who
wants §8's floor with no argument — which is why the label under that end of the track is a
word, `SOLID`, and not an absence.

Without saved preferences, Windows starts at 30% Acrylic and other platforms at solid.
Authored theme defaults may override the quantity. Saved preferences take precedence.

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

**Over a dark desktop the number never gets worse** for the original four calibrated themes: the
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
(`acrylic` / `mica`, unset reads as acrylic) and `appearance.wall` (unset reads as *on*).
The reach choices put **Everything but covers** first, followed by **Frame and sidebars**.

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

**Floating is the default arrangement.** The panes may meet edge to edge as
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
palette, because the theme choices ask *which room*; two layout cards ask *what would
this arrangement look like in the room I am already in*, and a card frozen in the default
palette would answer a question nobody asked.

Floating appears first, to the left of Flush.
Persisted under `appearance.layout` (`flush` / `floating`; unset reads as floating). The debug
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

The gear at the foot of the rail opens `SETTINGS`, which holds four screens in this order:
**PLATFORMS**, **LIBRARY**, **APPEARANCE**, **APPLICATION**. Each is drawn the same way: a
48px header lining up with the command bar and the filter panel's header, its own scroll,
cards on `PaneGround`.

PLATFORMS is the store-connection screen. APPEARANCE is §14 and §15 — theme, transparency,
layout. LIBRARY holds four cards: **ACQUISITION EXPORT**, **EXPLICIT CONTENT**,
**HIDDEN GAMES**, **ADDED BY HAND**. Export saves the stored facts; the other three control
what appears in the library.


Steam connection settings use **Signed in** as the session heading while connected or renewing.
When renewal requires another sign-in, **Sign out** sits to the right of **Sign in again**.
A successful sign-in does not add a second confirmation block; actionable notices remain visible.

It is not under APPEARANCE, which changes material and layout and no data. It is not under
PLATFORMS, which is about connecting to a store; this is about what to do with what arrived.

APPLICATION holds operating-system behavior, metadata credentials, setup replay and application build information.
Its **NOTIFICATION AREA** card has separate, off-by-default toggles for hiding Winnow when it
is minimized and keeping it running when its window is closed. The notification-area menu
offers **Open Winnow** and **Exit**; Exit always closes the process even when close-to-tray is
on. Its **STARTUP** card offers **Start with Windows**. That registration is per-user and
starts Winnow quietly in the notification area after sign-in. Unsupported systems disable
the toggle and say why.

Application settings on both desktop and fullscreen also offer **Start in fullscreen**,
off by default. It opens the TV interface on the next normal launch; changing it does not
switch the current view. Windows sign-in and explicit background launches retain tray-first
behavior. Exiting fullscreen restores the desktop, and reopening a hidden window does not
reapply the startup preference.

The **IGDB METADATA** card offers **Get IGDB credentials**, labelled **Client ID** and
**Client secret** fields, **Save credentials** and **Remove saved credentials**. The secret
is masked, is cleared after saving or leaving the form, and is never loaded back into the
field. A polite status line reports saved configuration and failures without claiming that
Twitch has validated the credentials. Copy explains secure local storage and immediate
activation, with a metadata refresh queued in the background. Removing saved credentials
preserves any environment or local configuration fallback.
Fullscreen Application opens a dedicated **IGDB metadata** page with the same model and
actions, large fields and explicit controller focus rows. A opens the existing on-screen
keyboard for either field; secret entry retains its masking.

Its **ABOUT WINNOW** card shows **Version** and **Source commit** as selectable Data-font
text. The version retains prerelease labels; builds without source metadata say `Unavailable`
for the commit.

The **UPDATES** card offers **Automatic background updates**, on by default, and **Include
beta releases**, off by default. The descriptions read “Check GitHub Releases and download
updates in the background. Restart when you are ready.” and “Include preview releases. Turn
off to receive stable releases only.” Both preferences are shared with fullscreen Application
settings. A polite status line explains checking, download progress, readiness or failure;
available versions use Data typography. **Check for updates**, **Download update**, **Cancel
download** and **Restart to update** expose the current operation. Restart is always explicit.
**Release notes** and **Download in browser** open the official release destinations in the
system browser; unsupported installations use the browser download path. Fullscreen presents
the same controls as large ordered rows with its switch tracks, On/Off state and explicit
directional navigation. Status changes preserve the current row and never switch screens.

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
modal's More menu (§10.3). The context menu acts on the whole picked set and names the number
once there is more than one, exactly as `Add to list` does. The details menu acts on one game.

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

### 16.4 First-run setup

A new library opens a nine-step wizard: Welcome, IGDB, Steam, Epic, GOG, Theme, Application,
Library and Ready. Every configuration step is optional. **Continue** advances, **Back**
returns to the previous step, **Skip this step** advances without saving credential drafts,
and **Skip setup** finishes the wizard from any step. Welcome uses **Get started**; Ready
uses **Open my library**. Changes already saved remain in place when a step is skipped.
The current step resumes after closing Winnow; finishing or skipping the whole wizard stops
automatic display. Existing libraries are not interrupted on upgrade. **Run setup again**
in Application settings reopens Welcome without resetting preferences.

**Desktop.** The wizard sits over the client area, leaving the caption available. A compact
header carries the step count, title and explanation; the body scrolls inside a bounded
panel and navigation stays visible at the minimum window height. The normal shell cannot
receive input underneath it. Keyboard Tab cycles inside setup. Embedded platform dialogs
keep their own focus scope; Escape closes that layer first, then skips an optional setup
step. Returning from fullscreen restores wizard focus. The controller keyboard appears above
the wizard and preserves secret masking.

**Fullscreen.** A separate setup page uses the TV typography and explicit controller focus
rows. Each configuration step opens an existing provider or settings page; Back returns to
the same wizard step. Root navigation and the quick menu stay out of the flow while setup
is open. A selects, B goes back one layer or one wizard step, and the visible Skip controls
remain available. Completing setup returns to Library. Changing presentation retains the
shared cursor.

**Existing controls retain their meaning.** IGDB has an explicit Save action, a masked secret
and protected local storage; Save and its status remain visible outside the desktop field
scroller. Continue is not a second Save button. Theme and preference controls save as they
change. Copy explains that skipped items remain in Settings and that saved IGDB credentials
take effect immediately with metadata fetching in the background. GOG describes local Galaxy discovery without inventing a sign-in flow.
Steam's consent and disclosure text stays intact. Credential drafts clear when leaving a step
or presentation. A failed preference save can be skipped; a failed progress write keeps the
wizard open and explains how to retry.
