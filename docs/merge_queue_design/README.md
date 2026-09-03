# Handoff: Merge Queue (game comparison & merge list)

## Overview

The **Merges** screen in Winnow, the local-first game library manager. It proposes groups of
library entries that are probably the same game — the same title bought on Steam, Epic and GOG;
a remaster next to its original; expansions and DLC next to their base game — and asks the user
to confirm each proposal and choose **which entry becomes the header**.

Two product rules drive everything on this screen:

1. **Merging is non-destructive.** It nests one or more entries under another as children so
   stats (playtime, ownership date, patched-since state) roll up under a single header. Nothing
   is deleted, and every merge is reversible from the row itself (`Separate again`).
2. **The choice is the user's.** Winnow proposes; it never auto-merges. `Different games`
   dismisses a proposal permanently and Winnow will not ask again. A confidence signal is a
   *word*, never a score, and never a reason to act on the user's behalf.

## About the design files

`Merge Queue.dc.html` in this bundle is a **design reference created in HTML** — a prototype
showing intended look and behaviour. It is not production code to lift. The task is to
**recreate this design in the target codebase's own environment** using its established
patterns and libraries. The real Winnow client is Avalonia 11 / .NET 10 (XAML), so the
expected output is `Views/MergeQueueView.axaml` + a view model, styled from the app's existing
`Themes/tokens.axaml` — not this HTML.

To open the prototype: serve this folder over HTTP (`python3 -m http.server`) and open
`Merge Queue.dc.html`. It needs `support.js` and `_ds/` as siblings, both included here.

## Fidelity

**High-fidelity.** Colours, typography, spacing, motion durations and interaction states are
final and are all Winnow design-system tokens. Recreate pixel-for-pixel using the app's
existing token dictionary. Every value in this README is either a token name or a measured px
value from the prototype.

The **content is placeholder data** — 14 fabricated proposals across five kinds, chosen to
exercise every state (2-entry pair, 3-entry group, 5-entry group, DLC with no playtime of its
own, a `won't run` entry, entries patched since last played). Copy for labels, reasons, empty
states and dock messages **is final** and follows the Winnow voice rules; the game titles and
numbers are not.

---

## Screen: Merges

**Purpose.** Work through a queue of merge proposals. For each one: pick the header, then
confirm (`Same game`) or refuse (`Different games`). Bulk paths exist for the boring cases.

### Window and shell

The prototype draws the whole desktop window so the pane reads in context. In the real app the
shell already exists — only the pane content is new.

| Element | Spec |
|---|---|
| Window | 1280 × 820, `--shell-ground` (#0F1C1E), 1px `--line` border, 8px radius |
| Caption | 36px, `TitleBar` component, `layout="flush"`, dragon mark at 20px in TextDim |
| Rail | 220px, `--chrome-surface`, 8px radius, 8px vertical pad, 2px row gap |
| Pane gap | 8px between rail and pane; 8px bottom padding (the OS resize band) |
| Pane | fills remaining width, `--pane-ground`, 1px `--line`, 8px radius, `grid-template-rows: auto auto 1fr` |

Rail content, top to bottom: three screen rows (`THE LIBRARY`, `THE FEED`, `MERGES` — selected,
no count, because a screen is not a cut of anything) · 1px divider · `CUTS` heading · five
bucket rows (`PATCHED SINCE` 37 with the Flare pip, `NEVER PLAYED` 412, `BOUNCED OFF` 88,
`PLAYED OUT` 61, `WON'T RUN` 12) · divider · `LISTS` heading · three list rows
(`Couch co-op night` 14, `Finish in 2026` 9, `Merged headers` 26).

Rail section headings: Bricolage 700, `--text-display-s` 12/15, `--track-display-s` .06em,
`--text-faint`, padding `6px 12px 4px`.

### Band 1 — header (56px, `padding: 0 16px`, flex, `align-items: center`, 12px gap)

| Item | Spec |
|---|---|
| `Merges` | h1, Bricolage 700, `--text-display-l` 22/26, `--text` |
| Count line | `14 proposals · non-destructive`, IBM Plex Mono, `--text-data` 12/16, tabular figures, `--text-dim`. Reads `nothing waiting` at zero. |
| — | `flex: 1` spacer |
| Sort | `Button variant="ctl"`, label `Sort · Strongest match`, trailing filled 8×5 caret triangle (`M0 0h8L4 5z`, currentColor), 7px gap. `active` while open. |
| Sort menu | `SortMenu` component, rendered **only when open**, absolutely positioned `top: 34px; right: 0; z-index: 5` under the trigger. Options: `Strongest match` (default), `Playtime at stake`, `Title`. Choosing one closes the menu. |
| Accept | `Button variant="ctl"`, `Accept 5 exact matches`. Label counts live; reads `No exact matches left` and is disabled at zero. |
| Merge selected | `Button variant="primary"`, `Merge 3 selected`; reads `Merge selected` and is disabled with an empty selection. |

### Band 2 — cut bar (40px, `padding: 0 16px`, `--chrome-surface`, 1px `--line` top and bottom)

- `SegmentedToggle`, six options: `ALL` (default) · `STORES` · `EDITIONS` · `EXPANSIONS` ·
  `PARTS` · `TESTS`. Filters the queue to one grouping kind.
- When a kind is picked, a `CutChip kind="user"` appears beside it carrying the kind's full name
  (`ACROSS STORES`), tooltip *"You set this — showing one grouping kind"*; its ✕ returns to ALL.
- Right-aligned count, Plex Mono tabular, `--text-dim`: the pending total (`14`) when unfiltered,
  and `14 → 6` when filtered. That arrow is the only arrow in the interface.

### Band 3 — the queue (scrolls; `padding: 16px 10px 24px 16px`, 16px gap between sections)

Right padding is 10px, not 16px: `--scrollbar-edge-inset`, so the scrollbar steps in off the
pane edge. `scrollbar-width: thin; scrollbar-color: var(--line) transparent`.

One `SectionPanel` per grouping kind, `pad="12px"`, 10px gap between the group cards inside it.
Section title (Bricolage 700, 17px), count = **pending** proposals in that section, and a blurb:

| Section | Blurb |
|---|---|
| `ACROSS STORES` | The same game bought more than once. Playtime rolls up under whichever copy you keep. |
| `EDITIONS` | Remasters and re-releases. Winnow cannot tell a re-release from a sequel on its own — these are yours to call. |
| `EXPANSIONS` | Content that needs the base game to run. Nesting these keeps one row per game in the library. |
| `PARTS` | Entries the store lists separately but ships as one release. |
| `TEST BUILDS` | Demos, betas and playtests that shipped as their own entry. |

When every proposal in a section is settled, its body is an `EmptyState`,
`measure="440px"`: *"Nothing left to decide here."*

---

## Component: proposal card

Container: `background --surface`, 1px border, `--radius-tile` 6px, `overflow: hidden`.
Border is `--line` normally and `--volt-edge-soft` (rgba(77,232,194,.4)) while the card is
checkbox-selected. **Two mutually exclusive states: pending and resolved.**

### Pending — header block

`display: grid; grid-template-columns: 26px 1fr auto; gap: 12px; padding: 12px; align-items: flex-start`

1. **Checkbox** (`Checkbox` component) in a 26px clipped column, for the multi-select bulk path.
2. **Title stack**, 2px gap:
   - Header title — Bricolage 700, `--text-body-l` 15/22, `--text`. **This is the title of the
     currently promoted row and changes when the user promotes a different one.**
   - `UnreadDot size="row"` if any entry in the group is patched-since-played, tooltip
     *"N of these have been patched since you played"*.
   - Confidence `Badge variant="fill"`, 10px, .04em tracking, `2px 6px` padding, on
     `--surface-raised`. Three values, distinguished by ink only:
     `EXACT MATCH` → `--text` · `LIKELY` → `--text-dim` · `WORTH A LOOK` → `--amber`.
   - Roll-up line — Plex Mono, `--text-data` 12/16, tabular, `--text-dim`, ` · `-joined:
     `312h rolled up · 2 entries · owned since 2017 · 1 entry patched since you played`
     (the last clause only when a child is unread). Playtime is the **sum over all entries**;
     ownership year is the **earliest**; the unread flag is inherited from any child.
3. **Actions**, 8px gap: `Button variant="primary" size="sm"` **Same game** ·
   `Button variant="quiet" size="sm"` **Different games**.

### Pending — candidate rows (one per entry)

`display: grid; grid-template-columns: 2px 34px 1fr auto 84px 68px 14px; gap: 10px;`
`height: 64px; padding-right: 12px; border-top: 1px solid var(--line-soft); cursor: pointer`

| Column | Content |
|---|---|
| 2px | The selection edge: `--volt`, full row height, on the promoted row only; transparent otherwise |
| 34px | Cover art, 34 × 51 (2:3), 3px radius. `background-image: var(--tile-gloss), <cover>` — the 16%-white 150° gloss sweep over the art. The prototype substitutes flat two-stop 155° gradients; **use the real cover art**, and apply the dormancy ramp as the library does elsewhere. |
| 1fr | Title (Bricolage 700, `--text-body` 13/18; `--text` when promoted, `--text-dim` otherwise, ellipsised) followed by one status word: `HEADER` in `--volt` on the promoted row, `NESTS UNDER` in `--text-faint` on every other. Both Plus Jakarta, `--text-label` 11/14, `--track-label`. |
| auto | Store `Badge variant="outline"` — `STEAM` / `EPIC` / `GOG` |
| 84px | Playtime, Plex Mono `--text-data`, tabular, `--text`, right-aligned. `—` for an entry with no playtime of its own (DLC, unlockable episodes). |
| 68px | Idle time, same type, `--text-dim`, right-aligned: `8mo`, `3y`, `never`, `—` |
| 14px | `UnreadDot size="row"` when patched since last played, tooltip *"Patched since you played"* |

Row background: promoted → `--chrome-raised` (#1D3437) · hovered → `--chrome-raised-half`
(rgba(29,52,55,.5)) · otherwise transparent. Transition `background var(--dur-hover-restore)`
(140ms) `var(--ease-out)`. `title="Make this the header"`.

### Pending — reason slot (one per card, at the bottom)

`min-height: 32px; padding: 6px 12px; border-top: 1px solid var(--line-soft);`
`background: var(--surface-raised-faint); font-size: var(--text-para) 12/18`

One text slot doing two jobs, cross-fading `color` over `var(--dur-fill)` (120ms):

- **Default:** the group's reason, `--text-dim`. It cites only what the match used —
  *"Same title, same IGDB id 1583 — Steam and GOG both call it Hollow Knight."*,
  *"Titles differ by "The Final Cut". Same publisher, same year, 0.91 name match."*,
  *"Four entries declare Stellaris as their parent app. None of them launch on their own."*
- **While a candidate row is hovered:** that row's own detail, `--text`  —
  *"GOG · never opened · installed 12 Nov 2021 · 1.1 GB on disk"*.

### Resolved

The card collapses to a single strip, `min-height: 44px; padding: 8px 12px`, 12px gap:
2px full-height `--volt` edge · header title (Bricolage 700, 15px) · Plex Mono meta
`4 entries · 190h · nested, nothing deleted` in `--text-dim` · spacer ·
`Button variant="quiet" size="sm"` **Separate again**.

The strip stays in place rather than vanishing, so the list does not reflow under the pointer.

---

## Interactions & behaviour

| Trigger | Result |
|---|---|
| Click a candidate row | Promotes it to header. Volt edge and `HEADER` move to it, every other row shows `NESTS UNDER`, the card's header title and roll-up recompute. Also marks the group "touched" (see `rollupTiming`). |
| Hover a candidate row | Row background steps to `--chrome-raised-half` over 140ms; the reason slot swaps to that row's detail over 120ms. |
| `Same game` | Group becomes resolved; it leaves the selection; section count drops. Dock card: title *"Rolled up under Hollow Knight."*, note *"1 entry nested · nothing was deleted."* |
| `Separate again` | Un-resolves that one group in place. No dock card. |
| `Different games` | Group is removed from the queue. Dock card: *"Left 1 group alone."* / *"They stay separate in your library. Winnow will not ask again."* **Consecutive dismissals accumulate into one card** — *"Left 3 groups alone."* — and one Undo reverses the whole run. |
| Checkbox | Toggles group selection; card border becomes `--volt-edge-soft`; the primary button label counts (`Merge 3 selected`). |
| `Merge N selected` | Resolves every selected group, each keeping the header the user picked. Dock: *"Rolled up 3 groups."* / *"Each kept the header you picked · nothing was deleted."* |
| `Accept N exact matches` | Resolves only `EXACT MATCH` groups **in ACROSS STORES** — the safe bulk path. Dock: *"Rolled up 5 exact matches."* / *"Cross-store duplicates only · nothing was deleted."* |
| Sort | Reorders groups **within** each section: strongest match (EXACT → LIKELY → WORTH A LOOK), playtime at stake (summed hours, descending), or title (locale compare on the current header title). |
| Kind filter | Shows one section; adds the cut chip; the count becomes `total → shown`. |
| Undo (dock) | Restores the state snapshot taken at the start of the last action or dismissal run. |

**Ambient dock.** `DockCard width="320px"`, absolutely positioned `left: 16px; bottom: 16px`,
`--shadow-dock`, 10px radius. Title + Plex Mono meta row with a bare ✕, then a body row: note
text (12/18, `--text-dim`) and a `quiet` **Undo** button. Enters with a 140ms
`opacity 0→1, translateY(6px→0)`. **Auto-dismisses after 7s**; the ✕ closes it early. Never
modal, never focus-stealing.

**Motion.** Only two transitions on this screen — the 140ms row-background restore and the
120ms reason cross-fade. No panel slides, no card flips, no staggering. Under
`prefers-reduced-motion` both are removed and state snaps (`tokens/motion.css` already does this).

**Keyboard (not built in the prototype — implement it).** Up/Down through candidate rows, Space
to promote the focused row, Enter for `Same game`, Escape for `Different games`, and the app's
standard focus treatment: a 2px `--volt` ring drawn as a brush swap on a border whose thickness
never changes.

---

## State

Per screen:

| State | Type | Notes |
|---|---|---|
| `header` | `Map<groupId, int>` | Index of the promoted row. Defaults to 0 — Winnow's own pick. |
| `touched` | `Set<groupId>` | Groups where the user has promoted a row explicitly. |
| `resolved` | `Set<groupId>` | Merged groups; render the resolved strip. |
| `dismissed` | `Set<groupId>` | `Different games`; filtered out of the queue and never proposed again. |
| `selected` | `Set<groupId>` | Checkbox multi-select. |
| `sort` | `'match' \| 'stake' \| 'title'` | Default `match`. |
| `sortOpen` | `bool` | Sort menu visibility. |
| `kind` | `'all' \| 'stores' \| 'editions' \| 'expansions' \| 'parts' \| 'tests'` | Default `all`. |
| `hover` | `{groupId, rowIndex} \| null` | Drives the row fill and the reason slot. |
| `dock` | `{type, title, note, runCount} \| null` | Plus a 7s timer and one state snapshot for Undo. |

Derived per group, not stored: header title, summed playtime, entry count, earliest ownership
year, inherited unread count, pending/resolved.

**Persistence.** `resolved` and `dismissed` are decisions and must survive a restart — they
belong in the local store next to the merge graph. `sort`, `kind` and selection are session
state. A merge writes a parent/child link, never a delete: both entries keep their own store id,
playtime and install state, and unlinking must restore them untouched.

**Data the real screen needs per proposal:** grouping kind, confidence tier, the reason string
(built server-side from the fields the match actually used — same shape as `ReasonBuilder`), and
per entry: title, store, store id, cover, playtime, last-played, install state, ownership date,
patched-since flag, parent-app id, and a per-entry detail string.

### Tweakable props exposed on the prototype

| Prop | Default | Effect |
|---|---|---|
| `showConfidence` | `true` | Hides the confidence badges entirely — the reason sentence carries it alone. |
| `rollupTiming` | `inline` | `after-choice` replaces the roll-up line with *"Pick a header to see the roll-up."* until the user promotes a row. |
| `rowHeight` | `64px` | Candidate row height, 48–76. |

---

## Design tokens

All values come from the Winnow token files in `_ds/.../tokens/` — the same set as
`Themes/tokens.axaml`. Reference the tokens, not the hex values.

**Colour** — `--well #050D0E` · `--ground #0F1C1E` · `--surface #16282A` ·
`--surface-raised #1D3437` · `--surface-high #254042` · `--line #2B4A4C` ·
`--line-soft rgba(43,74,76,.6)` · `--text #F0EDE7` · `--text-dim #8FA5A0` ·
`--text-faint #5A8286` · `--flare #FF4D93` · `--volt #4DE8C2` · `--volt-edge-soft rgba(77,232,194,.4)` ·
`--amber #FFB63D` · `--danger #E04B45` · `--surface-raised-faint rgba(29,52,55,.08)` ·
`--tile-gloss linear-gradient(150deg,rgba(255,255,255,.16) 0%,rgba(255,255,255,0) 42%)`

Discipline: **Flare only ever means patched-since-played** — on this screen that is the unread
dots and nothing else. Volt carries selection and the promoted header, and is the one filled
button. Amber appears once, on `WORTH A LOOK`.

**Type** — Bricolage Grotesque 700 (titles, section and screen headings) · Plus Jakarta Sans
400/500/600 (labels, buttons, prose) · IBM Plex Mono 400/500 with `font-variant-numeric:
tabular-nums` (**every number**). Scale: `--text-display-l` 22/26 · `--text-display-s` 12/15
+.06em caps · `--text-body-l` 15/22 · `--text-body` 13/18 · `--text-label` 11/14 +.04em caps ·
`--text-data` 12/16 · `--text-data-s` 10/12 · `--text-para` 12/18.

**Spacing** — 4px base: 4 · 8 · 12 · 16 · 24 · 32 · 48.

**Geometry** — `--radius-pane` 8px · `--radius-tile` 6px · `--radius-control` 4px ·
`--radius-badge` 3px · dock card 10px · `--rail-width` 220px · `--title-bar-height` 36px ·
`--pane-gap` 8px · `--scrollbar-edge-inset` 10px · `--selection-edge` 2px.

**Motion** — `--dur-hover-restore` 140ms · `--dur-fill` 120ms · `--ease-out` cubic ease-out.

**Shadow** — `--shadow-dock 0 12px 40px rgba(0,0,0,.6)`, used once (the dock). Elevation
everywhere else is the Surface → SurfaceRaised step, not shadow.

---

## Design-system components used

From the bound Winnow system — reuse the app's existing equivalents rather than restyling:
`TitleBar`, `RailRow` (bucket and list kinds), `SegmentedToggle`, `SortMenu`, `Button`
(primary / quiet / ctl, md and sm), `Badge` (outline and fill), `Checkbox`, `UnreadDot`,
`CutChip`, `SectionPanel`, `EmptyState`, `DockCard`.

Nothing new was invented at the component level. The proposal card is a **composition** —
`SectionPanel` body → card container → header grid → candidate rows → reason slot — and is the
one piece that needs building. It is close kin to `LibraryRow` (same 2px edge, same Plex Mono
right-aligned playtime and idle columns, same 8px unread dot) and should share its metrics.

## Assets

- `assets/dragon-mark.svg` — the brand mark, from the design system's `assets/icons/`. Caption
  only, 20px, TextDim.
- Cover art: **none included.** The prototype uses flat two-stop 155° gradients as stand-ins.
  Use real cover art from the library, with the dormancy ramp applied.
- No icon library. The only glyph on this screen is the 8×5 filled caret triangle on the sort
  button (`M0 0h8L4 5z`), plus the Unicode `✕` on the chip and dock card. No emoji.

## Files

| File | What it is |
|---|---|
| `Merge Queue.dc.html` | The prototype. Markup at the top, then the logic class with the placeholder data, derived values and every handler. |
| `support.js` | Runtime the prototype needs to render. Not part of the design. |
| `_ds/` | The Winnow design system: token CSS, bundled fonts, and the component bundle. |
| `assets/dragon-mark.svg` | Brand mark. |

Upstream product source: `github.com/safwyls/winnow` (`main`) — `design-system.md` for the
visual spec, `Themes/tokens.axaml` for the live token dictionary, `Views/` for the markup of
the surrounding screens.
