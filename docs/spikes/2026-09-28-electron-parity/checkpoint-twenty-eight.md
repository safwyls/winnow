# Loading dragon geometry and lifetime — 2026-09-29

The Electron loading worker now accepts theme-color and size changes without replacing
its animation clock. The same drawing function serves both surfaces, including every
closed inner and detached contour. Disabling tracing or detaching the presentation ends
its worker, removes the canvas and rejects late progress publication; reattachment owns
a fresh circuit. Native visibility and startup readiness remain as recorded in
[checkpoint 27](checkpoint-twenty-seven.md).

## Verification

- Eleven controlled renderer cases pass in 173ms: `.tmp/dragon-components.log`. They check
  one queued frame, exact elapsed phases, stop/restart, retained progress after color and
  size changes, context failure and dash seam intervals.
- All 2,861 component and live API cases pass across 142 files without skips in 48.72s:
  `.tmp/dragon-full-integration.log`. Build and final typecheck pass:
  `.tmp/dragon-build.log`, `.tmp/dragon-final-typecheck.log`.
- Six native drawing cases pass in 7.0s: `.tmp/dragon-native-readback.log` and its copied
  `results.json`. Native SVG measurement verifies all fifteen contours at 101 phases.
  Dark/light colors at 88px and 100px render all nine original sample positions through
  the production draw function; pixel checks retain ink/glow, distinguish the intermediate
  positions, and require the circuit endpoint to exactly match its start.
- The initial combined native run passed the four complete startup workflows, native
  contour checks and worker lifetime. Its four readback matrices failed only their exact
  endpoint hashes: `.tmp/dragon-native.log`. The readback fixture now requests
  `willReadFrequently`, retaining the exact hash assertion. Chromium's
  [canvas implementation](https://chromium.googlesource.com/chromium/src.git/+/165d81dca6b980999d31b857be758373ed79a071/third_party/blink/renderer/modules/canvas/canvas2d/base_rendering_context_2d.cc)
  can switch from GPU to CPU after repeated reads when that option is unspecified.
  The passing explicit readback-mode comparison is consistent with that explanation;
  this run did not instrument Chromium's backend selection directly.
- The separate native worker case uses ordinary OffscreenCanvas rendering and verifies
  colors, resized backing pixels, increasing elapsed/frame counts, disable/re-enable,
  detach/reattach and no late frame delivery. Final dark-background capture inspected:
  `.tmp/dragon-native-worker.log` (4.6s). The probe now resets its background between the
  light-color matrix and this case; the app itself gets both colors from its active theme.
- The preceding list-suite setup correction passes all ten native list workflows twice
  in 1.8 minutes, with their persistence, keyboard, focus and deletion assertions intact.
  The complete expanded 214-case native suite is running; no combined pass is claimed yet.

All four original LoadingDragon methods now have ported evidence. Inventory:
1,044 ported, 552 retained backend, 15 framework-specific, 714 pending and 110 partial.
This remains an incomplete frontend migration. The rendering checks establish Windows
Electron behavior with simulated controller input, not physical controller hardware or
other desktop platforms.
