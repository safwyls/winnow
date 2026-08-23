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

Bright, saturated, arcade-adjacent. The chrome is a deep indigo-violet stage; neon appears
only in signal positions. Cover art supplies the real colour riot, so the interface must be
vivid enough to hold its own without competing.

| Token | Hex | Role |
|---|---|---|
| `Ground` | `#16112A` | Window background — deep indigo-violet, never black |
| `Surface` | `#1F1838` | Rail, panels, list rows |
| `SurfaceRaised` | `#2C2350` | Hover, selection, popovers |
| `Line` | `#3D3168` | Dividers, borders, tile outlines |
| `Text` | `#F0ECFF` | Primary text |
| `TextDim` | `#9B90C4` | Labels, metadata |
| `Flare` | `#FF5C8A` | **Patched since you played** — the unread signal |
| `Volt` | `#4DE8C2` | **Active / recent / selected** |
| `Amber` | `#FFB63D` | Attention: high playtime, "played out", warnings |
| `Azure` | `#5B9DFF` | Informational, links, secondary counts |

### Discipline

`Flare` is the rarest colour in the interface and appears **only** on unread-update markers
and the bucket that counts them. The instant it becomes a generic accent, the badge stops
meaning anything and the product loses its point.

`Volt` carries selection and recency. `Amber` carries "you've been here a lot." `Azure` is
the neutral one and does the boring work.

Never tint cover art with brand colour. The art is content; the interface stays out of it
except through the saturation ramp in §5.

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
| 6 months | 0.72 | 0.88 | Slightly cooled |
| 1 year | 0.50 | 0.78 | Visibly faded |
| 2 years | 0.34 | 0.68 | Ghosted |
| 3+ years / never | 0.22 | 0.60 | Nearly monochrome |

Clamp at `0.22 / 0.60` — never fully grey. A cover you can't identify is a cover you can't
choose, and the point is to make forgotten games *findable*, not invisible.

**Hover restores full saturation over 140ms.** The game wakes up under the cursor. This is
the single most important interaction in the app: it makes the dormancy encoding legible by
showing you the before and after, and it feels good.

### 5.2 Unread badge

10px `Flare` dot, top-right, 8px inset, with a 2px `Ground`-coloured ring so it reads
against any cover. Optional soft outer glow at 30% opacity.

Present only when a major update landed after the user's last session (both signals from
§4.5 of the design doc — build push *and* announcement). Never on never-opened games; an
unplayed game has nothing to be behind on.

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

Covers must be virtualized (`ItemsRepeater`) and decoded off-thread at display resolution,
not full size. A 1,200-tile grid of 600×900 source bitmaps decoded eagerly will exhaust
memory.

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
| Bucket: 0 playtime | `Never opened` | `Pile of shame` |
| Bucket: low playtime | `Bounced off` | `Barely played` |
| Bucket: high playtime | `Played out` | `Completed` |
| Bucket: unrunnable | `Won't run` | `Dead` |
| Badge tooltip | `3 updates since you played` | `New content available!` |
| Journal prompt | `How was that?` | `Rate your session!` |
| Merge action | `Same game` | `Merge records` |

**Empty states are directions, not moods.**

- Patched since, empty: *"Nothing's been patched since you last played. This fills up on its own."*
- Never opened, empty: *"You've opened everything you own. Genuinely rare."*
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
- `TextDim` on `Surface` measures ~4.9:1. Do not dim further.
- Provide a settings toggle to disable the dormancy ramp entirely for users who prefer
  uniform art. The badges and buckets carry the signal without it.
