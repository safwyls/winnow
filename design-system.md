# Winnow — Design System

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
| `Well` | `#050D0E` | Scrollbar track, modal scrim — one step below Ground. *No longer the title bar (§9).* |
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

**The caption takes the rail's colour — `Surface`, the same ink and the same alpha.**

> **Amended (§14).** This section used to read: *"`Well` is one step darker than `Ground`,
> not lighter. Every desktop platform puts a lighter caption strip above a darker body,
> which means the brightest band in the window sits directly above the art. Inverting it
> makes the first inch of the window an unlit lip."* The objection it answers is real and
> still stands. The means were wrong.
>
> A `Well` caption above a `Surface` rail is **two chrome tones meeting at a corner**: a
> dark lip across the top, a lighter column down the left, and a visible seam where they
> join — three tones in the first inch of a window whose thesis is that the art is the only
> thing worth looking at. Painting both in `Surface` makes the chrome **one continuous
> bracket** and the cover wall a field recessed inside it. That is the same claim stated in
> one material instead of three tones, and it is the stronger statement of it: the window is
> now a frame with art in it rather than a stack of bands.
>
> **The lip is still unlit in the sense that mattered.** The rule was never "the caption must
> be the darkest thing" — it was "the caption must not be the *brightest* thing, and the art
> must be the first thing on screen with light in it." `Surface` is a chrome tone that no
> cover art comes near, and the wall it sits above is darker than it in every theme.
> `ThemeContrastTests.The_caption_is_the_rail` asserts both halves.
>
> **Same alpha, not merely the same colour.** With transparency up, two matching inks at two
> different alphas composite over the same backdrop to two different tones — which would put
> the corner straight back. `CaptionFill` *is* `ChromeSurface`, at every position on the
> slider.

> **Amended again (§15), and this time the amendment above survives rather than being
> reversed.** Under the **floating layout** the caption does *not* take the rail's ink. It
> takes `Well`, and so does every gap between panes. *(As first written this sentence also
> named the command bar and the cut bar; §15.8 moved both inside the library pane, and the
> caption is now the only strip on that ground — which is the section's own "it is a lip, not a
> toolbar" arriving from the other direction.)*
>
> The rule the previous amendment bought was *one continuous chrome field, with no seam in
> the first inch of the window*. It bought it by making the caption and the rail the same
> material, because they **met at a corner** and two tones meeting at a corner is a seam.
> Floating dissolves that corner: the caption and the rail no longer touch. What is
> continuous now is **the ground** — the caption and the gaps are one unbroken field, and the
> three content panes lie on it. The block is still there. It turned out to be the ground
> rather than the panes.
>
> **And the first inch is a caption again rather than a block of chrome (§15.8).** With the
> command bar taking this same ink at this same alpha directly underneath it, the two read as
> one tall undifferentiated strip — the seam was gone and so was the lip. Those controls
> operate the library, so they moved inside its pane, and what is left above the panes is 36px
> of caption.
>
> **And §9's older claim, the one the last amendment said was the real one, is served harder
> than before.** That claim was never "the caption must be the darkest thing"; it was *"the
> caption must not be the **brightest** thing, and the art must be the first thing on screen
> with light in it."* `Surface` satisfied it by being a chrome tone no cover art comes near.
> `Well` satisfies it by being the darkest tone in the window — below the field the covers
> hang in, not merely below the covers. The lip is more unlit under this layout, not less.
>
> **The two layouts therefore assert two different things, and both are tested.**
> `ThemeContrastTests.The_caption_is_the_rail` still holds for flush;
> `FloatingLayoutTests.The_caption_is_the_ground` holds for floating, and asserts the same
> property about seams pointed at whichever surface is carrying continuity.

> **Amended a third time (§16), and this time only one of the two layouts moves.** The window
> runs at **two** levels now rather than three: the ground and the caption at one, every pane at
> the other, and the chrome tier the rail used to sit on is gone.
>
> **Flush: this section stands exactly as written.** There is no window ground to be part of —
> the panes meet edge to edge and cover it — and the caption still meets the rail at a corner,
> which is the seam the amendment above exists to prevent. The caption is `ChromeSurface`, same
> ink and same alpha. What moved underneath it is which *tier* the rail is on, and the caption
> went with it.
>
> **Floating: the caption paints nothing at all past `SOLID`** and the ground shows through it.
> That is a stronger form of the last amendment's claim than the last amendment could make: the
> caption and every gap are not two surfaces that agree, they are one surface, over any backdrop,
> at every position on the slider.
>
> **And the older claim — "the caption must not be the *brightest* thing" — holds in flush and
> does not hold in floating over a bright wallpaper.** The ground is the most open surface in the
> window by construction, so the caption and the gaps are the brightest band in it together.
> §15.7 already conceded that for the gaps. It is what makes the caption the surface that sets the
> AA mark now, at 30 / 31 / 31 / 31 percent of the slider against white — where a selected rail
> row, which used to set it, now holds to 40 / 54 / 47 / 41. See §16.5 and §16.6.

`Well` survives, one step below `Ground`, on the two surfaces where a tone under the art
field is still the point: the scrollbar track and the detail modal's scrim.

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

**The one thing Winnow can draw that nothing else can.** Storefronts hold your last-played
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
and last reading Winnow holds, which is the part it actually watched happen, not the total
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
| No summary yet | `No description yet. Winnow fills the year, publisher and summary in from IGDB as it works through your library.` | `No data` |

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
asks about — friends, languages, Deck compatibility — is data Winnow does not
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

> **True in both layouts again (§15.8).** While the command bar floated on the window ground,
> the floating layout could only offer the panel's header rule and the library card's *top
> edge* on one scanline — §15.7's third honest cost. With the bar inside the library pane the
> two rules are the same kind of object at the same height, y=92 measured on the running
> window, under two 48px headers.

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
unchanged, and a transparency **slider** sits beside them. Both settings live on the rail's
`SETTINGS › APPEARANCE` screen and persist in `settings`.

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

### 14.1.1 Hue is the weakest axis, and the first set spent everything on it

The four themes that shipped first — Winnow, Cold storage, Nightshift, Phosphor — differed
in **hue and value and nothing else**, and read as four settings of one theme rather than
as four themes. Nightshift was Winnow with the lights off; Cold storage was Winnow lifted and
cooled. Two were withdrawn.

A room is separated by four things, and hue is the least of them:

| Axis | What it decides | Where it lands |
|---|---|---|
| **Temperature** | Which end of the wheel is ground and which is signal | Winnow and Nightshift cool · **Tungsten warm** · Box art neutral |
| **Chroma strategy** | How much colour the chrome is allowed at all | Winnow committed · Nightshift almost none · **Box art none, and the art is the only colour in the window** |
| **Value structure** | Where the contrast lives — stepped surfaces, or flat ones with the edges doing the work | Winnow 1.8x art→chrome · **Nightshift 1.4x, flat** · Tungsten 1.8x with the faintest edges · **Box art 4.8x, stark** |
| **Material** | What the chrome reads as | inked board · black glass · felt · mount card |

**The test of the set is that a thumbnail of the rail alone identifies the theme, with no
label.** If two are distinguishable only by hue, one of them is not earning its slot.

**No light theme, deliberately.** §9 inverts the platform's caption order so the first
inch of the window is an unlit lip, §5.3's tile scrim fades to `Ground`, and §5.1's
dormancy floor was calibrated against dark capsules on a dark field. A light theme is
not this table with the steps reversed; it is a second pass over all three, and half
of one would break the ramp that is the product's whole encoding.

| Theme | What it is, in one sentence |
|---|---|
| **Winnow** *(default)* | An inked green-teal stage, stepped evenly, dark enough that the cover art is the only lit thing in the window. The one tuned against six hundred real capsules. |
| **Nightshift** | Black glass: the surfaces stop stepping apart and every boundary becomes a drawn line, so the window is one dark pane with the layout scribed on it. |
| **Tungsten** | A warm room lit by one lamp — the only theme that is not cool. Edges nearly disappear and warm cover art settles into the field instead of standing off it. |
| **Box art** | A neutral mount with a 4.8x drop into a near-black art field. The chrome gives up colour entirely, so the covers — and the unread dot — are the only hues on screen. |

**Nightshift kept its name and changed its argument.** As shipped it was a value change and
nothing else. What makes it a room of its own is not how dark it is but **where the contrast
lives**: `Line` runs at 2.46:1 against the rail, the brightest edge in the set and nearly
twice Winnow's, while the art field, the rail and the caption sit within 1.4x of each other.
Tungsten is the same idea inverted — the faintest edges in the set, 1.38:1 — and the two are
unmistakable side by side.

**Box art is §1 taken to the end of its argument.** `Volt` is cold white light rather than a
colour, because a neutral room at full voltage is not a hue; `Amber` is a sand and `Azure` a
steel. Only the two colours that mean *stop* and *unread* keep their saturation, which makes
it the one theme where §2's rule is literally visible: `Flare` is not merely the hue the room
cannot produce, it is the only hue in the window that did not come out of a cover.

**Two costs, stated rather than hidden.** Tungsten spends the warm end of the wheel on the
ground, so `Volt` (brass, 43°) and `Amber` (ember, 16°) sit 27° apart — closer than any other
pair in the set, told apart by lightness and by where each appears. Box art has no second
saturated colour to spend, so `Volt` and `Azure` sit 29° apart and are separated by lightness
instead: `Volt` is a near-white at 17:1 against the art field, `Azure` a mid steel.

### 14.2 The five grounds

> **Amended (§16): there are two tiers, not three, and the table below describes the middle one
> as though it still exists.** `ChromeSurface` is a **pane** now — the rail and the filter panel
> take `WallGround`'s own alpha, exactly as the merge queue and the list view already did — and
> `ShellGround` is a **ramp** rather than a step, because the layer above it finishes early enough
> to keep the product linear. The rows still standing are `TileGround` (never), the popovers
> (never), and `ChromeRaised` (a veil, unchanged). Read §16 for what replaced the rest.

Which surface may admit the desktop is a **token**, not a rule somebody has to
remember. §13 gap 7 asked for "a named role for chrome that may be translucent as
opposed to surface that carries reading matter"; these are it.

| Token | What it backs | Translucent? |
|---|---|---|
| `ShellGround` | The client area below the caption | Paints **nothing** once the slider leaves zero — the columns over it paint their own |
| `WallGround` | The field the covers hang in | **Only when asked for**, and then ~~at half the chrome's reach~~ **at the pane tier — it admits 35%, and paints 0.588 to get there over the ground (§16.1)** |
| `PaneGround` | Merge queue, Stores, Appearance, the library's **list** view, the empty state | **Exactly `WallGround`** — same ramp, same setting. See §14.7 for why this changed |
| `TileGround` | Under the art stack inside one tile | **Never** — see §14.4 |
| `ChromeSurface` | Rail, filter panel, the list view's column-header strip | Yes — and **it is a pane (§16)**: `WallGround`'s own alpha, and its own unwalked `Surface` for an ink |
| ~~`ChromeGround`~~ | ~~Command bar, cut bar~~ — **retired (§15.8)**; both bars are inside the library pane and paint no fill | — |
| `CaptionFill` | The 36px title lip | Yes. **Flush** it *is* `ChromeSurface`, same ink and same alpha (§9). **Floating** it paints nothing and `ShellGround` shows through it (§16.5) |
| `ChromeRaised` | Hover / selection fill inside the rail, the panel and the list | Becomes a veil — see below |
| `ChromeRaisedHalf` | A *hovered* row where `ChromeRaised` is a selected one | The same veil at half strength |
| `ChromeFieldOnGround` | An input on the command bar or cut bar — search, the action bar's prompt. Its container is the **library pane** (§15.8) | Yes, at **the pane's own reach exactly** — see §14.7 |
| `ChromeFieldOnSurface` | An input in the filter panel — find, year, the option checkboxes | Yes, on the same terms — and **the same answer now (§16.1)**: the panel is a pane, so this alpha solves to zero too |

~~`ShellGround` is a **step, not a ramp**: two stacked alphas multiply, so a shell that faded
in proportion would stop the slider ever reaching its own end.~~ **Superseded (§16.3): the
premise is right about a ground that fades *in proportion* and this one does not.** The pane over
it rides `InkRampSpan`, so past the first quarter the product of the two is linear at exactly the
wall's rate. What the step was protecting — a pane compositing over the ground exactly *once* — is
asserted directly instead.

**Popovers keep an opaque fill.** A flyout is its own popup root and never receives the
window's backdrop, so a translucent fill there would sample the *application* rather
than the desktop and give a different answer at every position on screen.

**`ChromeRaised` is a veil, not an ink — and it is the one token that switches rather
than slides.** Opaque, a raised row is the ordinary `Surface → SurfaceRaised` step: an
ink that *replaces* what is under it. Translucent, a *darker* ink over an
already-translucent rail composites downwards and the selected row comes out darker than
the row beside it — elevation inverted. Those are two different operations, and
interpolating between them in ARGB walks through *mid grey at high alpha*, which is
neither: it crushed the metadata ink on a selected row to 4.2:1 six percent into the
track.

Only one veil is backdrop-independent. Solving `a·(V − rail) = λ·(Text − rail)` for every
possible `rail` gives `V = Text` and `a = λ`, so **the veil is `Text` and the only free
parameter is its strength.** It starts at exactly the strength that reproduces the theme's
own `Surface → SurfaceRaised` step over an opaque rail — derived per theme, so leaving
zero moves nothing — and grows to 10% as the rail opens up and there is more under it to
lift. §6's "elevation is the `Surface → SurfaceRaised` step" holds as a *relative* claim,
which is what it always was.

### 14.3 Transparency is a quantity, and it has its own inks

**The previous measurement was right and the conclusion was wrong.** `TextDim` on
`SurfaceRaised` at 85% over white is 3.1:1, and §13 gap 7 read that as "no reading
surface can be translucent". What it actually proves is narrower: *an ink chosen for an
opaque ground cannot have alpha subtracted from it.*

**And the control was wrong too.** The backdrop is a binary window hint, but nothing anyone
can *see* is: the perceived translucency is entirely the alpha on our own surfaces over that
backdrop, so it is continuous and ours to set. The checkbox that preceded this ran the chrome
at 86–91% and the verdict on it was that it "doesn't come across as transparency at all" —
correctly, at 14% of anything.

#### The material was the limit, not the alpha

Turning the alpha down was necessary and was not sufficient, and finding out why took a
measurement rather than an argument.

**Dark Mica cannot produce translucency at any alpha.** Windows composes it by tinting
toward its own near-black base so hard that the wallpaper contributes almost nothing:
back-solved from the composite behind our chrome on a real machine, the backdrop is
`#201F1E` **whether the wallpaper under the window is orange rock or blue sky**. At 30%
alpha — the far end of the slider — the rail lands on a neutral dark grey and reads as
*the chrome went grey*, not as the desktop showing through.

**So the hint order changed to `[AcrylicBlur, Mica, None]`.** Acrylic is a blur-behind at a
high radius, and at the same 30% the desktop is unmistakably present in the rail and the
caption.

That reverses a decision this document used to record, so here is the reversal in full. Mica
was chosen because it samples only the wallpaper — one image, bounded, measurable — and
acrylic was refused because it samples whatever is behind the window: *"no bound, no
measurement, and a rail whose legibility changes when the user alt-tabs."* The premise was
right and the conclusion was one generation out of date, for two reasons:

- **The tame backdrop was not a legible backdrop.** A material that is bounded because it is
  nearly opaque does not buy translucency; it buys a number.
- **The bound does not have to come from the material.** WHITE bounds *every* backdrop there
  is, wallpaper or window. The palette is measured against it across the whole slider, and
  the Appearance screen reports the worst case live and marks where it crosses AA. The real
  objection was to shipping a figure nobody could check — and the figure is on screen now.

Mica stays second in the list: a machine that refuses acrylic is better off with a tinted
backdrop than with none. `None` is the floor.

So: **a slider, 0 to 100, stored as a whole percent under the same `appearance.transparency`
key the checkbox used.** A stored `true` migrates to 25; a stored `false` to 0.

**Zero is a real position, not an off state dressed as one.** It is the default, it is
bit-for-bit the opaque palette with nothing carrying alpha, and it is the answer for anyone
who wants §8's floor with no argument — which is why the label under that end of the track
is a word (`SOLID`) and not an absence.

~~**The far end admits 70% desktop.** `MinChromeAlpha` is `0.30`.~~ **Retired (§16):
`MinChromeAlpha` no longer exists, because the tier it named no longer exists.** The far end
admits **85% on the window's ground** and **35% on every pane**, and the two are one identity
apart rather than two settings.

#### The two ramps, and why they are not the same ramp

Each translucent surface walks from its opaque token toward a **darker** ink, and `TextDim`
**brightens** to pay for what is left. The alpha and the inks travel on *different* curves,
and that is load-bearing rather than fussy:

- **Alpha falls linearly** across the whole track: `1 → 0.30`.
- **The inks finish in the first quarter** (`InkRampSpan = 0.25`) and then hold.

Alpha coming off lightens a dark surface over any brighter backdrop *immediately*, while a
compensation arriving in proportion is always behind it. Front-loading the inks moves the
point where the worst case drops under AA from **18% to 27%** on the default theme, and from
single digits on a selected rail row to that same 27%.

#### What it measures, and where the floor ends

| Default theme, `TextDim` on the worst chrome surface | Solid | At the AA mark (27%) | At the far end (100%) |
|---|---|---|---|
| Against **white** — the ceiling any backdrop can reach | 5.04:1 | **4.54:1** | 1.01:1 |
| Against a **dark desktop** (`#201F1E`, measured) | 5.04:1 | 8.17:1 | 6.73:1 |

Those are the brackets, and a real desktop sits between them. Measured on the running window
at the far end, over a wallpaper of lit orange rock against a blue sky, the rail's labels ran
**3.2:1 to 6.5:1** depending on what was behind that stretch of rail — which is what the two
numbers are for, and why neither is presented as *the* answer.

The worst chrome surface is a **selected rail row** — the rail with a veil over it, so the
lightest reading surface in the window and the first to lose its ink. §8 already singles it
out for the same reason on the opaque palette.

**Over a dark desktop the number never gets worse.** A dark backdrop is *darker* than our own
rail, so admitting more of it deepens the ground the labels sit on; `ThemeContrastTests`
asserts that at every position on the slider, for every theme. Against white it falls, and the
AA ceiling lands at 27% (Winnow), 30% (Nightshift), 30% (Tungsten), 26% (Box art).

> **Amended (§16.6): both the figure and the surface it is measured on moved.** The worst chrome
> surface used to be a selected rail row, and the rail was the most open reading surface in the
> window. It is a pane now, so it opens half as far and that row holds to **40 / 54 / 47 / 41**.
> What sets the mark instead is the **caption**, which sits on the window's ground — the most open
> surface there is — and the reported ceiling is **30 / 31 / 31 / 31**. Every theme keeps at least
> the range it had; three of the four gain, and Box art gains five points. The mark was never
> really about the rail: it is about whichever surface is most open and carries text.

**The range past the mark is a choice the user is allowed to make.** Being protected from it
is not a service, and being ambushed by it is not either — so the Appearance screen draws the
mark on the track and reports **both** numbers live, in Plex Mono `tnum`, with the worst-case
figure turning `Amber` and naming the line it crossed once it does. `Amber` and not `Danger`:
§2 gives `Amber` attention and `Danger` the one destructive act, and a setting chosen with the
number in front of you is neither an error nor something to be undone for you.

**Requested is not active.** Windows 10, a remote session and a compositor that refuses all
end with `ActualTransparencyLevel` reporting none of the levels that count — and Avalonia's
Win32 backend falls back to `Transparent`, not the `None` that was asked for, which is a
genuinely see-through window with nothing behind it. So the test names the levels that count
(acrylic, blur, Mica) rather than testing "not `None`". When the answer is no, transparency is
treated as zero and the settings screen says so in words. The preference is remembered either
way.

### 14.4 The dormancy ramp over a translucent window

§5.4's ramp is a two-layer opacity cross-fade, and the two layers are only opaque
*together*. Between the first bitmap decoding and the second, a dimmed tile is a partly
transparent tile — and on a translucent window that means the desktop showing through
the ramp's floor. Each tile therefore paints `TileGround` under its art stack, opaque in
every theme and every setting, so the ramp composites over exactly the ground it was
calibrated against. That is a fact of construction, not a measurement that could drift.

**This was belt-and-braces while the wall was opaque. Since §14.6 it is the only thing
holding**, so the tests assert it in both reach states rather than in the default one.
Verified on the glass as well: at the far end of the slider with the wall open and dimming
on, 187,192 pixels in the wall region differ from the same capture with the wall solid, and
**every one of them is the field or within 2px of a tile's antialiased edge**. Not one pixel
inside a tile changed.

### 14.6 Which material, and how far it reaches

The slider says **how much**. Two smaller decisions say **what of** and **how far**, and both
sit on the same Appearance card as qualifiers of the one quantity — not as two more rows.
Neither is drawn at all while the slider is at `SOLID`, because at `SOLID` neither does
anything, which keeps the common case to one control.

#### Acrylic or Mica, said in the UI and measured on the screen

§14.3 changed the hint order to `[AcrylicBlur, Mica, None]` and the reasoning was right, but it
settled a **default**, not an only option. Reading as a tone rather than as a view is a
legitimate thing to prefer — it is quieter, and it is what the rest of Windows 11 does — and
someone who wants it should not have to give up transparency to get it. So the head of the hint
list is the user's choice: acrylic asks `[AcrylicBlur, Mica, None]`, Mica asks
`[Mica, AcrylicBlur, None]`. **Acrylic stays the default**, for §14.3's reason: it is the one the
slider can be seen through.

**Mica is described by what it does, not sold as a lesser acrylic.** Back-solved from the pixel
on screen, at 45% over the same wallpaper in the same window position:

| Backdrop the window actually received | Under the lit rock | Under the sky |
|---|---|---|
| **Acrylic** | `#CC6E3A` | `#636573` |
| **Mica** | `#2D1C17` | `#201F24` |

Mica lands within a couple of units of the same near-black in both places, which is §14.3's
`#201F1E` measured again on a second wallpaper region. Acrylic carries the wallpaper and changes
across the window. That table *is* the argument for offering both, and a condensed form of it is
on the Appearance screen beside the choice.

**A substitution is a third answer, not the second one.** Mica needs Windows 11; acrylic works
further back. Asking for one and getting the other is better than getting nothing, so the window
still falls through — but the material that came back is reported **by name** and the screen says
so in an `Amber` field. Falling through is right; doing it silently is how a user concludes the
choice does nothing. The platform test stays positive per level (`== Mica`, `== AcrylicBlur`,
`== Blur`), never "not `None`", and everything else lands on `None` by default.

#### The pane may be translucent; the tiles may not

§14.2 used to keep `WallGround` opaque at every setting, on a §1 argument: a wallpaper behind six
hundred capsules is a second image competing with all of them. **That half was aesthetics and it
was overruled** by the person looking at the result — a solid slab bolted to translucent chrome
reads as two windows, which is the opposite of what the transparency was for.

**The other half was construction and it still binds** — it just binds somewhere else. §14.4's
cross-fade is answered by `TileGround`, not by keeping the field opaque. So the line is: **the
field may open up, the tiles may not.** Covers sit solid on an open field and the desktop shows in
the gutters between them.

The clause that used to follow — *"the list view, the merge queue, Stores and Appearance are text
sitting directly on it, so they take `PaneGround` and stay solid at every setting"* — **is
withdrawn, and §14.7 records the measurement that withdrew it.**

**The wall admits exactly half the desktop the chrome does.** `MinWallAlpha` is `0.65` against the
chrome's `0.30`.

> **Amended (§16): the constant survives and the relation it was stated in does not.** `0.65` is
> still exactly right and its derivation below is untouched — `1 − 0.65 = 0.35` is still what
> reaches the eye through the field. But "half what the chrome does" is now vacuous: there is no
> chrome for it to be half of, and every pane in the window admits this same 0.35. What the
> Appearance screen prints instead is the pair that is left — **the ground admits 85%, a pane
> admits 35%** — and the constant names an *admission* rather than a paint, because a pane is drawn
> on the window's ground and paints `MinPaneAlpha` `0.588` to get there. Polarity improved without
> being retuned, from 29 / 46 / 38 / 44 to **34 / 47 / 41 / 44**, because that ground darkens the
> field slightly on the way through. It is derived, not chosen by eye, and the constraint is not contrast — it is
**polarity**. §5.1's ramp is dark capsules on a dark field and only reads that way while the field
stays *darker* than the capsules on it. Over white the field climbs and eventually passes the
dormancy floor of an ordinary dark cover, after which a dimmed tile reads as a hole punched in a
lit field. The wall does not have to hold across the whole slider — past the AA mark the user has
already been told the labels stop clearing 4.5:1 — it has to not fail **first**:

| Per theme (Winnow / Nightshift / Tungsten / Box art) | | 
|---|---|
| Chrome's AA ceiling | 27 / 30 / 30 / 26 |
| Field inverts the ramp, at `0.60` | 25 / 40 / 33 / 38 — **Winnow fails two points early** |
| Field inverts the ramp, at `0.62` | 27 / 42 / 35 / 40 — the loosest floor that clears all four |
| Field inverts the ramp, at `0.65` | **29 / 46 / 38 / 44** — chosen |

`0.65` is taken over the marginal `0.62` because it is exactly half the chrome's reach, which is a
relation that can be printed on the settings screen and checked — and it buys 2 to 16 points of
margin on top.

**Measured on the running window, this is not only a white-wallpaper argument.** At 45% over a
real photograph the acrylic composite behind the wall back-solves to `#8E6251` under the rock and
`#9B827D` under the sky. At half reach the field lands at luminance **0.020–0.024** — under the
dormant capsule's **0.031** and under the rail beside it at **0.036**. At the chrome's own reach
the same field would land at **0.033–0.045**: above the dormant capsule, and level with or above
the rail. Full reach loses *both* invariants at once on an ordinary desktop — the ramp inverts,
and the art field stops being the recess §14.2 says the covers hang in.

Over the measured dark desktop the question never arises: the composite is darker than `Ground`,
so opening the field deepens it. `ThemeContrastTests` asserts that at every position.

**The Appearance screen prints both numbers** — ~~how much of the chrome is desktop, and how much of
the wall~~ **how much of the window's ground is desktop, and how much of a pane (§16.1)** — in Plex
Mono `tnum`, so the relation is visible rather than asserted. It is a ratio and
not a second slider on purpose: two percentages on one screen that mean different things is a
worse screen than one quantity with a stated relation.

Both preferences persist beside theme and transparency, under `appearance.backdrop`
(`acrylic` / `mica`, unset reads as acrylic) and `appearance.wall` (unset reads as *off*, which is
the previous behaviour and a real taste).

### 14.7 The panes take the field's ramp, and the fields take half of theirs

> **Amended (§16): the argument below was right and did not go far enough.** It moved the merge
> queue, Stores, Appearance and the list view onto the field's ramp on the grounds that they are
> content in the library pane's position rather than window furniture. **The rail and the filter
> panel are content columns by exactly the same test** — §11.1 calls the panel a peer of the rail —
> so they are on that ramp too, and the chrome tier is gone. Two consequences below are stale:
> `MinFieldAlpha` is **no longer a half** (its container changed tiers, so the identity now gives
> **zero**, the same answer `MinPaneFieldAlpha` already gave), and the ceilings in the table are
> measured against a chrome tier that no longer exists. The **identity** in this section is what
> survived, and it now governs three levels rather than two.

**The verdict that opened this: half a translucent window is worse than none of it.** With the
chrome and the art field open and every other surface solid, the window read as two applications
bolted together — the same complaint §14.6 already accepted about the wall, arriving one level
further in. The panes and the input fields were the surfaces still bolted shut.

#### `PaneGround` was measured against the wrong number

The rule it replaced was: *these are text sitting directly on the field, and §14.3's arithmetic
says an ink chosen for an opaque ground cannot have alpha subtracted from it.* **The principle is
right and the figure it was checked against was the chrome's.** The wall admits `1 − 0.65 = 0.35`
of the desktop where the chrome admits `1 − 0.30 = 0.70` — *less than half* — and the rail already
carries reading matter at the chrome's own reach, all the way to the AA mark the Appearance screen
draws. Text on the **wall's** field was therefore never the case that was measured.

Walked per theme against white, which is the ceiling any wallpaper can reach:

| Last whole percent still clearing 4.5:1 | Winnow | Nightshift | Tungsten | Box art |
|---|---|---|---|---|
| **Chrome**, `TextDim` on its worst surface *(what ships)* | 27 | 31 | 30 | 26 |
| **Pane**, `TextDim` on the open field | **59** | **71** | **65** | **73** |
| **Pane**, a *selected* list row (`ChromeRaised` over the field) | 43 | 54 | 50 | 55 |
| **Pane**, `Text` — a header, the empty state | 100 | 100 | 100 | 100 |
| **Input field**, `TextDim` — the placeholder | **71** | **75** | **73** | **71** |
| **Input field**, `Text` — what you are typing | 100 | 100 | 100 | 100 |

Every opened surface fails **later** than the chrome does, most of them by more than double. The
bar is the one `MinWallAlpha` is already held to — *not the surface that fails first* — and nothing
here comes near failing it. Over a dark desktop the question does not arise at all: the composite
is darker than `Ground`, so opening a pane *deepens* the ground its labels sit on, and
`ThemeContrastTests` asserts that at every position.

So `PaneGround` **is** `WallGround` — the same alpha, the same ink, and the same *setting*. It
answers `appearance.wall` rather than opening on its own, because a translucent Appearance screen
beside a solid grid is the original complaint in mirror image.

**Three surfaces did not move, and two of them were never in question.** `TileGround` stays opaque
because §14.4 is construction rather than measurement — it is the only thing standing between the
dormancy ramp's floor and the desktop, and a pixel diff on the running window confirmed it. The
popovers stay opaque because a flyout is its own popup root, never receives the window's backdrop,
and would sample the *application* — a different answer at every position on screen. And **polarity
does not reach the panes**: the merge queue is the only one that shows cover art, it shows it inside
an opaque `Border.card`, and it applies no dormancy ramp, because the question there is identity and
not recency.

**The list view was a fourth ground hiding inside the third.** Its outer grid declared `PaneGround`
and its `ListBox` painted opaque `Surface` straight over it, so the token was dead paint and the one
pane in the window wearing a *chrome* tone in the art's position — which §14.2's recess rule is
exactly about. The rows take `PaneGround` now and the column-header strip takes `ChromeSurface`, so
the list has the same structure the grid does: a chrome bar above, the field below. Both are their
opaque tokens at `SOLID`, so nothing moves there; the rows land one step darker than before, and
every ink on them gains — `TextDim` goes 5.88:1 to 6.69:1 on the default theme from the step alone.

Its row fills had to change with it. `SurfaceRaised` is an *ink*, and an ink over a field that can
open composites **downwards** over a bright wallpaper — the selected row would come out below the
row beside it, §14.2's inversion in the one place inside a pane it could still happen. They take
`ChromeRaised` and a new `ChromeRaisedHalf`, which *are* `SurfaceRaised` and its half at slider
zero. Walked, the elevation never once inverts, in any theme, at any position.

#### An input field is a target, so its number is stricter — and it is forced

A field is a **child** of the bar or panel it sits in, so the two alphas **stack**: what the desktop
finally contributes to a field is `(1 − containerAlpha) · (1 − fieldAlpha)`. That turns the field's
own alpha from a taste into an equation, once you ask the obvious thing — that a field admit what
the art field admits, so the window has *one* translucency and not three:

```
(1 − MinChromeAlpha) · (1 − MinFieldAlpha) = 1 − MinWallAlpha
         0.70         ·        0.50        =        0.35
```

~~**`MinFieldAlpha` is `0.50`**~~ — **it is `0` now (§16.1), and the third move is the point.** It
is derived from the other two, and `ThemeContrastTests` asserts the identity rather than the
constant, so retuning either end of the slider cannot leave a stale number behind — which is
exactly what happened here: the filter panel stopped being chrome, its container term went from
`0.70` to the wall's own `0.35`, and the half it used to spend vanished. Both fields in the window
now paint nothing past the ink ramp, by two routes to one answer. ~~Said in one sentence on the
Appearance screen: **a field admits half of what the surface around it admits**~~ — the sentence the
screen says now is **a field admits exactly what the pane around it admits**, and it gets there by
painting nothing at all.

> **Amended (§15.8): this holds for the filter panel's fields, and the command bar's field now
> answers the same equation with a different container.** Writing the identity generally,
>
> ```
> fieldAlpha = 1 − (1 − MinWallAlpha) / (1 − containerAlpha)
> ```
>
> the panel is chrome, its container term is `MinChromeAlpha`, and the answer is the half above.
> The search box and the cut bar's prompt moved **inside the library pane**, so their container is
> `PaneGround` — which is `1 − MinWallAlpha` already. There is no half left for a field to spend:
> **`MinPaneFieldAlpha` solves to zero**, and past the ink ramp that field paints no fill at all.
>
> **The two-term form did not survive the move, and that is the lesson rather than the cost.** It
> was never a fact about fields; it was a fact about what was underneath them, and what was
> underneath them changed. The rule is the identity; the constant is whatever the identity gives
> once you say honestly which surface the field is drawn on.
>
> What it costs is small and already recorded below: *a field is found by its border, and lit by
> its ring* — the fill and the surface around it converge to 1.05:1 at the far end anyway. `SOLID`
> is untouched, so the step a field cuts into the pane is exactly the step it always was, and it
> fades out on `InkRampSpan` like every other compensation here.
>
> **And it follows the wall's setting rather than the slider's.** The old field followed the slider
> because it sat on a bar that was open whatever the wall was doing. This one does not: with the art
> field solid the pane under it is solid, the identity is vacuous — nothing is being admitted for the
> field to match — and a field that faded anyway would lose its step for no gain.
>
> **One more thing was carried over and should not have been: the ink.** The chrome's inks *walk*
> (§14.3) because the chrome opens to 0.70 and pays for it; `PaneGround` does not walk at all — it is
> the theme's own `Ground` at an alpha, at every position. A field cut into an un-walked ground must
> be un-walked too, or the step between them changes size across the slider. Caught by
> `ThemeContrastTests`, not by reading.

**And it holds across the slider rather than only at its end, because this factor rides the INK
ramp.** The bar's share of the desktop is already linear in the slider position; the moment the
field's factor stops moving, the product of the two is linear at exactly the wall's rate. On the
alpha's own linear ramp the product would be *quadratic*, and a field would sit at half the wall's
openness through the middle of the track — which is the part anybody actually uses. So the field's
alpha finishes in the first quarter, on `InkRampSpan`, for a sharper version of §14.3's reason: a
compensation arriving in proportion is always behind. Slider zero is still bit-for-bit opaque, and
nothing jumps leaving it.

**The fill is the chrome's other ink, not a fourth colour.** A field on the command bar takes the
rail's ink; a field in the filter panel takes the command bar's. That is one step from its container
in the neutral family, in the direction the opaque palette already took — so slider zero is
bit-for-bit unchanged — and both are the **walked** inks, so a field darkens as the chrome does and
its text is paid for by §14.3's two ramps rather than by a third one.

**A field is found by its border, and lit by its ring.** Over a dark desktop the fill and the bar
converge — 1.05:1 at the far end — but they did that before this change too, at 1.14:1, because a
chrome tone one step from another chrome tone over the same backdrop was never what drew a field.
`Line` draws it and `Volt` says it has the caret. **Focus stays §10.7's brush swap on a border whose
thickness never changes** (§13 gap 6 records that §8 and §10.7 disagree; §10.7 is what the rest of
the app follows): thickening it would reflow the whole command bar every time the caret landed. The
ring clears AA on the field to **89 / 94 / 91 / 100** per cent of the slider against white, and past
a few percent it reads *better* on the field than on the bare bar — the field is the darker of the
two, which is the same property that lets it open at all.

#### One thing this found that predates it

Checking the placeholder — the dimmest ink any field carries — turned up a failure older than any
of this. **The year field's watermark was `TextFaint`, which measures 4.13 / 3.69 / 3.58 / 4.12 on
the *opaque* ground: under AA at `SOLID`, before transparency exists.** §2 gives `TextFaint`
watermarks and disabled arrows; a year hint the user is expected to read is neither. It is `TextDim`
now — 6.69–7.66 opaque, and AA to 71–76% of the slider on the open field — which is where every
other placeholder in the app already was.

### 14.5 Every themeable brush is declared as an attribute

`<SolidColorBrush x:Key="X">#16282A</SolidColorBrush>` and
`<SolidColorBrush x:Key="X" Color="#16282A"/>` look identical and are not: Avalonia's
XAML compiler constant-folds the first into an `ImmutableSolidColorBrush`, whose colour
cannot be written. A theme change works by writing `Color` on the brush objects the
views already resolved — `StaticResource` looks up once and never again — so a folded
brush is a token the theme system silently cannot reach. Measured, not assumed: the
first build had thirty-five of them, and the symptom was a window that half repainted.


---

## 15. The floating layout

**A second arrangement, behind a setting, default off.** The panes may meet edge to edge as
they always have, or the **content** regions may detach into rounded cards with a uniform gap
around each, on a window ground that runs unbroken behind the caption, the command bar and
every gap. VS Code shipped this in Aug 2026; JetBrains ships the same thing and its users call
the result *islands*.

**It is structure, and the two settings it sits beside are not.** §14's theme is *material* and
its slider is *quantity*. This is neither: it applies in every theme at every position on the
slider including `SOLID`, and it changes no colour that either of those two was measured
against. That is why it is a peer of THEME on the Appearance screen rather than a third
qualifier hanging off the slider — see §15.5.

### 15.1 What floats, and what stays flush

**The line is content against chrome, not big against small.** The reference draws it in
exactly one place and this follows it: the title bar spans the full width and does not float,
neither does the activity strip nor the status bar, and what detaches is the sidebar, the
editor and the secondary panel. Mapped onto this window:

| Region | Floating | Why |
|---|---|---|
| **Caption** | Flush, full width | Chrome. It is a lip, not a pane (§9) |
| **Command bar** | ~~Flush~~ → **inside the library card** | **Revised, §15.8.** They operate the library and nothing else, so they are its header |
| **Cut bar** | ~~Flush~~ → **inside the library card** | Revised on the same rule |
| **Rail** | **Card** | Content — the bucket, list and settings axis |
| **Cover wall / list view / empty state** | **Card** | Content |
| **Merge queue · Stores · Appearance** | **Card** | Content; they replace the library pane and take its island |
| **Filter panel** | **Card** | Content, and a peer of the rail (§11.1) |
| **Detail modal** | Full bleed | A modal covers everything, gaps included |

The command bar was the one judgement call, and it was settled the wrong way. **See §15.8.** The
reasoning below is kept because half of it survived: giving the bar *a card of its own* would indeed
have made the controls a fourth region competing with the pane they operate, and it would indeed
have broken the ground's continuity. What it missed is that those were not the only two options.

> **The original text.** *"The command bar was the one judgement call, and it is settled: flush.
> Giving it a card would have made the controls a fourth region competing with the pane they
> operate, and it would have broken the ground's continuity across the top of the window — which is
> the whole of what makes the panes read as floating."*

### 15.2 The fifth ground

The palette has four neutral steps and the flush layout already spends three of them: `Ground`
for the art field, `Surface` for the chrome columns, `SurfaceRaised` for elevation. The gap
between two panes needs a tone of its own, and there is exactly one left.

**`ShellGround` is inked `Well`** under this layout — the tone §9 already keeps for *"where a
tone under the art field is still the point"*, joining the scrollbar track and the modal scrim
in a third use that is the same use. The caption, the command bar and the cut bar take the
same ink at the same alpha, so at `SOLID` the four are one painted field with no boundary
anywhere in them.

> **Amended (§16.5): the caption does not take the ground's ink at an alpha of its own any more —
> it takes no fill at all, and this ground is what shows through it.** So the claim holds at every
> position on the slider rather than at `SOLID`: the caption and every gap are one *surface*, not
> two that agree. (The command bar and the cut bar left this field for the library pane at §15.8;
> the caption is the only strip on it.) That is not a shortage answered by the only remaining option; the deepest
tone is the *right* one for the space behind everything, and it is what makes a gap read as a
recess rather than as a missing pane.

The order that follows is one direction and holds in every theme:

```
Well  <  Ground  <  Surface
gap      the art    the chrome
         field      panes
```

**§5.1's polarity is untouched.** The wall island is `WallGround` exactly as before, and the
capsules sit on exactly the field they were calibrated against. What is new is that the field
now has something *below* it, which is a fact about the gaps and about nothing else.

### 15.3 Geometry: 8 and 8, and why each

**The gap is 8px.** It is §4's own spacing step, it is the smallest step on that scale that
reads as a gap rather than as a badly-drawn rule at 100% scaling, and — the part that decides
it — **it is exactly the width of the resize band §9.1 measures.** One number solves two
problems: a pane inset by it is a pane none of whose controls the OS can eat.

**One pane owns each gap.** Half the gutter from each of the two neighbours was tried first
and is wrong for a reason that shows up in exactly one state: the filter panel is not always
open, so a library pane carrying half a gutter on its right came out with **four** pixels
between it and the window edge whenever the panel was closed — measured on the glass, not
reasoned about. So the rail gives up its right margin, the library pane owns both of its own
gutters, and the filter panel gives up its left one. Every gap is 8 in every state, and no gap
is the sum of two margins that can go out of step.

**The radius is 8px**, above the tile's 6 and the control's 4. Radius reads as a proportion of
the corner it turns: 6px on a 750px-tall column is a chamfer, not a round. The three radii
still rank by the size of the object they round, which is the rule §4 was already stating with
two of them.

**The rail's column becomes `Auto` with the pane carrying its own 220.** Taking the margins
out of a fixed 220 column would have taken them out of the rail's content — 220 of chrome
becoming 204 of it — so every label in the rail would move when the layout changed. The column
widens; the rail does not narrow.

### 15.4 §9.1 is retired here, not kept

`ScrollBarEdgeInset` steps every window-edge scrollbar 10px in, because Windows answers the
outer 8px with `HTRIGHT` before the client area sees the pointer. **§9.1 states that rule about
which edge a control is on, never about which control it is** — and floating moves every one of
these scrollbars off the window's edge and onto a pane's. Eight pixels of gap plus the pane's
own border is already outside the band, so the inset would be a second, visible 10px gutter
inside an 8px-inset card: a scrollbar touching nothing. It is dropped under this layout and
kept under the other, which is the rule doing what it says rather than an exception to it.

### 15.5 Where the setting lives

Its own `LAYOUT` section on the Appearance screen, **under THEME and above TRANSPARENCY.**

The two controls under TRANSPARENCY are *qualifiers*: they are meaningless at `SOLID`, which is
why they are not drawn there at all. Layout is not a qualifier of anything, so a fourth row
inside that card would have said it depended on a quantity it does not depend on. Structure is
also what you read first — you see how a window is put together before you notice what it is
made of.

**It is drawn the way THEME is drawn and not the way the qualifiers are**, on this screen's own
established rule: a qualifier is a *consequence* and the honest way to show a consequence is to
say it in a sentence; a layout is a *shape*, with no colour and no number in it, so the
miniature is not an illustration of the setting — it is the setting at 1/8 scale. Two cards,
one template, and exactly the four values the layout changes bound out of the view model: the
ground, the margin, the radius, and where `Line` falls.

**One place a layout card deliberately differs from a theme card.** A theme card draws its own
fixed palette, because four of them side by side ask *which room*. A layout card is repainted
from whichever theme is up, because two of them ask *what would this arrangement look like in
the room I am already in* — and a card frozen in the default palette would answer a question
nobody asked.

Persisted under `appearance.layout` (`flush` / `floating`; unset reads as flush). The debug
capture flag is `--layout=flush|floating`, session-only and sealed against writing like every
other one (§14.3's seal).

### 15.6 What it costs, measured

~~**Nothing §14 measured moved.**~~ **Superseded (§16.7), and the replacement is a measurement
rather than a construction.** The claim held while floating merely repainted the caption in a
deeper tone. Floating puts the caption on the window's **ground** now — the most open surface
there is — so the two layouts no longer fail in the same place: 30 / 31 / 31 / 31 against flush's
56 / 69 / 61 / 57. `Colorimetry.AaCeiling` therefore walks **both** layouts and reports the worse,
so the mark on the track means one thing whichever layout is up and flipping the layout can never
invalidate it. The polarity floor and the dormancy ramp are still layout-free.

~~The original claim.~~ The AA ceiling is computed off a selected rail row, the
polarity floor off the wall against a dormant capsule, and the dormancy ramp off `TileGround`;
the layout touches none of the three. It moves ~~four tokens — `ShellGround`, `CaptionFill`,
`ChromeGround` and `ChromeFieldOnGround`~~ **two tokens, `ShellGround` and `CaptionFill`
(§15.8)** — and `FloatingLayoutTests` asserts every other token is bit-for-bit identical between
the two layouts, at every position on the slider, with the wall in and out.

**The chrome strip gains contrast rather than losing it.** The caption is repainted from `Well`
instead of `Surface`, and `Well` is the darkest tone in the palette — so over the brightest
backdrop a wallpaper can be, every ink on it lands on a deeper ground than it did. Asserted at
every position, per theme: the layout cannot be the thing that takes a label under §8's floor.
It is the only strip left to check; the command bar's labels are on the library pane in both
layouts, so the layout does not reach them.

~~**§14.7's forced field identity survives, and it was re-derived rather than assumed.**~~
**Superseded by §15.8, and the supersession is the point.** The re-derivation done here was
correct *for a command bar sitting on the window ground* — "the command bar was **never inside a
pane** in either layout", so the sum kept its two terms and the ink stepped `Surface → Ground`
with the bar. The bar is inside a pane now, in both layouts, so the premise is gone: the sum
loses a term and the ink stops depending on the layout at all.

**The token count went four to two.** This layout used to move `ShellGround`, `CaptionFill`,
`ChromeGround` and `ChromeFieldOnGround`. `ChromeGround` no longer exists and
`ChromeFieldOnGround` no longer varies with the layout, so what floating changes is the ground
the panes lie on and the caption — and `FloatingLayoutTests` asserts every other token is
bit-for-bit identical between the two.

**The panes never composite TWICE, and that is the whole construction.** A painted ground behind
translucent panes would stack: a rail at §14.3's measured `0.30` over a shell at `0.30` lands at
`0.51`, and every figure the Appearance screen prints would describe a window that is not on
screen.

> **Amended (§16.3): "exactly once" is the rule, and "a step, not a ramp" was one way of getting
> it.** `ShellGround` paints a fill at every position now. What makes that safe is that the pane
> over it rides `InkRampSpan`, so the product of the two alphas is linear at the wall's rate past
> the first quarter and lands on `1 − MinWallAlpha` at the end of the track to the last decimal.
> The *once* is asserted directly in `FloatingLayoutTests`, at every position, in both layouts and
> both reach states — which is a tighter guarantee than a step was, because a step could be
> defeated by a second element declaring the token and nothing would have caught it.

### 15.7 The honest costs

Three, and none of them is fatal:

~~**At `SOLID` the ground is one field; past it, it is a field with brighter slots cut in it.**~~
**Repealed (§16.5).** The cost was real and its cause was the middle tier: a gap took no fill of
ours and admitted the whole desktop, where the caption beside it admitted the chrome's
70%-at-most, so the ground was one tone at zero and two everywhere else. The caption paints no
fill either now, and the same `ShellGround` shows through both — one field at every position on
the slider, not one at zero. What is left of the honesty is the other half of the sentence, and it
is in §16.5: over a bright wallpaper the ground is the brightest band in the window, and the
caption is now part of it.

**The gap tone does almost no work under the library pane.** Measured, `Well`-against-`Ground`
comes out at 1.13:1 in Winnow and 1.02–1.06:1 in the other three, so what makes the wall island
float is its **1px `Line` border**, not the gap. Against the rail the gap does read on its own
(1.28:1 in Winnow, 1.29:1 in Box art) because `Surface` is two steps up. Fixing it would mean
lifting `Ground`, which is the tone §5.1's polarity is calibrated against — so it is not fixed,
it is stated. **Tungsten is the weakest of the four**: it has the second-faintest gap tone and,
by design, the faintest `Line` in the set (1.58:1 against the gap), so its library pane floats
less than any other. Nightshift and Box art draw loud lines and come off best.

**§11.1's rule across the window is now a join rather than a continuation.** Beside the rail,
the filter panel's 48px header rule *continued* the rule under the command bar straight across
the screen. Floating breaks continuations by construction — that is what a gap is. It lands
better than expected: the filter panel takes the **rail's** top margin rather than the wall's,
which puts its header rule and the library pane’s top edge on the same scanline (y=92, measured on the running window), so the line still
crosses the window, now as three collinear segments rather than one. It is a weaker statement
of the same thing, and it is a real cost of the layout rather than a free win.

> **Mostly repaid by §15.8, and measured the same way.** The scanline is still y=92, but what
> lies on it in the middle column changed: it was the library card's *top edge*, and it is now
> the **command bar's own rule inside the card** — the same kind of object as the panel's header
> rule, at the same height, under a header of the same 48px. The three panes' top edges have all
> moved up to y=44 together. So the line crossing the window is once again a header rule meeting
> a header rule, rather than a header rule meeting a pane edge, and §11.1's claim is a
> continuation again in everything except the 8px the gaps take out of it.

### 15.8 The command bar belongs to the library, not to the window

**Looked at, and revised.** §15.1 put the command bar and the cut bar flush on the window ground
with the caption, on a content/chrome line borrowed from the reference. On screen that produced
**a tall undifferentiated block of chrome in the first inch of the window** — a caption strip and a
control strip in one ink, flush together, above three panes that all started somewhere else. The
line was drawn in the right place for the reference and the wrong place for this window.

**Those controls are not window chrome.** Search, layout, density, display, sort and `Filters` all
act on the library and on nothing else. They are the library pane's **header**, so they are inside
its card — in *both* layouts, because which pane a control belongs to is a fact about what the
control does and not about whether the panes are inset.

**What it buys, in the order it matters.**

- **One top edge.** The rail, the library and the filter panel now all begin on the same scanline
  immediately under the caption. That was the stated goal and it is the visible result.
- **The caption is a lip again**, which is all §9 ever asked it to be. It is the only strip left on
  the window ground, so §15.2's continuity claim is made by the caption and the gaps alone.
- **A visibility rule became a fact of composition.** The merge queue, Stores and Appearance
  replaced the library *specifically* so they would not sit under a command bar whose search and
  sort mean nothing to them — a claim four `IsVisible` bindings had to keep agreeing on. The bar is
  inside the library's own `Border` now, so no arrangement of those four panes can put a settings
  screen under the library's controls. There is no parallel rule left to keep in step.
- **The layout stopped needing to say anything about the bars.** Floating used to repaint them,
  margin them 8px in to line their gutter up with the pane below, and strip their bottom rule
  because there was a gap under them rather than a pane. All three overrides are gone. A setting
  that no longer has to compensate for a control's position is the strongest available evidence
  that the position was wrong.

**The cut bar goes with it, under the command bar and above the art.** It describes the library's
current cut, so it belongs to the library's pane; and inside the pane the order reads downwards as
cause, claim, consequence — the controls, then what they did, then the result. `926 → 41` also lands
against the tiles it is counting rather than a strip further away from them. Both bars keep their
1px rule in both layouts, because in both layouts there is art directly under them.

**Applied to the flush layout as well, and not for symmetry.** Flush already painted the command bar
in the art field's own ink with a rule under it, so at `SOLID` the move costs **zero pixels** — it
ratifies what that layout was already asserting. What changes is the alpha, and that is a
correction: the bar used to take the *chrome's* reach while the wall it sat on took the wall's, so
with the art field solid and the slider up you got a see-through strip glued to the top of a solid
field — §14.7's "half a translucent window" arriving one level in. It opens with the pane it belongs
to now, or stays solid with it.

**`Filters` still toggles a sibling island from inside the library pane.** Slightly odd, and left
alone: it is where the control has always been, moving it is a larger change than this one, and the
panel's own edge is directly under the toggle either way. Worth revisiting only if the rail ever
grows a second thing that opens a column.

**What the ink cost.** The bar was painted from the chrome's ink because it was chrome; on the pane
it is on the art field's ramp, which every ink on it **gains** from — §14.7 measures a pane's
`TextDim` failing at 59–73% of the slider against the chrome's own 26–31%. `ChromeGround` is
retired: the pane paints its ground once and the bars sit on it, because a second coat of the same
ink at a second alpha is the double composite `ShellGround` is a step and not a ramp to avoid. The
field on those bars is re-derived in §14.7's amendment. Nothing the Appearance screen reports moved:
the AA ceiling is 27 / 31 / 30 / 26 before and after.

> **Those figures are the last ones this document reports for the three-tier window (§16).** The
> reported ceiling is **30 / 31 / 31 / 31** now, and a selected rail row — which set the old
> figures — holds to **40 / 54 / 47 / 41**. The bar moved because the rail did.

---

## 16. Two tiers, not three

**Asked for on aesthetic grounds and it re-derived four constants.** The request was: *"make
the rail and filters panes the same level of opacity as the game grid/main pane. Then the
background and titlebar should be the same, and somewhere between where the background is now
and the rail is now."* What shipped ran at three levels, and the eye expects two:

| | opened to | who |
|---|---|---|
| ground · gaps · caption | 100% in the gaps, 70% on the caption | the shell |
| **chrome** | **70%** | **rail, filter panel** |
| wall | 35% | library pane, and the screens that share its place |

The middle tier had no job left to do. §14.7 already moved the merge queue, Stores, Appearance
and the list view onto the field's ramp, on the argument that they are *content in the library
pane's position* rather than window furniture. **The rail and the filter panel are content
columns by the same test** — §11.1 calls the panel "a peer of the rail", and the rail owns the
bucket, list and settings axis. Nothing about them is chrome except a token name.

So there are two, and one rule generates every number in them.

### 16.1 One free quantity, and everything else forced

A surface painted on another surface **stacks alphas**: what the desktop finally contributes is
the product. §14.7 already used that to fix an input field's alpha; the same identity applies one
level further out, to a pane on the window's ground:

```
alpha = 1 − (1 − MinWallAlpha) / (1 − containerAlpha)
```

The window's ground is the only surface with **nothing above it**, so it is the only free
quantity. Everything under it is forced:

| | admits | paints | forced by |
|---|---|---|---|
| `ShellGround` — the gaps, and the caption | **85%** | `MinShellAlpha` **0.15** | nothing. This is the choice |
| any pane — rail, filter panel, art field, settings screens | **35%** | `MinPaneAlpha` **0.588** | the ground |
| any input field, in a pane or in the panel | **35%** | `MinFieldAlpha` **0** | the pane it is cut into |

`MinWallAlpha` is still `0.65` and **its derivation is untouched**, because `1 − 0.65 = 0.35` is
still exactly what reaches the eye through a pane. What changed is that the constant now names an
*admission* rather than a paint. §14.6's polarity argument reads word for word.

### 16.2 What fixes the ground, since the identity does not

The ground answers to one thing: the caption, which carries the wordmark and three window glyphs
and is the only reading matter on it. So the bar is the mirror image of the one `MinWallAlpha` is
held to — **the restructure may not cost the user range they already have.** Walked per theme
against white:

| Ground opens to | AA ceiling (Winnow / Nightshift / Tungsten / Box art) | |
|---|---|---|
| 0.12 | 29 / 30 / 30 / 30 | Nightshift loses a point |
| **0.14** | 29 / 31 / 30 / 30 | the marginal value — two themes exactly at par |
| **0.15** | **30 / 31 / 31 / 31** | **chosen** |
| 0.20 | 32 / 33 / 33 / 33 | more range, less window |

`0.15` is taken over the marginal `0.14` for the reason `0.65` was taken over `0.62`: it is the
round step past the boundary, it buys 1 to 5 points on top, and it states as a pair of numbers
the Appearance screen prints — **the ground admits 85%, a pane admits 35%.**

**A second route lands within a point and a half of it.** The request was for a value *between*
admitting everything and admitting the old chrome's 70%. Transmittances compose by **multiplying**,
so the midpoint between two of them is the geometric mean, not the arithmetic one:
`√(1.00 × 0.70) = 0.837`, an alpha of `0.163`. The legibility boundary and the honest reading of
"halfway" agree, and the more conservative of the two is taken.

### 16.3 The pane rides the ink ramp, or the two tiers pull apart in the middle

`ShellGround` used to be a **step** — nothing at all past slider zero — because two stacked alphas
multiply and a pane on a proportional ground would admit a *quadratic*: 8.75% at the middle of the
track where it should admit 17.5%, so the tiers would sit twice as far apart through the part of
the slider anybody uses as they do at its end.

That is a fact about a ground that fades in proportion. It is not a fact about this one, **because
the layer above it finishes early.** The ground's share is already linear in the slider position,
so the moment the pane's factor stops moving the product is linear at exactly the wall's rate:

```
t     0.10   0.20   0.25   0.40   0.60   0.80   1.00
pane  1.4%   5.6%   8.8%   14.0%  21.0%  28.0%  35.0%
0.35t 3.5%   7.0%   8.8%   14.0%  21.0%  28.0%  35.0%
```

Sub-linear under the first quarter, which is the safe direction, and it meets the linear part
exactly at `InkRampSpan`. That is §14.7's own argument for the field's ramp, promoted one level.

**What the step was really protecting is still protected and is asserted directly:** a pane
composites over the ground **exactly once**. `FloatingLayoutTests` walks it at every position, in
both layouts, in both reach states. A second coat — a second element declaring `ShellGround` — puts
every figure the Appearance screen prints out by the same factor and nothing else would catch it.

**The ground's ink bleeds into the panes, and it was measured rather than waved past.** Some
fraction of every pane is `Well` rather than its own tone: 9.5% at the far end, at most 34% in the
middle where the pane is still nearly opaque. Against the same pane painted straight onto the
desktop the worst tone difference is **1.06 to 1.11:1** — under the `Well`-to-`Ground` step itself,
which §15.7 measures at 1.02 to 1.13:1 and calls nearly invisible.

### 16.4 The rail's ink ramp is retired, and it had to be

§14.3's ink ramp is a **chrome** compensation: the chrome opened to 0.70 and paid for it with a
darker ink. There is no chrome. And the ink it walked toward is worse than redundant —
`TranslucentSurface` is **below `Ground`** in three of the four themes, so at the alpha the rail now
shares with the art field the walked chrome would sink *under the field beside it*. Measured, over
white: the walked rail is at or below the wall at **87 to 89 of the 101 slider positions** in Winnow,
Nightshift and Tungsten; the unwalked one at **none** of them, in any theme.

§14.2's recess — the art hangs *below* the chrome — is therefore carried by the **ink** now rather
than by the alpha: `Surface` over `Ground`, both unwalked, at one shared alpha, in every theme at
every position. `ChromeSurface` takes the treatment `PaneGround` has always had.

**`TranslucentSurface` is retired with the tier it belonged to**, exactly as `ChromeGround` was when
the command bar moved. The field stays on the record and in the theme format so that no user theme
needs editing; nothing reads it. `TranslucentChromeGround` is untouched and does more work than
before — it is the ground's own walked ink, in both layouts.

### 16.5 The caption, which is the risk and is now the derivation

**Floating: the caption paints nothing at all and the ground shows through it.** That is stronger
than the claim §15.2 used to make. The caption used to carry the ground's *ink* at the *chrome's*
alpha while the gaps beside it carried no fill, so §15.7 had to record an honest cost: *"at SOLID
the ground is one field; past it, it is a field with brighter slots cut in it."* There are no slots
now. The caption and every gap are not two surfaces that agree — they are one surface, over any
backdrop, at every position on the slider. **§15.7's first cost is repealed.**

**Flush: §9's amendment stands exactly as written.** There is no ground to be part of — the panes
meet edge to edge and cover it — and the caption meets the rail at a corner, which is the seam the
amendment exists to prevent. So the caption is still `ChromeSurface`, same ink and same alpha; the
rail moved tiers and the caption went with it. Both are painted on the same `ShellGround`, so the
equality is true on the glass and not only in the token map.

**And §9's older claim is where the honesty is owed.** *"The caption must not be the brightest
thing, and the art must be the first thing on screen with light in it."* In flush it holds outright:
the caption is a chrome tone at the pane tier, level with the rail, above the art by the palette's
own step. In floating it holds at `SOLID` and over a dark desktop, and **over a bright wallpaper it
does not** — the ground is the most open surface in the window, so the caption and the gaps are the
brightest band in it, together. §15.7 already conceded that for the gaps. The caption joins them,
which is the two-tier structure being visible rather than a regression hiding inside it.

### 16.6 What it measures

Walked per theme against white — the ceiling any wallpaper can reach.

| Last whole percent still clearing 4.5:1 | Winnow | Nightshift | Tungsten | Box art |
|---|---|---|---|---|
| **Reported AA ceiling** — *before* | 27 | 31 | 30 | 26 |
| **Reported AA ceiling** — *after* | **30** | **31** | **31** | **31** |
| A selected rail row — *before* (this used to set the mark) | 27 | 31 | 30 | 26 |
| A selected rail row — *after* | **40** | **54** | **47** | **41** |
| The rail's own labels — after | 56 | 69 | 61 | 57 |
| The caption on the ground — after *(this sets the mark now)* | 30 | 31 | 31 | 31 |
| The caption in flush — after | 56 | 69 | 61 | 57 |
| A pane's `TextDim` — after | 63 | 71 | 68 | 74 |
| A selected list row in a pane — after | 48 | 56 | 53 | 56 |
| The field's polarity floor — before / after | 29 → **34** | 46 → **47** | 38 → **41** | 44 → **44** |

**The headline is two numbers that moved in opposite directions, and both are the same change.**
The rail's ceiling roughly doubled, because the rail stopped being the most open reading surface in
the window. The *reported* ceiling barely moved, because something else took that position: the
caption, on a ground the change deliberately opened up. **The mark was never about the rail. It is
about whichever surface is most open and carries text**, and the restructure moved which one that
is. Every theme still holds at least the range it had, three of the four gain, and Box art gains
five points.

**Over a dark desktop nothing gets worse anywhere**, at any position, in any theme — the composite
is darker than `Ground`, so opening a surface deepens the ground its labels sit on.
`ThemeContrastTests` asserts it across the range.

**Polarity clears the mark by 4 to 16 points**, so `MinWallAlpha` survives at `0.65` untouched. It
improved without being retuned: a pane is painted on the window's ground now, which darkens the
field slightly on the way through.

### 16.7 The mark is taken across both layouts, because it stopped being layout-free

§15.6 used to claim the layout moved nothing §14 measured. **That is no longer true and the
replacement is a measurement rather than a construction.** Floating puts the caption on the ground
and flush paints it at the pane tier, so the two layouts fail in different places — 30 / 31 / 31 / 31
against 56 / 69 / 61 / 57. A mark on the slider that moved when the user changed layout would be a
promise that expired on an unrelated setting.

So `Colorimetry.AaCeiling` walks **both** layouts and reports the worse. The mark means one thing
whichever layout is up, and flipping the layout can never invalidate it.

### 16.8 The honest costs

**Two panes at the same tier can still be in different states.** `appearance.wall` still gates the
art field and the screens beside it, while the rail and the filter panel follow the slider alone.
With the reach off you get a translucent rail beside a solid library pane — two panes at one *tier*
in two *states*. That is not new (the same setting produced a starker mismatch before, a rail at
70% beside a solid field), and it is not fixed here, because the alternative is worse: the flush
layout has no visible ground, so gating the side panes on the reach setting too would leave a fresh
install with transparency up showing nothing translucent but a 36px lip. The Appearance screen says
what the setting does in words instead.

**The typed text in the filter panel's fields lost four points.** It used to hold AA across the
whole slider on both fields. It still does in the library pane, whose ground is `Ground`; in the
filter panel it now runs out at **96% and 97%** on Winnow and Box art, because the panel's field
paints no fill at all — the identity forces it to zero — so the ink under the caret sits on the
panel's own `Surface` rather than on a `Ground` step cut into it. Four points at the very top of the
track, on a pure white wallpaper, three times past the mark. The only fill that would buy it back is
one that makes the field less open than the pane around it, which is §14.7's bolted-shut patch.

**The caption gives up seven points of its own range** in the floating layout — 38% to 31% on
Nightshift — which is the whole of why the reported ceiling rose by one there rather than by
twenty. That is the trade the request bought, stated rather than buried: a ground and a caption that
are one field, at the price of the caption being measured on the most open surface in the window.

### 16.9 The committed sheets state the three-tier figures

**Every `compare--*` sheet in `docs/screenshots/appearance/` was captured before this, and several
of them print numbers on their captions that are now wrong.** They are left as they are — a
screenshot is a record of the build that produced it, and re-lettering one is worse than saying
which figures moved. Read them against this table:

| A sheet says | It is now |
|---|---|
| "worst chrome surface" / "a selected rail row" | the **title bar** on the window's ground, in the floating layout |
| `5.04:1` solid, `2.91:1` at 45% white, `1.01:1` at 100% white *(Winnow)* | `5.04:1`, `2.82:1`, `1.42:1` — the surface changed, not the palette |
| "past 27% the white figure drops under 4.5:1" *(Winnow)* | **30%** |
| the AA mark's position on the track | **30 / 31 / 31 / 31**, and taken across both layouts (§16.7) |
| "the chrome admits 70%, the wall admits half of it" | **the ground admits 85%, a pane admits 35%** |
| "a field admits half of what the surface around it admits" | **a field admits exactly what the pane around it admits**, by painting nothing |
| "Chrome only" / "Chrome and the wall" *(the reach choice)* | "The ground and the side panes" / "Everything but the covers" |
| "the cover wall never opens up at any setting" *(pre-dates §14.6 as well)* | it opens with the reach setting, at the pane tier |

**The window itself is the record that is kept current**, which is the §14.3 argument that put these
figures on the Appearance screen in the first place: the screen measures the running window and
reports the worst case live, so a sheet that has gone stale is a picture of an old build rather than
a claim anybody is still making.
