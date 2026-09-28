# Winnow Rift

September 28, 2026 · TASK-367 · Interactive design proposal

Rift gives the material cards and artwork portals their own visual setting. Its graphite
and blue-black surfaces, moonstone green and violet edges, sans serif headings and fine
monospace labels make light and depth part of the whole interface. The working name is a
theme concept within Winnow, not a renamed product or a separately shipped frontend.

## Review

From the repository root:

    python -m http.server 8774 --bind 127.0.0.1 --directory docs/spikes

Open [Rift Discover](http://127.0.0.1:8774/2026-09-28-rift-design/index.html?page=discover)
or [Rift Library](http://127.0.0.1:8774/2026-09-28-rift-design/index.html?page=library).
The footer also links to [restored Afterglow](../2026-09-27-afterglow-artwork-mock/index.html?page=library).
Keep both directories together: images, bundled fonts, Lucide and Pixi are referenced from
the preceding study to avoid duplicating its assets. No network service, backend, live
library or launcher files are used.

Discover treats the featured game as a physical cover suspended in its world, with a
panoramic background and a small stack of translucent plates. Eight covers form the next
shelf; eight compact records provide a return path into recently sampled games. Library
uses the full canvas, with horizontal filters and search above the covers. Desktop has a
slim navigation rail. Fullscreen brings navigation into a central bottom dock and enlarges
covers; short windows reduce the heading and card dimensions to keep a full row visible.

Hovering or focusing artwork reveals its game details through a separate aperture. The
image and text stay at their final size; the opening reveals them. Only the active card
and portal render effects. The body does not scroll; the current page or collection pane
does. Long descriptions are excerpted in the portal and remain complete in the game dialog.

**Tune the atmosphere** controls cover size, compact captions, Matte/Satin/Foil, intensity,
selective metallic highlights, depth, maximum tilt and portal contour. Moonstone, Amethyst
and Ember change the interface palette. Still mode and system reduced motion keep the
material light and portal stationary. These are in-memory mock controls and reset on reload.
Search, filters, grid/records, featured-game switching, game dialogs and keyboard navigation
are functional against twelve sample games. No game launch action is simulated.

## Reuse boundary

`rift.css` and `index.html` define a new composition. `app.js` owns static fixtures and
navigation. `effects/` preserves the finish, depth, flyout and portal modules from the
pre-fork mock at 66dbf61. They retain their own lifecycle, image reuse and graphics fallback;
the fork supplies a shared motion preference and a cooler portal rim. This is separate
from the production React `ArtworkEffects`, `GamePreview` and `GameCard` APIs, which remain
available to themes. Porting Rift to those APIs is a subsequent step after design review.

The original Afterglow mock uses its pre-effect layout with the later filled-frame and
eight-title browsing improvements. Production Afterglow explicitly opts out of shared
materials and uses the new optional `overlay` caption mode; existing theme defaults,
Catalogue behavior, artwork cache improvements and saved material settings are preserved.

## Verification

Browser checks covered desktop Discover and Library, featured-game switching, search,
unplayed filtering, compact records and complete game descriptions. Hades and Borderlands
hero/cover fallbacks rendered through WebGL. Keyboard focus produced a still portal and
level raised card. Amethyst plus Still mode preserved the material finish with the portal
loop stopped; Roundness reached its maximum and Restore defaults restored the controls.
Pointer input tilted the artwork. Next/Previous keyboard focus left the featured panel
at zero internal scroll after switching its visual boundary to `overflow: clip`.
No browser warnings or errors were reported. Both entrypoints passed unique-ID, local
asset-reference and JavaScript syntax checks.

Fullscreen at 760×560 displayed a complete row and a 370 px preview without scrollable
portal descendants. At 390×700 the complete long Guns, Love and Tentacles title wrapped
in a docked preview, its description excerpted, and the navigation stayed visible. Discover
had no horizontal overflow at that width. Temporary viewport overrides were reset.

Production Afterglow passed TypeScript and build checks, with 199 tests passing and eight
existing backend-dependent tests skipped. Eight focused checks cover desktop/fullscreen
captions, retained material preferences, Catalogue and eight returning games. Browser
checks at 1280×720 and 680×620 covered quiet cards, keyboard captions, reduced motion and
no horizontal overflow. Build output retained two existing Zod annotation warnings.
No native app or real library was used. Physical controller, touch and GPU performance
were not measured for this study.

Captures: [Discover](01-rift-discover.png), [Library portal](02-rift-library.png),
[restored Afterglow](../2026-09-27-afterglow-artwork-mock/19-restored-afterglow.png).

## Assets

Artwork, fonts and library licenses are documented in the preceding study's
[asset provenance](../2026-09-27-afterglow-artwork-mock/README.md#asset-provenance).
Artwork remains the property of its respective owners; these local review copies do not
establish a production redistribution license.
