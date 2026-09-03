# Winnow — Design System

**Winnow is a local-first game library manager that surfaces the games you own, meant to play, and forgot existed.** No server, no account, no telemetry. It reads Steam, Epic and GOG from their own local files, records the sessions storefronts throw away, and tracks what has been patched since you last played.

The product's thesis is one sentence: **your library has unread mail.** Two rules follow, and every visual decision in this system is downstream of them.

- **The art is the chart.** Dormancy is rendered as desaturation of the cover itself. A game played last week is full-vivid; one dormant three years is faded and cool-shifted. No sparkline, no bar, no second visual language competing with the art.
- **Patched-since-played is an unread badge.** A hot pink dot in the tile corner, and the rarest colour in the interface.

## Products represented

One product, one surface: a **Windows desktop client** (Avalonia 11 / .NET 10, dark-only for v1) that draws its own window chrome. There is no web app, no mobile app and no marketing site. Its screens are the cover wall, the list view, the feed, the game detail modal, the merge confirm queue, the filter panel, and two settings panes (Stores, Appearance).

## Sources this system was built from

Everything here was lifted from source, not from screenshots:

- **GitHub — [github.com/safwyls/winnow](https://github.com/safwyls/winnow)** (branch `main`). Worth exploring further if you are designing against this product: [`design-system.md`](https://github.com/safwyls/winnow/blob/main/design-system.md) is a 2,100-line visual spec with the measurements and the reversals behind every rule, [`src/Winnow.App/Themes/tokens.axaml`](https://github.com/safwyls/winnow/blob/main/src/Winnow.App/Themes/tokens.axaml) is the live token dictionary, and [`src/Winnow.App/Views/`](https://github.com/safwyls/winnow/tree/main/src/Winnow.App/Views) holds the real markup for every screen.
- Files read in detail: `README.md`, `notes.md`, `mock-library.html` (the browser mock the app was calibrated against), `Themes/tokens.axaml`, `Themes/controls.axaml`, `Views/MainWindow.axaml`, `Views/GameTileView.axaml`, `Views/FeedView.axaml`, `Views/FeedCardView.axaml`, `Views/GameDetailsView.axaml`, `Winnow.Recommend/ShelfBuilder.cs`, `ReasonBuilder.cs`, `Shelves.cs`, `ViewModels/ReasonText.cs`.
- A verbatim copy of the visual spec is kept at [`guidelines/winnow-visual-spec.md`](guidelines/winnow-visual-spec.md). When this readme and that file disagree, that file wins.
- `github.md` records the sync point.

No Figma file, no slide template and no brand book was provided, so there are no slide layouts in this system.

---

## CONTENT FUNDAMENTALS

**The app knows something faintly embarrassing about the user** — they own 1,247 games and have opened 412 of them zero times — **and must never be smug about it.** That single constraint produces the whole voice.

**Plain and specific.** Name a thing by what the person recognises, not by the table it lives in: "Checked 12 times since 23 Aug 2026 — up 1h 7m," never "12 snapshots."

**Sentence case in prose. Caps only for the app's own vocabulary** — bucket names and rail headings are set in uppercase Display S (`PATCHED SINCE`, `LISTS`, `SETTINGS`) because they are the application's words. A list the user named is body type in their own casing, because it is their sentence.

**Second person, and the app never says "I".** "You put 2.8 hours into this in 2021." Winnow refers to itself by name when it must: "Winnow fills the year, publisher and summary in from IGDB as it works through your library."

**No emoji. No exclamation marks. No cheerleading.** There is exactly one em dash's worth of flourish per screen and usually none.

**Buttons are named for what they do**, never for the data model or the mechanic:

| Write | Don't write |
|---|---|
| `Patched since` | `Needs attention` |
| `Never played` | `Pile of shame` |
| `Bounced off` | `Barely played` |
| `Played out` | `Completed` |
| `Won't run` | `Dead` |
| `3 updates since you played` | `New content available!` |
| `How was that?` | `Rate your session!` |
| `Same game` / `Different games` | `Merge` / `Cancel` |
| `Save as live list` | `Save smart collection` |
| `Play` on disk, `Install` when not | `Play` on a 60GB uninstalled game |

**Empty states are directions, not moods.**

- *"Nothing's been patched since you last played. This fills up on its own."*
- *"You've played everything you own. Genuinely rare."*
- *"Reading your Steam library. Covers and metadata fill in over the next few minutes — you can browse now."*
- *"No lists yet. Select titles and choose Add to list, or filter the library and save the result as a live list."*
- *"Delete "Couch co-op night"? The titles stay in your library."*

**The interface may only claim what it can support.** "No updates recorded in that stretch" — not "nothing has shipped" — because update polling is staggered across days and an empty rail can mean a quiet decade or a turn that has not come round yet. Two absences are never collapsed into one: *"You've never opened this."* and *"Steam has no date for your last session."* are different sentences.

**A recommendation is a sentence, not a score.** The reason is the product: *"You put 37 hours into this in 2017 and it has had 2 updates since, most recently 'v1.19.2 Patch'."* It cites only what the arithmetic used, and when the model demoted something it says so out loud.

**Explanations are short.** The team's own note on the interface reads: *"Drop the over-explanatory text blurbs throughout the interface. Explanations should be short, straightforward, and unambiguous. No more than a few words."*

---

## VISUAL FOUNDATIONS

### Colour

**One dark green-teal ink, stepped six times, and four signal hues.** The neutral family is not grey and not black: it has a committed hue, so Volt reads as that same ink turned up to full voltage — continuous with the room, which is right for a colour marking a state the interface always has.

`Well #050D0E` · `Ground #0F1C1E` · `Surface #16282A` · `SurfaceRaised #1D3437` · `SurfaceHigh #254042` · `Line #2B4A4C` · `Text #F0EDE7` · `TextDim #8FA5A0` · `TextFaint #5A8286`
`Flare #FF4D93` · `Volt #4DE8C2` · `Amber #FFB63D` · `Azure #57A8F0` · `Danger #E04B45`

**Hierarchy is carried by temperature as well as lightness.** Text is warm off-white and TextDim a cool sage: primary text reads as paper laid on the room, metadata as part of the room. Do not neutralise either.

**Discipline is the whole system.** Flare marks unread updates and the bucket that counts them — the tile badge, the list-row dot, the rail pip, the gap rail's marks. Nowhere else, ever. Volt carries selection and recency and is the one primary fill. Amber is attention. Azure does the boring outbound work. Danger appears on the window close button and on a delete confirm.

**Cover art supplies all the real colour; the chrome is a stage and stays out of the way.** Never tint cover art with brand colour. An earlier violet palette was cut precisely because it read as a third accent and pushed warm Steam capsules green.

**Two tiers, not three.** The window ground and the caption sit at one level; every pane sits at the other. Panes may admit the desktop behind them (a transparency slider, 0–100), and the ink darkens as they open so contrast holds — text never sits on a translucent field.

### Type

Three roles, three families, all bundled as static instances (no system fallback — the display face is load-bearing).

- **Bricolage Grotesque 700** is the voice: bucket names, screen titles, tile titles, shelf headings. Slightly irregular, optically quirky, sharp — it reads like game packaging rather than a dashboard.
- **Plus Jakarta Sans 400/500/600** is body and UI: labels, buttons, prose, tooltips.
- **IBM Plex Mono 400/500** is data. **Every number in the app is Plex Mono with tabular figures** — non-negotiable in list view, where a playtime column that does not align vertically is unreadable at scan speed.

Scale: Display L 22/26 · Display S 12/15 (+0.06em, caps) · Body L 15/22 · Body 13/18 · Label 11/14 (+0.04em, caps) · Data 12/16 · Data S 10/12. Prose measures 12/18 at up to 720px.

### Backgrounds and imagery

**No illustration, no photography, no pattern, no texture, no gradient background anywhere.** The only images in the product are cover art the user already owns. Placeholder art is the title set in Bricolage on a Surface field — never a spinner, never a hole. Mocks use flat two-stop gradients as cover stand-ins and never reproduce publisher art.

Imagery runs whatever colour temperature the publisher chose, with one systematic intervention: the dormancy ramp, which pushes idle art cooler and flatter. There is no grain, no duotone, no overlay tint. One gloss sweep — a 16% white diagonal fading out by 42% — sits over each cover, and that is the only decoration in the interface.

### The dormancy ramp

Months since last played, mapped to `saturate()` → `hue-rotate(-6deg)` → `brightness()`, clamped at **0.22 / 0.68**: under a month is 1.00/1.00, six months 0.72/0.91, a year 0.50/0.83, two years 0.34/0.74, three-plus 0.22/0.68. Never fully grey — a cover you cannot identify is a cover you cannot choose. **Hover restores full saturation over 140ms**, which is the single most important interaction in the app. The encoding is decorative-redundant: idle time also appears as text on hover and as a sortable column, and a setting turns the ramp off entirely.

### Motion

Scarce and short. **140ms** hover restore (CubicEaseOut), **80ms** per half of a card turn, **120ms** row fill cross-fade, **1.4s** pulse on the launch strip's waiting pip (the only loop). Panels do not slide; screens do not transition; nothing bounces, nothing springs, nothing staggers. Reduced motion removes the transitions outright so state snaps.

### States

- **Hover** is a step up in the neutral family — Surface → SurfaceRaised — plus, on a tile, a 2px lift and the one permitted drop shadow. Never an opacity change, never a colour wash.
- **Press** is one further step (SurfaceHigh), or Volt → VoltPress on a filled button. Nothing shrinks.
- **Selection** is a ChromeRaised fill plus a **2px Volt left edge**, and exactly one row in the rail ever carries it. A rule an open live list contributed takes the same fill with a TextDim edge instead.
- **Focus is drawn, not adorned:** a 2px Volt ring as a brush swap on a border whose thickness never changes, because thickening a border on focus reflows the row it sits in. On a Volt fill the ring is VoltInk.
- **Disabled** is 40% opacity, and a zero-count row dims rather than hiding so the rail never reflows.

### Borders, shadow, radius

Borders are 1px `Line`, or `LineSoft` (60%) where art meets chrome. **Elevation is the Surface → SurfaceRaised step, not shadow.** Shadow is spent in exactly three places: a tile lifting under the pointer (`0 8px 24px rgba(0,0,0,.55)`), the detail modal (`0 18px 48px`), and the ambient dock (`0 12px 40px`). Radii rank by the size of the object they turn: **8px** panes, **6px** tiles and cards, **4px** controls, **3px** badges, 10px on the dock card.

### Layout

A 4px base unit: 4 · 8 · 12 · 16 · 24 · 32 · 48. Fixed geometry: 36px caption, 220px rail, 48px command bar, 276px filter panel, 44px list rows, 8px pane gap. Tiles are 2:3 portrait at 148×222 with a 16px gutter, density-adjustable 108→200, and the grid reflows on available width — never a fixed column count. The window opens at 1280×820 and floors at 1200×640, measured rather than guessed.

Fixed elements: the caption (drag handle, three buttons, nothing else), the rail, the command bar and cut bar inside the library pane, the filter column on the right, and the ambient dock bottom-left. **Nothing interactive sits within 8px of the window edge** — that band belongs to the OS resize border, so every edge scrollbar steps 10px in.

### Transparency and blur

A quantity, not a switch: the Appearance screen exposes a 0–100 slider over the platform backdrop (acrylic or mica). Popovers never open up — a flyout is its own root and would sample the application rather than the desktop — and a tile's ground is always opaque so the two-layer dormancy fade composites over the field it was calibrated against.

### Accessibility floor

TextDim on Surface measures 5.88:1 and on a selected row 5.04:1; Text on Surface 13.1:1; Volt on Ground 11.3:1. Every encoding is backed by words: the ramp by an idle column, the badge by a tooltip and a bucket count, chip provenance by a tooltip. Full keyboard grid navigation, `/` to search, Enter to launch, and an Escape ladder that unwinds one layer of the cut per press.

---

## ICONOGRAPHY

**This product has almost no icons, deliberately, and that is the rule to follow.** There is no icon font, no sprite sheet, no Lucide or Heroicons dependency, and nothing was substituted from a CDN. Everything glyph-like in the app is drawn as vector geometry in the markup at a 1px stroke or a flat fill, and there are only seven of them:

| Glyph | Where | Shape |
|---|---|---|
| Minimise / maximise / restore / close | Caption buttons, 46px wide | 1px stroke paths at 11×11, TextDim → Text on hover, close reddens to Danger |
| Grid view | Command bar segmented toggle | Four 5×5 squares, 2px apart |
| List view | Command bar segmented toggle | Three 13×2 bars, 3px apart |
| Caret | Sort and Display buttons | A filled 8×5 triangle |
| Sort direction | List-view column headers | The same triangle, in Volt, pointing up or down |
| Funnel | The Filters button | A 6-point filled path, 11×10 — the one control with a universally read glyph |
| Dismiss ✕ | Chips, dock cards, the modal | The Unicode character, not a drawn path |

Dots do the work icons usually would: a 14px Flare badge on a tile, an 8px dot in a list row, a 7px pip beside a rail count, a 6px Volt dot marking the active sort, a 7px pulsing Volt pip on the launch strip, five 11px rating dots.

**No emoji, anywhere, ever.** Unicode is used for two characters only: `✕` for dismissal and `→` in the cut bar's `926 → 136`, which is the only arrow in the interface.

**The brand mark** is a dragon head — thirteen paths, one flat colour, EvenOdd fill rule — held in `assets/icons/dragon.svg` (the app's own file, inked `#101c1e` for recolouring) and `assets/icons/dragon-mark.svg` (the same geometry inked in TextDim, for use as an image in HTML). `assets/icons/dragon.ico` is the window icon. In the app the mark takes a theme brush and recolours with the palette; in the caption it sits at 20px in TextDim beside the wordmark, tracked 1.5px, and nothing else lives there. **There is no wordmark lockup file and no logo variants** — if you need the brand in type, set "Winnow" in Bricolage Grotesque Bold.

---

## Index

| Path | What it is |
|---|---|
| `styles.css` | The one stylesheet consumers link. Imports only. |
| `tokens/colors.css` | Palette, veils, semantic surface and ink aliases, composites. |
| `tokens/typography.css` | `@font-face` for all six bundled faces, families, scale, tracking. |
| `tokens/spacing.css` | The 4px scale and its in-use aliases. |
| `tokens/geometry.css` | Radii, window and pane geometry, tile sizes, the dormancy constants. |
| `tokens/motion.css` | Durations, easings, the reduced-motion override. |
| `tokens/base.css` | Ground, link colours, focus ring, and the type classes (`.display-l`, `.data`, …). |
| `assets/fonts/` | Bricolage Grotesque Bold; Plus Jakarta Sans 400/500/600; IBM Plex Mono 400/500. |
| `assets/icons/` | `dragon.svg`, `dragon-mark.svg`, `dragon.ico`. |
| `guidelines/*.card.html` | 19 foundation specimen cards (Colors, Type, Spacing, Brand). |
| `guidelines/winnow-visual-spec.md` | The upstream visual spec, verbatim. The tiebreaker. |
| `components/` | 21 components in four groups, each with a `.d.ts` contract and a `.prompt.md`. |
| `ui_kits/desktop-app/` | The click-through recreation of the app. Open `index.html`. |
| `SKILL.md` | Agent-skill entry point. |
| `github.md` | Upstream repo, branch and last sync. |

### Components

**core** — `Button` · `Badge` · `CountPill` · `TextField` · `Checkbox` · `DensitySlider` · `UnreadDot`

**navigation** — `TitleBar` · `RailRow` · `SegmentedToggle` · `SortMenu`

**library** — `GameTile` · `LibraryRow` · `GapRail` · `FeedCard` · `SectionPanel`

**feedback** — `CutChip` · `DockCard` · `RatingDots` · `StatusPip` · `EmptyState`

Every one of these has a counterpart in the Avalonia source. **Intentional additions:** two components generalise rather than invent — `RailRow` merges the app's `Button.bucket` and `Button.listrow` styles behind one `kind` prop (they are the same row in two voices), and `SectionPanel` is the feed's `Border.section` plus its header band, which the app composes inline in `FeedView.axaml`. Nothing else was added: there is no Toast, Avatar, Tabs, Accordion, Breadcrumb or Pagination here, because the product has none.

### Not built

`FilterPanel`, the Stores panel and the Appearance screen exist as parts of the UI kit only — the Stores and Appearance panes are stubbed with a one-line note rather than recreated. There are no slide layouts (no deck was provided) and no marketing-site kit (there is no marketing site).
