# Electron artwork loading measurement

Measured on Windows on 2026-10-03 for TASK-382, against the decoder in `0b1f5ad3`.
Electron 44.4.5 / Chromium 152.0.7977.130 ran both versions on the same local machine.

## Finding and change

The old renderer decoded base64 with `Uint8Array.from(atob(value), callback)`. Its callback
ran once per byte on the renderer thread before Chromium could decode the PNG. Main also
converted the backend's PNG bytes into a base64 string for IPC. The new production path
passes a `Uint8Array` through the named preload bridge directly into a PNG Blob. The
compatibility string path uses native `Uint8Array.fromBase64` where available, with an
indexed loop for older hosts. This removes the per-byte callback from both paths.

The desktop screenshot lightbox also borrows the same asset's already-decoded thumbnail
while the larger image loads. Its lease remains valid if the thumbnail strip detaches or
the cache evicts that entry. Fullscreen previews already use the lightbox's 1280-pixel
rendition and share it directly. Neither path speculatively downloads a missing preview.

## Controlled decoder measurement

These are initial medians of five timed trials after one warmup. Each trial creates a new
production `ArtworkDecoder`, uses its six decode permits, waits for every image, then
releases every Blob URL. Timing includes conversion, Blob creation and `Image.decode()`.
All requested images decoded successfully.

| Workload | Original base64 path | Optimized compatibility path | Binary path |
| --- | ---: | ---: | ---: |
| 24 covers, each 240 × 360 | 255.9 ms | 28.8 ms | 29.1 ms |
| One screenshot, 1280 × 720 | 104.6 ms | 18.0 ms | 17.3 ms |
| Three backdrops, each 1920 × 1080 | 808.4 ms | 110.8 ms | 100.1 ms |

The fixture creates deterministic textured, opaque PNGs using a seeded pixel generator.
Each workload repeats the same PNG at its given size; it does not reproduce a particular
user's library. PNG generation and binary input preparation happen outside the clock.
The benchmark opens a sandboxed native Electron renderer with an explicit temporary
profile and does not start the backend or contact an image service.

Run from `src/Winnow.Electron`, with dependencies installed, one run at a time:

```powershell
node scripts/benchmark-artwork.mjs 0b1f5ad3
node scripts/benchmark-artwork.mjs
```

Each run prints its medians and writes engine versions, PNG byte sizes and individual
trial timings to a new `.tmp/artwork-benchmark-*/results.json`. The optional Git revision
loads that revision's decoder and measures the base64 path. Without a revision it measures
both paths in the current working tree. The tool verifies decoded image counts and closes
its own Electron process. Absolute timings vary with machine load; there is no timing
threshold in CI.

A repeat using the committed benchmark tool produced 447.6 / 177.4 / 910.9 ms for the
three original-path workloads and 36.3 / 20.4 / 112.3 ms for the binary path, respectively.
The optimized compatibility path measured 50.3 / 22.1 / 120.0 ms. This confirms the direction
of the improvement while showing why the initial timings are not fixed performance promises.

## Scope and limits

The comparison measures a renderer bottleneck, not total application loading time. It
excludes network fetch, backend disk access, resizing/PNG encoding, IPC copying and final
painting. It also does not measure Avalonia and Electron side by side with a live library.
The binary bridge avoids base64 allocation in main, but this table does not quantify that
additional saving. First-time remote images can still wait on their provider.

Source inspection identified a separate difference: the reference Avalonia frontend keeps
a persistent local frontend image cache and derives smaller renditions from its cached
source. Electron uses the backend disk cache with per-width renderer caches. The backend
still decodes/resizes and encodes each requested PNG under four permits. Persistent
Electron caching or backend rendition caching would need separate measurement and an
explicit invalidation policy; neither is introduced in this change.

The existing 32 concurrent image loads, six decode permits, 32 MiB queued-image budget,
32 MiB unused decoded-image cache, two-minute freshness and five-minute encoded-entry
expiry remain. Binary queued images count their actual byte length. Visible consumers
retain their pixels, departing consumers cancel pending work, and existing cover retries
remain bounded. Main keeps credentials and validates both image channels identically.

## Verification

Component tests cover binary preference, compatibility decoding, queue accounting,
request cancellation, cache freshness, independent thumbnail leases, image replacement,
failure retention and avoiding previews from a different screenshot. Native Electron
checks exercise desktop and fullscreen artwork lifetimes, idle recovery, image geometry
and gallery controls. A held full-image response verifies that the real production
preload/main path keeps the desktop thumbnail visible until the binary replacement arrives.
Task execution results are recorded in TASK-382.
