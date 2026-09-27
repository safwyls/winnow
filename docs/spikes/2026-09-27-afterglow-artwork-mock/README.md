# Afterglow artwork and caption study

September 27, 2026 · TASK-354 / TASK-355 / TASK-356 · Proposal for user review

This standalone mock explores portrait covers and compact captions for Afterglow, Winnow's
Electron frontend. It preserves the Discover hero composition, warm palette and typography.
It does not change the production frontend, Avalon, backend, artwork cache or user library.

## Open the mock

From the repository root:

```powershell
python -m http.server 8772 --bind 127.0.0.1 --directory docs/spikes/2026-09-27-afterglow-artwork-mock
```

Open [the library preview](http://127.0.0.1:8772/index.html?page=library) or
[Discover](http://127.0.0.1:8772/index.html?page=discover). All images, fonts and scripts are
local. The mock makes no API calls and requires no running Winnow backend.

## What to review

- Covers fill aligned 2:3 frames using `object-fit: cover`, cropping from the center when
  their proportions differ. Sable deliberately demonstrates a landscape-only source.
  The mixed-artwork control does the same for Hades.
- Grid captions sit over the bottom of the artwork and fade in on hover or keyboard focus.
  A dark gradient supports the full title and a compact metadata row. Idle cards show only
  artwork, without a reserved text area. Touch screens keep overlays visible. Long names
  also have an accessible full name and a full-title preview when selected. Compact records
  retain visible text beside the thumbnail.
- Discover keeps its large landscape hero and editorial invitation. Eight returning games
  use small portrait thumbnails in four columns on wide windows, two columns below 1150 px,
  and one below 540 px. The recommendation shelf uses the same compact caption rules.
- Moving over cover art opens an amorphous PixiJS portal around the cursor. A soft amber/blue
  rim surrounds alternate game artwork, with slight lens distortion and parallax. Seven games
  have secondary art; the others reveal a procedural night sky. Captions stay above the effect.
  Loading/missing artwork and compact records have no portal.
- Display options change cover size, caption density and the palette. Defaults are
  **Balanced + Essential**. **With context** adds a short reason; **Titles only** hides
  metadata. Cover portals offer **Flowing**, **Still** and **Off**. Keyboard focus always uses
  a still opening; system reduced motion also freezes shape, parallax easing and reveal motion.
  In Still mode the opening follows pointer movement directly, without its own animation loop.
  These controls are in-memory demonstrations and reset on reload.
- The review strip switches desktop/fullscreen composition and original/mixed/missing/loading
  artwork. Loading and missing states preserve the same frame dimensions.
- Fullscreen removes the library index, enlarges text and focus, and adds a Discover
  recommendation strip. Arrow keys navigate library cards; Enter opens the title preview.
  Escape closes the preview or returns from Library to Discover. This is a browser
  composition study, not a native fullscreen or physical-controller implementation.

The sample has 12 titles. Stores, playtimes, installation flags and recommendation reasons
are illustrative fixtures, not a live account snapshot. Search, filters, record/grid view,
hero selection, title previews and display controls are interactive. The title dialog is
only an inspection aid, not a redesign of the production details screen.

## Verification

Initial browser checks at 1440×900 and 760×560 found no page or library horizontal overflow. Header
and footer remain inside the viewport and content scrolls within its pane. The wide desktop
library shows five equally sized cover frames (approximately 203×304 px). Missing and loading
art retain those dimensions. Caption overlays do not change the card height.

Exercised the initial six-result unplayed filter, one-result Tentacles search, clearing back to twelve,
complete long-title preview, compact records, three cover sizes, all three caption options,
both palettes and reset. At 1440 px, small/balanced/large desktop cards produce six/five/four
columns. Fullscreen arrow navigation advanced from Borderlands 2 to Borderlands 3. Checked
fullscreen Discover at the constrained window size, hero selection, landscape fitting and
missing/loading thumbnails. JavaScript syntax and Git whitespace checks passed.

Initial caption-below-art review captures:

- [Discover](01-discover.png)
- [Library](02-library.png)
- [Compact records](03-records.png)
- [Display options](04-display-options.png)
- [Fullscreen library](05-fullscreen.png)
- [Discover shelves](06-shelves.png)

The hover-overlay revision was checked in the browser at 2052×1272. Idle desktop captions
have zero opacity and cards match their cover height (291 px in this layout). Visually
checked pointer-hover reveal. Fullscreen keyboard focus exposed the complete Guns, Love
and Tentacles title: its overlay measured 214 px inside a 326 px card, with no overflow or
height change. Discover shelf focus exposed the same overlay treatment. Titles-only controls
hide metadata, reset restores it, and record captions remain visible in normal flow.
The browser reported no warnings or errors. Touch and reduced-motion fallbacks are defined
in CSS; no physical touch-device check was performed.

Caption-overlay review captures: [idle artwork](07-idle-artwork.png) and
[Discover caption on keyboard focus](08-overlay-focus.png).

The portal revision was checked in the browser at 2052×1272 and 760×560 on desktop and
fullscreen compositions. Eight returning items form two rows at the wide size and four at
the narrow size. All twelve wide desktop library covers measure approximately 194×291 px,
with `object-fit: cover`; no horizontal overflow was found and the footer remains visible.
Visually checked the moving portal on Enshrouded, secondary artwork and the star-field fallback,
fullscreen arrow navigation, Enter to inspect, Still and Off settings, loading-art focus,
and cleanup after navigation, dialogs and switching to records. The DOM contained at most
one effect canvas, and none in the inactive states. Keyboard focus revealed a still portal.
The final shader run produced no new browser warnings or errors. JavaScript syntax and Git
whitespace checks passed. System reduced motion is wired to the same Still path through
`matchMedia`; changing the OS preference and physical touch input were not exercised.

Current capture: [eight returning games and Enshrouded's portal](09-discover-portal.png).

The effect uses one lazy-initialized WebGL renderer and a single custom fragment filter.
Secondary images load on demand through Pixi's asset cache. The private ticker is capped
at 45 fps while flowing, stops when idle, and stays stopped in Still mode. Scroll, blur,
hidden pages and removed cards cancel pending activation; async results cannot attach to
an old card. Resolution is capped at 2×. These are implementation bounds, not measured
GPU timings or evidence of production performance. WebGL failure leaves the normal cards
and captions usable. A production build should import only the Pixi modules it needs;
this standalone review uses the full browser bundle for offline portability.

The mock does not measure production artwork latency. Proposed follow-up work includes
targeted artwork invalidation, size-aware requests, cached encoded variants and avoiding
base64 transport. These require implementation and cold/warm timing checks after design
review; fast local mock images are not evidence that those changes have shipped.

## Asset provenance

Game images are unmodified copies of existing local review assets or Winnow's artwork cache.
No live launcher files were accessed. Artwork remains the property of its respective owners;
these review copies are not a production redistribution license.

Steam cache keys: 49520 (Borderlands 2), 504230 (Celeste), 632470 (Disco Elysium), 1203620
(Enshrouded), 1145360 (Hades cover and hero), 367520 (Hollow Knight), 753640 (Outer Wilds).
IGDB cache keys: co20r3 (Borderlands 3), co9cbz (Bounty of Blood), co20xh (Guns, Love and
Tentacles), co7u1h (Moxxi's Heist). Outer Wilds' hero and the Sable/Hades landscape examples
come from the preceding September 26 design study; its README records the Steam CDN origins.
The portal's Borderlands 2 and Enshrouded scenes are copied from their local Steam hero cache
entries (Enshrouded's standard variant). Celeste, Disco Elysium and Hollow Knight scenes are
the preceding study's landscape images. Hades and Outer Wilds reuse this study's hero images.

DM Sans, Newsreader and JetBrains Mono are copied from the frontend's installed Fontsource
packages. Lucide comes from the preceding design study. Their licenses are included in
`assets`. The dragon mark comes from Winnow's renderer assets.

PixiJS 8.21.0 is vendored from the official npm package, with its MIT license in
`assets/PIXI-LICENSE.txt`. The custom filter follows Pixi's
[filter conventions](https://pixijs.com/8.x/guides/components/filters); the explicit vertex
shader matches the default filter coordinate mapping shipped in that package.
