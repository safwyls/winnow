# Afterglow artwork and caption study

September 27–28, 2026 · TASK-354 / TASK-355 / TASK-356 / TASK-357 / TASK-358 / TASK-359 / TASK-360 / TASK-363 / TASK-364 / TASK-365 / TASK-366 · Proposal for user review

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
- Grid cards keep their artwork clear. Hovering briefly or using keyboard focus opens a
  flyout beside the card with its full title, store, playtime, installation status, revisit
  reason and description. It prefers the right, flips left near the edge and keeps inside
  the header/footer bounds. Very narrow windows use a bottom panel when neither side fits.
  The current portal treatment grows from the pointer into that space, with a softly
  shifting rim and shaded game artwork behind stationary text. **Display options → Details
  portal** offers Living portal, Still portal and the earlier Simple preview. Keyboard
  focus and system reduced motion always use a stationary portal. Roundness and Edge
  waviness sliders tune the contour, with a small outline preview beside the controls.
  You can move into the flyout to read it. Portal details occupy a fixed plane with no
  scrolling; the aperture reveals the text at its final size. A shorter viewport uses an
  ellipsis for descriptions that cannot fit. Escape dismisses the preview before leaving
  Library. Simple preview retains scrolling and Page Up/Down support.
  Selecting the cover opens the full-title dialog, including its description, on all input
  paths. Compact records retain visible text beside the thumbnail.
- Discover keeps its large landscape hero and editorial invitation. Eight returning games
  use small portrait thumbnails in four columns on wide windows, two columns below 1150 px,
  and one below 540 px. The recommendation shelf uses the same side previews.
- Cover art catches a soft light that follows the pointer and settles when it pauses.
  A PixiJS filter samples the existing print to enrich its colours and add a broad reflection.
  Lettering and the centered crop stay fixed within the card. Loading/missing artwork and
  compact records have no finish; their game information remains available.
- Poster cards lift with a soft shadow and tilt toward the pointer. Artwork and finish
  lighting move together on one surface; the button's hit area stays fixed to avoid hover
  feedback near its edges. Compact records and returning-game thumbnails remain flat.
- Display options change cover size, compact record text and the palette. Defaults are
  **Balanced + Essential**. In list rows, **With context** adds a short reason and
  **Titles only** hides metadata. Grid previews always include the full information.
  Cover finish defaults to **Satin at 55%**. **Matte** uses diffuse light;
  **Satin** adds a gentle sheen; **Foil** adds stronger iridescence. An intensity slider and
  **Off** option support comparison. Turning off **Follow the pointer** fixes the light in
  one position. Keyboard focus and system reduced motion use that same stationary treatment.
  **Card depth** independently toggles floating artwork and adjusts maximum tilt from 0–12°,
  with 7° as the default. Zero gives lift alone. Keyboard focus, reduced motion and disabled
  pointer following keep raised cards level. Finish intensity does not alter card depth.
  **Highlight foil** adds silver, gold or holographic reflections to light pixels. Its
  brightness cutoff runs from 40–95% (default 72%) with a soft boundary; strength defaults
  to 65%. It is enabled for this study and can be disabled independently of the surface
  finish. It selects bright illustration details as well as lettering. Turn off both
  surface finish and highlight foil to compare with the original artwork.
  These controls are in-memory demonstrations and reset on reload.
- The review strip switches desktop/fullscreen composition and original/mixed/missing/loading
  artwork. Loading and missing states preserve the same frame dimensions.
- Fullscreen removes the library index, enlarges text and focus, and adds a Discover
  recommendation strip. Arrow keys navigate library cards; Enter opens the title preview.
  Escape closes the preview or returns from Library to Discover. This is a browser
  composition study, not a native fullscreen or physical-controller implementation.

The sample has 12 titles. Stores, playtimes, installation flags and recommendation reasons
are illustrative fixtures, not a live account snapshot. Descriptions are short original
synopses prepared for this study, not metadata fetched from the backend. Search, filters, record/grid view,
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

Floating-card review capture: [floating artwork](11-floating-artwork.png).

The highlight-foil revision was checked on desktop and fullscreen at 2052×1272 and
760×560. Visually compared silver and holographic reflections on Hollow Knight and gold
on Hades. At a 95% cutoff, gold was concentrated on the white title; at 40%, it included
more of the illustration. Checked independent surface Off, highlight disable and disabled
controls, zero foil strength, material changes and default reset. Fullscreen arrows produced
stationary silver foil and level card depth; Enter opened the correct title and removed
the effect. Compact records and loading placeholders had no canvas. Desktop Discover
keyboard focus used stationary foil. The narrow options panel, page and library had no
horizontal overflow, and the fullscreen footer stayed visible. No browser warnings or
errors were reported; JavaScript syntax and Git whitespace checks passed. The existing
reduced-motion stationary branch remains in use; OS preference switching and physical
touch input were not exercised.

Highlight-foil review capture: [holographic highlights](12-highlight-foil.png).

The side-preview revision was checked on desktop and fullscreen at 2052×1272, fullscreen
at 760×560, and desktop at 390×700. Grid captions have no rendered area; artwork and foil
remain unobstructed by text. Hollow Knight opened a right-hand preview; the rightmost
Enshrouded card opened it to the left. The complete Guns, Love and Tentacles title and
description wrapped without horizontal overflow. Moving into the flyout kept it open;
leaving dismissed it. Escape removed the description relationship and kept Library visible.
Enter opened the correct game and removed the preview. Fullscreen arrows exposed a
stationary finish and the same information. Constrained previews scrolled with Page Down;
at 390 px the panel docked above the footer. Missing artwork still exposed the description
without a shader canvas. Navigation dismissed the preview, and compact rows retained their
inline captions. Titles-only list preferences did not hide grid preview metadata. JavaScript
syntax and Git whitespace checks passed, and no browser warnings or errors were reported. Physical touch and
screen-reader output were not exercised; controls retain accessible full names and active
previews use aria-describedby. Reduced motion disables the flyout's entrance animation.

Previous capture: [artwork with a side preview](13-side-preview.png). Move across covers in
the interactive mock to assess the depth and reflection; a still capture cannot show their response.

The cover finish uses one lazy-initialized WebGL renderer and a single custom fragment
filter. It creates textures from the cover images already decoded by the browser, keyed
by image URL; it does not fetch secondary art. The private ticker runs at up to 60 fps
while following or fading, stops once the light settles, and stays stopped for stationary
lighting. Pointer scrolling, blur, hidden pages and removed cards cancel pending activation;
async initialization cannot attach to an old card. Resolution is capped at 2×.
Card depth uses a CSS perspective transform with an 8 px lift, 18 px forward translation
and 950 px perspective. Pointer updates coalesce into one animation frame and CSS smooths
the transform; depth has no continuous idle loop. Both effects read the untransformed
button bounds, so tilting the artwork cannot change their coordinate reference.
Highlight foil uses the original sampled pixel's weighted brightness for its mask, before
any lighting is applied. A feathered threshold selects the print; a darker metallic base
and moving bright reflection give white pixels a visible finish. It shares the existing
shader, texture and ticker, with no OCR, per-cover masks, additional textures or requests.
These are implementation bounds, not measured
GPU timings or evidence of production performance. WebGL failure leaves the normal cards
and captions usable. A production build should import only the Pixi modules it needs;
this standalone review uses the full browser bundle for offline portability.

The mock does not measure production artwork latency. Proposed follow-up work includes
targeted artwork invalidation, size-aware requests, cached encoded variants and avoiding
base64 transport. These require implementation and cold/warm timing checks after design
review; fast local mock images are not evidence that those changes have shipped.

## Portal flyout revision — September 28, 2026

`portal-surface.js` provides `WinnowPortalSurface` with configure, show, resize, hide and
dispose methods. `flyout.js` owns the accessible HTML, hover/focus lifecycle, cursor origin
and viewport placement. The HTML reading plane has a fixed height of 560 px on desktop
and 620 px in fullscreen, capped to the available viewport before opening. Narrow windows
use 85% of that space for a docked preview. Description excerpts fit whole lines and end in
an ellipsis when necessary; selecting the cover shows the complete description.

A 128-point CSS clip path and the shader use the same superellipse and ripple geometry.
The aperture grows and travels from the pointer for 360 ms after a 120 ms hover delay,
revealing the already laid-out words without scaling, translating or scrolling them.
Once revealed, the inset text drops its clip path; only the decorative rim keeps moving.
Its canvas includes the cursor
origin, allowing the opening to begin outside the final panel. Roundness changes the
superellipse exponent; Edge waviness changes ripple amplitude. Both controls run from
0–100%, default to 70% and 45%, update an outline preview and reset with Restore defaults.
They remain available for Still portal and are disabled for Simple preview.

The portal samples game artwork behind a gently uneven contour. A second,
private Pixi renderer is created lazily on cover hover or focus, at up to 1.5× resolution.
Pointer hover intent warms the renderer and shader before the visible entrance. The
entrance follows each display frame; the quiet moving rim targets 30 updates per second
without discarding fractional frame time. A wall-clock deadline prevents delayed frames
from extending the entrance. One clock updates the opening mask and shader uniforms.
Artwork stays fixed against the full reading plane as the aperture reveals it. Only Living portal runs a loop;
Still portal and keyboard focus draw once. Hiding, navigation, scrolling, dialogs, visibility
changes and blur stop rendering and detach the canvas. Page teardown destroys this renderer
without releasing resources shared with the independent cover renderer. A CSS image and shading layer
keeps the text available if graphics initialization fails or the context is lost. Its mask
completes the entrance and then stops updating.

TASK-364 browser checks covered Library and Discover shelves on desktop, fullscreen
keyboard navigation, the complete Guns, Love and Tentacles title, both slider endpoints,
restoring defaults, and Still/Simple comparisons. During the pointer entrance the panel
and text plane both measured 560 px high, with no text transform and `overflow: clip`.
After opening they retained those dimensions and had zero scrollable descendants.
Fullscreen at 760×560 used a 362 px side portal with a one-line description excerpt.
At 390×700 both surfaces docked above the footer without horizontal overflow; desktop
retained three description lines. Temporary viewport overrides were reset. The browser
reported no warnings or errors.

A Node/JSDOM smoke check exercised the pointer hover delay, fixed panel height, live
system reduced-motion changes and Escape/ARIA cleanup. A simulated WebGL initialization
failure completed the reveal, stopped its animation loop and kept still mode loop-free.
A contour check compared the HTML polygon against the shader's analytic boundary at nine
combinations of control values and four time samples; maximum numerical error was below
1.3e-13 pixels. Hide/dispose canceled pending animation frames.
JavaScript syntax and Git whitespace checks passed. OS preference switching, physical
touch, screen-reader output and GPU timings were not measured. No production frontend
code or real library preferences changed.

TASK-364 captures: [fixed portal details beside Outer Wilds](15-fixed-portal-reveal.png)
and [shape controls](16-portal-shape-controls.png). Use the live mock to assess the reveal,
edge movement and artwork. Capture 14 records the earlier TASK-363 treatment.

## Portal pacing check — September 28, 2026

TASK-365 addresses the reported sluggish, jittery reveal after TASK-364. The old 30 fps
gate reset its timestamp to the latest callback and discarded the remainder. The visible
entrance therefore skipped irregular numbers of display frames. Capping elapsed time also
extended the animation after a stall. Both HTML masks were rebuilt even after the text was
fully revealed.

Controlled Node calls to the actual surface's `tick` method compared commit `6d7976c` with
the revised clock. They supplied timestamps for 60/120 Hz displays, captured draw calls and
checked the opening deadline. A separate draw harness counted DOM lookups and mask writes
over 300 settled WebGL frames.

| Check | Before | After |
| --- | --- | --- |
| Entrance intervals at simulated 60 Hz | 33.3 / 50 ms | 16.7 ms |
| Entrance intervals at simulated 120 Hz | 33.3 / 41.7 ms | 8.3 ms |
| Completion after a 200 ms stall at 150 ms | 783.3 ms | 366.7 ms |
| DOM mask writes over 300 settled draws | 600 | 0 |
| DOM queries over 300 settled draws | 600 | 0 |

The new 360 ms entrance completes on the first available frame at or after its deadline;
the controlled tests settled at 366.7 ms. Ambient intervals were consistently 33.3 ms at
both simulated refresh rates. Invisible pixels beyond the halo now skip the expensive
star/cloud calculations. This last change was checked visually, not timed on the GPU.

For browser observations, append `&portalProfile=1` to the mock URL, hover a cover and
inspect `#card-flyout`'s `data-portal-profile`. The opt-in probe records up to 400 calls to
`draw`; without that query flag it does not wrap any methods. It measures JavaScript and
render-command submission, not GPU completion or presented frames. In this in-app browser,
Borderlands 2's desktop entrance measured 652.8 ms before and 360.9 ms after. The respective
225/390-sample snapshots reported median submission costs of 0.3/0.2 ms and 95th percentiles
of 0.5/0.4 ms. These are local observations, not a cross-device performance guarantee.

Browser checks exercised live pointer opening on desktop and fullscreen (560/620 px fixed
planes with no scrollable descendants), both shape sliders at maximum with a long title in
760×560 fullscreen, reset defaults and keyboard focus. Node/JSDOM verified the shorter hover
delay, a completed and stopped graphics fallback, live reduced-motion changes and Escape/ARIA
cleanup. Syntax and whitespace checks passed. Viewport overrides were reset. The normal
review URL is left without profiling enabled; [capture 17](17-smooth-portal.png) records the
settled appearance, while the live mock shows the timing change.

## Portal artwork revision — September 28, 2026

TASK-366 replaces the procedural star field with each game's local hero or landscape image.
Eight sample titles have a separate landscape source or landscape-only artwork; the four
remaining Borderlands 3 samples use their own covers. The image fills the fixed reading
plane with a centered crop. Stronger shading sits behind the text and footer hint, while
the rim retains its light and motion. Missing/loading samples and failed image decodes use
a neutral dark interior.

Hover intent starts decoding the local image without delaying the entrance. One promise
per URL reuses decoded images; private Pixi textures are created once on use and destroyed
with the surface. No new remote assets are requested. Switching titles clears the previous
scene immediately, and a generation check ignores late results from earlier hovers. The
CSS fallback reuses the same decoded image when graphics are unavailable.

Browser checks covered hero scenes for Hades and Outer Wilds, bright Sable artwork,
Borderlands 3's cover fallback and its missing-artwork state. Desktop and fullscreen
retain their 560/620 px reading planes. A Node/JSDOM check exercised out-of-order image
decodes, one decode per URL, repeat hovers, failed/missing images, graphics fallback,
still mode, teardown during a pending decode and private texture cleanup. Borderlands 2's
live desktop entrance measured 362.6 ms with zero scrollable descendants. A 390-call probe
reported median/95th-percentile submission costs of 0.2/0.3 ms. Versioned script URLs
prevent the preview from mixing cached catalog code with the new flyout; after reloading,
the browser reported no new warnings or errors. JavaScript
syntax and Git whitespace checks passed. These are local mock checks, not production
artwork latency or GPU measurements. The existing keyboard and reduced-motion paths
remain stationary; OS preference switching was not exercised in this revision.

Current capture: [Hades artwork inside the portal](18-hero-portal.png).

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
