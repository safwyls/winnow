# Winnow Rift

September 28, 2026 · TASK-368 · Interactive design proposal

Rift makes browsing a sequence of selected worlds. A searchable collection index leads
into a floating deck of covers and a persistent artwork portal. The portal is the reading
surface: it stays open while the pointer moves away, and choosing another game changes
both the deck and its details. This composition replaces the banner, shelves and grid of
the first study. Graphite, blue-black, moonstone and violet remain the palette; DM Sans
and fine monospace labels keep the interface quiet around the artwork.

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

The index is the fast path for scanning and search. Library orders it alphabetically;
Discover offers a sample recommendation sequence. Four lenses select all games, four
unplayed games, eight games to return to, or five installed games. These are fixture
selections, not measured recommendation scores. The selected cover and its two neighbors
form a small deck; clicking a neighbor or using the arrow controls advances through the
current selection. The selected cover and **View game** open the complete description.

The portal gives details a stable home beside the cover. Pointer selection opens its
aperture from the nearby deck edge toward the reading plane. Its artwork and text never
scale during the reveal. Keyboard selection opens it still. The portal remains open
rather than appearing over other games when the pointer crosses the index.

Desktop keeps the searchable index alongside the scene. Fullscreen hides it and enlarges
the scene; the index button or `/` brings it back. Selecting a result closes the drawer
and restores keyboard focus to the selected cover. Arrow keys move through the current
selection; up/down also work within the index. On narrow windows the cover and portal
stack in one scrolling workspace. Header and footer stay visible, and the portal itself
has no scrollbar. Short windows scroll the workspace as needed.

**Atmosphere** retains Matte/Satin/Foil, intensity, metallic highlights, floating depth,
maximum tilt, roundness, waviness and Still mode. Card scale changes the deck cover size;
index text can be minimal or include a reason to return. Moonstone, Amethyst and Ember
change the interface palette. Controls are in-memory and reset on reload. System reduced
motion shares the still path. Search with no matches shows a recoverable empty state;
missing/loading artwork keeps the reading layout and metadata available.

## Reuse boundary

`index.html`, `rift.css` and `app.js` own the workspace composition, fixtures and selection.
`options.css` holds the options dialog. `effects/aperture.js` connects selection to the
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

Browser checks covered desktop at 2025 and 1280 px widths and fullscreen at 1280 and
760 px widths. The 390 px fallback stacked the deck and portal without horizontal scroll;
the complete long Guns, Love and Tentacles title and its View game action remained
readable. Portal content height stayed fixed with no scrollable reading plane. The
workspace scrolls at constrained heights while the app bar and footer remain pinned.

Search, no matches, single-result controls, eight-game return filtering, direct selection,
fullscreen index dismissal and full-description dialogs were exercised. Keyboard selection
produced a still portal. Missing artwork and the mixed-proportion Hades cover preserved
the 2:3 card frame and details. Foil, 60% intensity, Amethyst, 100% portal roundness and
Still mode worked together; the portal loop stopped. After disabling Still mode, pointer
input produced nonzero X/Y card tilt and a material canvas with the changed palette.

All JavaScript modules passed Node syntax checks. The entrypoint passed 53 unique-ID and
10 local-reference checks. The Rift browser tab reported no warnings or errors. Production
files and the restored Afterglow study were unchanged; no production build was needed.
Physical controllers, touch hardware and GPU performance were not measured.

Current captures: [Desktop workspace](03-rift-workspace.png),
[Focused fullscreen](04-rift-fullscreen.png).
Previous study: [Discover](01-rift-discover.png), [Library portal](02-rift-library.png).

## Assets

Artwork, fonts and library licenses are documented in the preceding study's
[asset provenance](../2026-09-27-afterglow-artwork-mock/README.md#asset-provenance).
Artwork remains the property of its respective owners; these local review copies do not
establish a production redistribution license.
