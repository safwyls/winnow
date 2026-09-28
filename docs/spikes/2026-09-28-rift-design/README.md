# Winnow Rift

September 28, 2026 · TASK-369 · Interactive design proposal

Rift uses two ways to browse the same collection. Discover keeps a focused deck of
floating covers beside a persistent artwork portal. Library opens a dense gallery with
compact captions, filters and a smaller portal on the right. A quiet star field connects
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
controls advances through the current selection. The selected cover and **View game**
open the complete description.

Library presents alphabetically ordered covers in several rows, using the same materials
and depth effects. A compact title and store/playtime line sit beneath each cover. Select
a cover to update the persistent portal without leaving the gallery. **Hide details**
expands the gallery across the available space. Card scale changes its density; the text
setting can hide metadata or add a short reason to return. Arrow keys move by column and
row, and Home/End reach the first/last game.

Both views offer four lenses: all games, four unplayed games, eight games to return to,
or five installed games. These are fixture selections, not measured recommendation scores.

The portal gives details a stable home beside the cover. Pointer selection opens its
aperture from the nearby deck edge toward the reading plane. Its artwork and text never
scale during the reveal. Keyboard selection opens it still. The portal remains open
rather than appearing over other games when the pointer crosses the index.

Desktop keeps Discover's index and Library's filter rail beside the scene. Fullscreen
hides that rail; its button or `/` opens it again. Discover stacks its deck and portal on
narrow windows. Library keeps a scrolling gallery and opens a dismissible details portal
when a cover is selected. Escape or the close button returns focus to the selected cover.
The app bar and footer stay fixed, and the portal has no internal scrollbar.

**Atmosphere** retains Matte/Satin/Foil, intensity, metallic highlights, floating depth,
maximum tilt, roundness, waviness and Still mode. Card scale changes the Discover deck
and Library density. Moonstone, Amethyst and Ember change the interface palette.
**Background** controls the star field and brightness. The field is still, so it needs no
continuous animation loop and behaves the same under reduced motion. Controls are
in-memory and reset on reload. Search with no matches shows a recoverable empty state;
missing/loading artwork keeps the reading layout and metadata available.

## Reuse boundary

`index.html`, `rift.css`, `gallery.css` and `app.js` own composition, fixtures and selection.
`options.css` holds the options dialog. `effects/starfield.js` renders one decorative canvas
at startup and after resizing; brightness only changes its opacity. It has no card or
portal dependency and can be reused independently. `effects/aperture.js` connects selection to the
existing portal renderer; it owns the persistent surface lifecycle, sizing and settings.
The cover finish, pointer depth and portal shader retain their separate modules, image
reuse and graphics fallback. `effects/flyout.js` is the prior hover adapter and is no longer
loaded by this study. Only the active cover uses a material renderer. The portal renders
at 30 Hz while ambient motion is active, and settles for keyboard input, Still mode or
window blur. Hidden documents and dialogs stop its loop.

This mock remains separate from the production React `ArtworkEffects`, `GamePreview`
and `GameCard` APIs. Integration into the Electron frontend follows design review. The
first Rift proposal and the Afterglow restoration are recorded at commit 257d5e5; their
captures remain evidence of that version.

## Verification

The Library displayed all twelve fixtures in two complete rows at 2025 px width with its
portal visible. At 1280×720, desktop displayed ten complete cards and fullscreen displayed
all twelve before scrolling. Hiding details widened the gallery and stopped the hidden
portal renderer. Small card scale increased the number of columns. The 390 px fallback
kept a scrollable gallery within the viewport; selecting the longest title opened a fixed
portal with its complete title and View game action. Close restored focus to the cover.
No horizontal body overflow or internal portal scrollbar was observed.

Browser checks covered installed filtering, search with no matches and recovery, missing
artwork, selection, row-wise keyboard navigation and portal hide/show. Keyboard selection
kept portal motion still. Star brightness reached 100%, its visibility control removed
the field, and Restore defaults returned it to 65%. Discover retained its deck and portal
with the star field behind them. The prior study verified the unchanged material options,
including palette changes and pointer-following tilt.

All JavaScript modules passed Node syntax checks, and the entrypoint passed unique-ID
and local-reference checks. Rift reported no browser warnings or errors. Production and
Afterglow files were unchanged. Physical controller/touch hardware and GPU performance
were not measured; the static star field adds no recurring render work.

Current captures: [Library gallery](06-rift-library-gallery.png),
[Discover star field](05-rift-starfield-discover.png).
Prior compositions: [Desktop workspace](03-rift-workspace.png),
[Focused fullscreen](04-rift-fullscreen.png), [First Discover](01-rift-discover.png),
[First Library portal](02-rift-library.png).

## Assets

Artwork, fonts and library licenses are documented in the preceding study's
[asset provenance](../2026-09-27-afterglow-artwork-mock/README.md#asset-provenance).
Artwork remains the property of its respective owners; these local review copies do not
establish a production redistribution license.
