# Afterglow: the quiet collection

September 28, 2026 · TASK-367 · Restored design study

Afterglow returns to the portrait-cover and caption treatment from commit d748cb6,
before material lighting, floating depth and portals. Covers remain flat; hover and
keyboard focus reveal captions over a shaded part of the artwork. The warm palette,
editorial typography and panoramic Discover hero remain. Filled 2:3 frames and eight
returning titles are retained as independent browsing improvements.

The effects direction now has its own [Rift design study](../2026-09-28-rift-design/README.md).
Its shell and layouts are designed around the floating cards and artwork portals. Rift
is an interactive proposal; the production Electron frontend remains Afterglow/Catalogue.
Production Afterglow also uses quiet captions, while shared effect components and saved
material preferences remain available to other themes.

## Open the mock

From the repository root, run:

    python -m http.server 8772 --bind 127.0.0.1 --directory docs/spikes/2026-09-27-afterglow-artwork-mock

Open [Library](http://127.0.0.1:8772/index.html?page=library) or
[Discover](http://127.0.0.1:8772/index.html?page=discover). The preview uses local sample
images and never connects to the backend or writes library data. The review strip switches
desktop/fullscreen and artwork states; Display options adjusts cover size, captions and palette.

## Verification and preserved evidence

The restored preview loads no Pixi, depth or portal scripts. Desktop keyboard focus
revealed the caption with zero canvases or tooltips. Production desktop/fullscreen
checks and the Rift checks are recorded in the new study and TASK-367.

[Current Afterglow capture](19-restored-afterglow.png).
Captures 01–08 record the early artwork/caption studies; 09–18 record the effects
experiments before the fork. Unloaded experimental helper files remain with those
historical captures. The detailed measurement methods and results for TASK-354 through
TASK-366 are available in this README at commit 66dbf61. They describe those recorded
versions, not the restored presentation.

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
