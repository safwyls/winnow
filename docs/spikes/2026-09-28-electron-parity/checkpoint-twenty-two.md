# Artwork ownership and visible feed shelves — 2026-09-29

The shared Electron Artwork component now measures its actual display width and pixel
density before requesting pixels. It uses the source buckets from 160 to 3840 pixels.
The original 400×600 cover fixture requests 480 once and consumes a warm entry; Fit/Fill,
dormancy and selection keep the same image and cover bounds. The saved CoverArtMode
preference now reaches both Avalon Library compositions.

An artwork cache admits at most 128 pending slots and runs six fetch/decode operations
at once. Its 32 MiB LRU bounds decoded scrollback; active leases keep evicted images valid.
One hundred requests for a slot share one factory and decoded object. Releasing the last
consumer cancels the named request. A replacement waits for canceled work to retire before
starting another factory. Null and failed results remain retryable at the same size.

Encoded PNG data retains the existing two-minute freshness and five-minute inactive
cache lifetime. Decoded images use owned Blob URLs. Detachment clears DOM sources before
releasing pixel holds. Shutdown refuses new admissions, waits for ignored cancellation
and blocked decoders, and prevents late encoded or decoded publication. The shared decoder
also serves fullscreen backdrops. Backend CoverPipeline and ArtworkEndpoints are unchanged;
they continue to own disk caching and request cancellation, rather than the renderer.

Fullscreen Home now displays its original LT/RT shelf hint. The original two-shelf fixture
runs at 1920×1080 and 1280×720: loading the feed while Library is mounted records no
exposures, Home records exactly its first two cards, RT adds the second shelf's two cards,
and LT does not duplicate records. The existing desktop and fullscreen modal/clipping
tests continue to exercise the real backend's surfacing records.

## Verification

- The initial two new exposure cases reached the hint assertion after passing their
  visibility assertions, then failed because the hint was missing:
  `.tmp/feed-exposure-before.log`. After the fix, all six new/existing feed native cases
  pass in 20.6 seconds: `.tmp/feed-exposure-after.log`.
- All 48 initial artwork/cache/decoder cases pass in 2.44 seconds:
  `.tmp/artwork-lifecycle-first.log`. Two additional integrated shutdown cases are
  included in the complete suite below.
- All six native artwork cases pass in 3.3 seconds:
  `.tmp/artwork-lifetime-native-second.log`. They bundle production Artwork, AvalonCover,
  styles and the cache in a sandboxed Electron window with deterministic PNGs and an
  instrumented bridge. They do not modify the production preload or add test routes.
- The first full component run passed 2,673/2,676 cases. Three merge artwork fixtures
  lacked measured dimensions and browser decoding. They now supply 96px bounds, decode
  their selected sources and assert the 160px request bucket and named request ID, while
  preserving the original provider-precedence assertions. The two affected suites then
  pass 148/148: `.tmp/artwork-merge-components.log`.
- The complete component/live-backend run passes 2,680 cases in 134 files without skips,
  44.40 seconds: `.tmp/artwork-integration-final.log`. Build and typecheck pass:
  `.tmp/artwork-build-final.log`.
- The expanded native suite passes all 178 cases in 7.2 minutes:
  `.tmp/artwork-verified-native.log`. Desktop and fullscreen warm-cover captures and
  both settled second-shelf captures were inspected. Cover bounds and selection remain
  intact; the 1280×720 and 1920×1080 feed layouts keep the shelf hint visible.

The probe's first bundle attempted to parse authored JSON-with-comments as ordinary JSON.
Its bundler now loads the existing `?raw` imports as text, matching production Vite. The
early cache test also mixed real and fake GC clocks; it now creates all timers under one
controlled clock and verifies pixel disposal as well as query removal.

Twelve more original methods have equivalent evidence. The inventory contains 949 ported,
540 retained backend, 13 framework-specific, 796 pending and 137 partial methods. The
complete migration remains in progress and all task acceptance criteria remain unchecked.
