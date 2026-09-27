# Afterglow artwork and caption study

September 27, 2026 · TASK-354 / TASK-355 / TASK-356 / TASK-357 / TASK-358 · Proposal for user review

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
- Cover art catches a soft light that follows the pointer and settles when it pauses.
  A PixiJS filter samples the existing print to enrich its colours and add a broad reflection.
  Lettering and the centered crop stay fixed within the card. Captions fade above the lighting;
  their dark backing remains readable. Loading/missing artwork and compact records have no finish.
- Poster cards lift with a soft shadow and tilt toward the pointer. Artwork, finish and
  caption move together on one surface; the button's hit area stays fixed to avoid hover
  feedback near its edges. Compact records and returning-game thumbnails remain flat.
- Display options change cover size, caption density and the palette. Defaults are
  **Balanced + Essential**. **With context** adds a short reason; **Titles only** hides
  metadata. Cover finish defaults to **Satin at 55%**. **Matte** uses diffuse light;
  **Satin** adds a gentle sheen; **Foil** adds stronger iridescence. An intensity slider and
  **Off** option support comparison. Turning off **Follow the pointer** fixes the light in
  one position. Keyboard focus and system reduced motion use that same stationary treatment.
  **Card depth** independently toggles floating artwork and adjusts maximum tilt from 0–12°,
  with 7° as the default. Zero gives lift alone. Keyboard focus, reduced motion and disabled
  pointer following keep raised cards level. Finish intensity does not alter card depth.
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

Portal review capture: [eight returning games and Enshrouded's portal](09-discover-portal.png).

The Satin revision was checked at 2052×1272 and 760×560 on desktop and fullscreen layouts.
Visually inspected cursor lighting, the three finishes, and full-intensity Foil. Checked
the intensity output, stationary pointer mode, Off, disabled controls and default reset.
The cursor response reached its settled state with one canvas and a stopped ticker;
keyboard focus used its static state. Fullscreen arrows moved focus, Enter opened the
correct game, and dialogs, loading placeholders and compact records had no effect canvas.
The mixed-proportions Hades source retained its centered 2:3 crop under the shader.
There was no horizontal overflow at the constrained size, and the footer remained visible.
No new browser warnings or errors occurred. JavaScript syntax and Git whitespace checks
passed. System reduced motion uses the verified stationary path; changing the OS preference
and physical touch input were not exercised.

Satin review capture: [artwork lighting](10-satin-artwork.png).

The floating-card revision was checked at 2052×1272 and 760×560. On desktop, moving between
opposite corners changed the card's perspective while its button retained the same
191.25×286.875 px bounds. The artwork, shader and revealed caption stayed aligned. Checked
zero tilt with lift, floating disabled while Satin remained active, 12° maximum tilt,
stationary Foil, and default reset. Fullscreen arrow navigation raised a level card and
Enter opened the correct full-title preview. Dialogs removed the floating state; compact
records retained their normal layout. Discover keyboard navigation exposed a level card,
and all eight returning games remained present. Maximum tilt at the narrow size produced
no horizontal page or library overflow; the footer stayed inside the viewport. No new
browser warnings or errors occurred. JavaScript syntax and Git whitespace checks passed.
The stationary path was exercised through keyboard input and disabled pointer following;
changing the OS reduced-motion preference and physical touch input were not exercised.

Current capture: [floating artwork](11-floating-artwork.png). Move across covers in the
interactive mock to assess the depth and reflection; a still capture cannot show their response.

The current effect uses one lazy-initialized WebGL renderer and a single custom fragment
filter. It creates textures from the cover images already decoded by the browser, keyed
by image URL; it does not fetch secondary art. The private ticker runs at up to 60 fps
while following or fading, stops once the light settles, and stays stopped for stationary
lighting. Pointer scrolling, blur, hidden pages and removed cards cancel pending activation;
async initialization cannot attach to an old card. Resolution is capped at 2×.
Card depth uses a CSS perspective transform with an 8 px lift, 18 px forward translation
and 950 px perspective. Pointer updates coalesce into one animation frame and CSS smooths
the transform; depth has no continuous idle loop. Both effects read the untransformed
button bounds, so tilting the artwork cannot change their coordinate reference.
These are implementation bounds, not measured
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
The earlier portal's Borderlands 2 and Enshrouded scenes are copied from their local Steam hero cache
entries (Enshrouded's standard variant). Celeste, Disco Elysium and Hollow Knight scenes are
the preceding study's landscape images. Hades and Outer Wilds reuse this study's hero images.

DM Sans, Newsreader and JetBrains Mono are copied from the frontend's installed Fontsource
packages. Lucide comes from the preceding design study. Their licenses are included in
`assets`. The dragon mark comes from Winnow's renderer assets.

PixiJS 8.21.0 is vendored from the official npm package, with its MIT license in
`assets/PIXI-LICENSE.txt`. The custom filter follows Pixi's
[filter conventions](https://pixijs.com/8.x/guides/components/filters); the explicit vertex
shader matches the default filter coordinate mapping shipped in that package.
