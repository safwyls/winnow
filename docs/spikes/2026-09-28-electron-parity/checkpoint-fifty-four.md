# Fullscreen Details section and cinematic parity

Fullscreen directional navigation now includes list-membership checkboxes. Long relationship
actions wrap below their title instead of squeezing it into a narrow column. The same
relationship layout remains usable in the desktop modal.

The cinematic backdrop now fills the window behind the safe margins. Screenshot previews
use the remaining reading height, with a 100-pixel minimum at the supported small-window
boundary. They preserve their full image and remain fully visible when focused after a
resize. Desktop retains its separate thumbnail strip and modal backdrop.

## Verification

- Build and typecheck pass: `.tmp/details-cinematic-build-final.log`.
- All 3,322 component/live API cases pass across 162 files without skips in 53.82 seconds:
  `.tmp/details-cinematic-integration-final.log`.
- All 45 native Details, backdrop and gallery cases pass without skips or retries in
  3.2 minutes: `.tmp/details-cinematic-native-final.log` and
  `.tmp/details-cinematic-native-final/results.json`.
- A further 25 native Details/controller cases pass at the source fixture's 3% safe margin
  without skips or retries in 1.9 minutes: `.tmp/details-controller-native-final.log` and
  `.tmp/details-controller-native-final/results.json`. Across these two batches, 55 distinct
  native cases pass, with 15 shared Details cases.
- All six cinematic cases also pass with the production Steam hero aspect and fitting hints:
  `.tmp/details-cinematic-fixture-final.log` and `.tmp/details-cinematic-fixture-final/results.json`.
- The six section cases cover populated and empty Updates, Journal and Library at
  2560×1440 and 1280×720, with 140% text and 120% interface at the smaller size. They use
  the original long update/expansion/list titles, notes and nullable ratings. Every action
  is reached by controller Down; membership toggles preserve the actual focused node and
  use the current list revision.
- The six cinematic cases reproduce the source landscape, including all four colored
  crop-detection bands, the saved/automatic key choice, long title, note and interface-scale
  matrix. Assertions cover full-window geometry, veils, separators, typography, single
  Details hydration, preview pixels and requested resolution through 4K, 720p reading/focus,
  complete About copy and backdrop URL disposal. Existing production HTTP tests cover
  saved-background precedence and Steam fallback independently of the rendering fixture.
- The initial section tests reproduced skipped checkboxes and squeezed relationship text.
  The cinematic tests reproduced the inset backdrop and previews taller than the reading
  viewport. Test-only corrections account for Chromium's fractional pixel snapping and
  separate viewport resizing from fullscreen-entry events.
- Normal and enlarged-text screenshots were inspected. The full landscape occupies the
  background; both previews retain their red, green, blue and yellow edges at each size.

Two original methods move from partial to ported. The inventory is 1,255 ported, 625 retained
backend, 23 framework-specific, 434 pending and 98 partial out of 2,435. These are contract
dispositions, not a product-completion percentage. The complete native-suite gate, remaining
feature parity and primary Electron release cutover remain open.
