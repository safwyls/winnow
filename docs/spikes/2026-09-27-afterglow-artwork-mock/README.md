# Afterglow artwork and caption study

September 27, 2026 · TASK-354 · Proposal for user review

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

- Library cards use aligned 2:3 frames. Images use `contain`, so Steam's 2:3 and IGDB's 3:4
  covers remain complete. Unmatched proportions receive a neutral matte; Sable deliberately
  demonstrates a landscape-only fallback. Production should prefer a portrait source when
  available, while respecting explicitly selected artwork.
- Captions reserve two title lines and one compact metadata row. They do not reserve a
  second description block. Long names have a hover title, a visible keyboard-focus tooltip,
  an accessible full name and a full-title preview when selected.
- Discover keeps its large landscape hero and editorial invitation. Returning games use
  small portrait thumbnails; the recommendation shelf uses the same compact caption rules.
- Display options change cover size, caption density and the palette. Defaults are
  **Balanced + Essential**. **With context** adds a short reason; **Titles only** hides
  metadata. These controls are in-memory demonstrations and reset on reload.
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

Browser checks at 1440×900 and 760×560 found no page or library horizontal overflow. Header
and footer remain inside the viewport and content scrolls within its pane. The wide desktop
library shows five equally sized cover frames (approximately 203×304 px). Missing and loading
art retain those dimensions. Short and long titles use the same caption height.

Exercised the six-result unplayed filter, one-result Tentacles search, clearing back to twelve,
complete long-title preview, compact records, three cover sizes, all three caption options,
both palettes and reset. At 1440 px, small/balanced/large desktop cards produce six/five/four
columns. Fullscreen arrow navigation advanced from Borderlands 2 to Borderlands 3. Checked
fullscreen Discover at the constrained window size, hero selection, landscape fitting and
missing/loading thumbnails. JavaScript syntax and Git whitespace checks passed.

Review captures:

- [Discover](01-discover.png)
- [Library](02-library.png)
- [Compact records](03-records.png)
- [Display options](04-display-options.png)
- [Fullscreen library](05-fullscreen.png)
- [Discover shelves](06-shelves.png)

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

DM Sans, Newsreader and JetBrains Mono are copied from the frontend's installed Fontsource
packages. Lucide comes from the preceding design study. Their licenses are included in
`assets`. The dragon mark comes from Winnow's renderer assets.
