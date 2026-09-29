# Winnow Rift

September 28, 2026 · TASK-371 · Interactive design proposal

Rift uses two ways to browse the same collection. Discover keeps a focused deck of
floating covers beside a persistent artwork portal. Library opens a dense gallery with
compact captions and filters. Cover-adjacent portal previews grow into a full details
page. A quiet star field connects
both views. Graphite, blue-black, moonstone and violet remain the palette; DM Sans and
fine monospace labels keep the interface quiet around the artwork.

The working name is a theme concept within Winnow, not a renamed product or a separately
shipped frontend. This is a mock for review; production Afterglow is unchanged.

## Review

From the repository root:

    python -m http.server 8774 --bind 127.0.0.1 --directory docs/spikes

Open [Rift Library](http://127.0.0.1:8774/2026-09-28-rift-design/index.html?page=library)
or [Rift Discover](http://127.0.0.1:8774/2026-09-28-rift-design/index.html?page=discover).
The footer links to [restored Afterglow](../2026-09-27-afterglow-artwork-mock/index.html?page=library).
Keep both directories together: images, bundled fonts, Lucide and Pixi are referenced from
the preceding study to avoid duplicating assets. No backend, live library or launcher
files are used.

Discover's searchable index leads into a sample recommendation sequence. The selected
cover and its two neighbors form a small deck; clicking a neighbor or using the arrow
controls shuffles through the current selection in 520 ms. Left/right keys use the same
directional motion, repeated input continues from the current poses, and Still mode or
system reduced motion switches immediately. The selected cover and **View game**
open the full details page.

Library presents alphabetically ordered covers across the available browsing width,
using the same materials and depth effects. Hover or keyboard focus opens a portal
beside the cover. It flips left when needed, or docks within a narrow viewport. The
preview contains the title, store, playtime, reason and description. It has no buttons
or pointer interaction; leaving the cover closes it. Keyboard focus keeps the preview
with its cover, Tab follows the cover order, and Escape dismisses it. Clicking the cover
or pressing Enter opens details. The fixed reading plane is up to 420 pixels high on
desktop and 440 in fullscreen. Card scale changes density;
the text setting can hide metadata or add a short reason to return. Arrow keys move by
column and row, and Home/End reach the first/last game.

Both views offer four lenses: all games, four unplayed games, eight games to return to,
or five installed games. These are fixture selections, not measured recommendation scores.

Opening a game expands its preview portal across the content viewport in 680 ms. The
new page stays at its final size while the aperture reveals it: the title, description
and artwork never scale. Discover uses its persistent portal as the same entrance.
The completed page includes a hero backdrop, full description, artwork and the known
library facts. The renderer stops after the reveal. **Back** or Escape restores the
browsing position and the originating cover or action. Navigation remains available
in the pinned app bar; the footer stays visible too.

Desktop keeps Discover's index and Library's filter rail beside the scene. Fullscreen
hides that rail; its button or `/` opens it again. Discover stacks its deck and portal on
narrow windows. Library keeps a scrolling gallery, with a docked preview when there is
no space beside a cover. Previews have a fixed reading plane without internal scrolling;
long descriptions clamp there and remain complete on the independently scrolling details
page. Still portal, Simple preview and reduced motion open details immediately.

**Atmosphere** retains Matte/Satin/Foil, intensity, metallic highlights, floating depth,
maximum tilt, roundness, waviness and Still mode. **Portal activity** changes edge motion
independently of shape: 0% freezes the rim, 40% keeps the original pace and 100% moves
roughly four times faster. The small shape preview animates while adjusting it. Still
portal, Still mode and system reduced motion disable the activity control; keyboard
selection continues to show a stationary portal. Card scale changes the Discover deck
and Library density. Moonstone, Amethyst and Ember change the interface palette.
**Background** controls the star field and brightness. The field is still, so it needs no
continuous animation loop and behaves the same under reduced motion. Controls are
in-memory and reset on reload. Search with no matches shows a recoverable empty state;
missing/loading artwork keeps the reading layout and metadata available.

## Reuse boundary

`index.html`, `rift.css`, `gallery.css` and `app.js` own composition, fixtures and selection.
`options.css` holds the options dialog; `journey.css` styles previews and the details page.
`effects/starfield.js` renders one decorative canvas at startup and after resizing;
brightness only changes its opacity. It has no card or portal dependency.

`effects/deck-shuffle.js` is generated from the production renderer's reusable
`components/deck-shuffle.ts`. It animates outer cover transforms and opacity independently
of materials, without changing selection or delaying activation. Regenerate it from
`src/Winnow.Electron` with:

```powershell
npm exec -- esbuild src/renderer/components/deck-shuffle.ts --bundle --format=iife --global-name=WinnowDeckShuffle --outfile=../../docs/spikes/2026-09-28-rift-design/effects/deck-shuffle.js
```

`effects/portal-surface.js` owns the reusable material and fixed-plane reveal, including
an optional source rectangle for the full-view expansion. `effects/aperture.js` connects
Discover selection and appearance controls to that renderer. `effects/library-preview.js`
owns hover intent, keyboard access and cover-relative placement. `effects/details.js`
owns the full details page, expansion lifecycle and focus restoration. Each surface uses
the same shape and activity controls. The earlier `effects/flyout.js` adapter is not loaded.

Only the active cover uses a material renderer. Preview portals render at 30 Hz while
ambient motion is active, and settle at zero activity, for keyboard input, Still mode or
window blur. Hidden documents and dialogs stop their loops. The full-page expansion runs
only during its entrance and stops when complete or interrupted. The cover finish and
pointer depth retain their separate modules, image reuse and graphics fallback.

This mock remains separate from the production React `ArtworkEffects`, `GamePreview`
and `GameCard` APIs. Integration into the Electron frontend follows design review. The
first Rift proposal and the Afterglow restoration are recorded at commit 257d5e5; their
captures remain evidence of that version.

## Verification

Library previews were exercised with pointer hover, crossing from cover to preview,
keyboard focus and Tab into View game. Both left and right placement were verified.
Desktop and fullscreen opened full details, including the longest fixture title. The
390 px fullscreen fallback docked the preview inside the viewport and showed the complete
missing-art details page without horizontal overflow. Returning restored the original
cover focus and the exact 210 px gallery scroll offset. Escape dismissal, direct cover
opening, navigation out of details and the Discover entrance were checked.

The browser exposed the expanding polygon and then the completed page with its clip
removed and renderer stopped. A controlled-clock Node check verified the source-sized
opening, intermediate growth, 680 ms completion, stopped loop and full viewport corner
coverage at roundness extremes. Still mode opened the page immediately. These checks
exercise the transition but do not constitute a GPU performance benchmark.

The preceding gallery study checked filters, search recovery, star controls and the
unchanged material options, including palette changes and pointer-following tilt.

Activity controls were checked in desktop Discover and fullscreen Library, including
zero, maximum, reset to 40% and disabled states for Still portal and Still mode. The
settings preview moved at maximum and froze at zero. A controlled-clock Node check
exercised the renderer's zero/default/maximum rates, pause/resume without a time jump,
opening completion at zero activity, and keyboard/fallback loop guards.

All JavaScript modules passed Node syntax checks, and the entrypoint passed unique-ID
and local-reference checks. Rift reported no browser warnings or errors. Production and
Afterglow files were unchanged. Physical controller/touch hardware and GPU performance
were not measured; the static star field adds no recurring render work.

Current captures: [Cover-adjacent portal](08-rift-cover-preview.png),
[Full details page](09-rift-game-details.png).
Prior compositions: [Library inspector](06-rift-library-gallery.png),
[Discover star field](05-rift-starfield-discover.png), [Portal activity control](07-rift-portal-activity.png),
[Desktop workspace](03-rift-workspace.png), [Focused fullscreen](04-rift-fullscreen.png),
[First Discover](01-rift-discover.png), [First Library portal](02-rift-library.png).

The TASK-374 shuffle follow-up was checked in desktop and fullscreen in the mock and
production renderer. Directional controls produced intermediate card transforms and
settled on the selected game. Production checks also covered rapid keyboard reversals,
focus retention, details activation during the shuffle and immediate reduced-motion
selection. The shared controller's focused tests and the Electron build passed; see
the [integration evidence](../2026-09-28-rift-integration/README.md#discover-shuffle--task-374).

## Assets

Artwork, fonts and library licenses are documented in the preceding study's
[asset provenance](../2026-09-27-afterglow-artwork-mock/README.md#asset-provenance).
Artwork remains the property of its respective owners; these local review copies do not
establish a production redistribution license.
