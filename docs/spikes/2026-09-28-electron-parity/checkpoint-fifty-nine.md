# Fullscreen sizing and rendering — 2026-09-30

TASK-381.4 restores the original effective 0.85 baseline behind displayed 100% interface
scale. At reference and larger sizes, a uniform viewport factor preserves the logical
canvas and physical safe margins. Home computes its cover target in reference coordinates,
so 4K increases resolution without increasing cover density. Empty saved scales fall back
to the baseline; the application service ignores the old key and persists later adjustments
under the existing v2 key. Text size and the other preferences remain independent.

Fullscreen fitted covers sample each adjoining image edge once per decoded asset and fill
only uncovered strips. Fill mode and release clear the canvas, its center stays transparent,
and dormancy applies to the image and padding together. Desktop keeps its original surface.
Shelf targets restore 44px buttons, 4px gaps and 12/16px dots; the entire rail fits its row
without adding controller focus stops. Pointer selection retains controller focus.

Activity separates its bold item titles, dates and regular uppercase note label. Notes
open in a full reading page with a bounded measure, saved safe margins, initially focused
Back outside the scrolling text and Up/Down scrolling. Closing restores the invoking
action on both surfaces. Desktop retains its existing note dialog. The original journal
and contour SVGs now decorate Activity and Settings across the whole fullscreen canvas,
using live theme colors behind the inset controls.

The earlier root LB/RB omission is corrected in `d5746fd0`. The current task's regression
checks include those hints across root pages and preserve their absence from desktop and
Details. Source artwork and glyph assets retain their original licenses.

## Source equivalence

All 19 assigned source methods have per-method evidence in
`src/Winnow.Electron/tests/migration-fullscreen-sizing.json`.

- Backdrop tests retain backend candidate ordering and add integrated desktop portrait/
  backdrop ownership through failure, resizing, disposal and late completion. Native
  pixel tests cover the original four cinematic/width combinations, independent transition
  geometry, fading to Ground, and both exact cover-padding fixtures.
- Home checks use six shelves of twenty cards, both margin/text matrices, a shelf change
  at 80%, unchanged cover rectangles for long titles and empty/short/long reasons, and all
  four root backdrops at 10% margins. Smaller windows retain responsive CSS typography:
  1280-wide measurements normalize to the original 1920-wide 64px title and 72px reason
  height. This replaces the small-window Avalonia Viewbox mechanism. The native scaling
  tests directly check the original effective transform and logical canvas at 1920×1080
  and 3440×1440, with an additional 3840×2160 density comparison.
- Reading tests retain 24 complete paragraphs at both source sizes and add 10% safe margins
  and return focus. Activity uses the source 5,400-second manual session, rating and note,
  asserts computed title/date/label roles, and switches to an empty updates week.
- Refresh tests observe one realized-content DOM mutation while retaining neighboring
  wrappers, with none for offscreen content, unchanged selection or animation frames.
  This is React's observable consumer boundary; no unused `RowsChanged` event is added.
  Scheduled native frames finish the eight-row transition within three seconds. Forward
  and reversed moves retain their arranged position until a new frame. Ten shelves of
  ten cards retain viewport/footer geometry and one hero update per move at all three
  source text scales. The original elapsed/layout counters are diagnostics, not thresholds.
- Two migrated application-service theory cases exercise legacy values, v2 writes, change
  publication and reload while preserving unrelated preferences. The migration audit now
  accepts named `.cs` evidence in `Winnow.Application.Tests` alongside backend HTTP tests;
  all other evidence checks and the frozen source inventory remain in place.

## Verification

- Build and typecheck pass: `.tmp/fullscreen-sizing-build.log`.
- All **3,369 component/live API cases across 165 files** pass without skips in **54.20 seconds**:
  `.tmp/fullscreen-sizing-components-final.log`. The first run exposed an obsolete fullscreen
  Close-note selector; the replacement Back action now has component and native coverage.
- **Ten native scale/reading/backdrop cases** pass together in **two minutes**:
  `.tmp/fullscreen-sizing-information-final.log` and `.tmp/fullscreen-sizing-information-final/`.
  Initial reading runs exposed fixture startup synchronization and missing return focus;
  both are corrected. The final 4K case checks cover count and proportional cover/hero size
  after screenshot inspection caught a second application of the viewport factor.
- **Eight native shelf/row cases** pass in **20.6 seconds**, and **seven native artwork cases**
  pass, including the final missing-image guard. Evidence: `.tmp/fullscreen-sizing-rows.log`,
  `.tmp/fullscreen-sizing-rows/`, `.tmp/fullscreen-sizing-native-initial.log` and
  `.tmp/fullscreen-sizing-native-initial/`. The latter batch also passed all 22 Home/Library
  layout cases before the final viewport-factor repair; subsequent regressions are separate.
- The final regression batch passes all **69 native cases in 6.6 minutes**: 22 Home/Library
  layout, 15 Details, 25 lifecycle/navigation and seven controller-status cases. Evidence:
  `.tmp/fullscreen-sizing-regressions.log` and `.tmp/fullscreen-sizing-regressions/`.
  This includes the small-window, large-text, ultrawide and nine-palette matrices, with no
  skipped or retried cases. Together with the focused suites, this is **94 distinct native
  cases**, not one full aggregate run.
- The final reading-measure refinement scales the 1200px reference cap with smaller-window
  typography. Both source-size cases pass again in **18.9 seconds**:
  `.tmp/fullscreen-sizing-reading-final.log` and `.tmp/fullscreen-sizing-reading-final/`.
  `.tmp/fullscreen-sizing-native-summary.json` joins each case's latest result; all 94 pass.
- Both application-service theory cases pass in an isolated .NET build. The TRX is
  `tests/Winnow.Application.Tests/TestResults/fullscreen-scale.trx`; build artifacts use
  `.tmp/fullscreen-scale-artifacts/`. This does not constitute a full .NET rerun.
- Inspected the 1280-wide reading page, desktop note dialog, fullscreen Activity hierarchy,
  ultrawide Settings contours, 4K Home, fitted artwork pixels and constrained shelf rail.
  A bounded source review found the reading-margin, missing-decoration and label-cascade
  gaps; each has a production correction and explicit native assertions.
- All changed Electron files pass Prettier; `git diff --check` passes. The migration report
  regenerates successfully; `.tmp/fullscreen-sizing-migration-gate.log` records the expected
  nonzero complete-migration gate while the remaining 477 methods are unresolved.

## Remaining validation

The inventory has **1,303 ported, 625 retained backend, 30 framework-specific, 391 pending
and 86 partial** methods. The complete migration gate still fails for **477 unresolved
methods**. This task closes its 19 methods, not the entire port.

Native fixtures use actual Electron windows with fixed client dimensions and the production
mode-change event, disposable data directories, seeded libraries and `--no-sync`. Deterministic
artwork/activity and simulated controller input establish repeatable rendering behavior,
not physical-controller or TV-distance acceptance. TASK-381.40 retains that validation.
Final aggregate migration/release checks and the primary Electron release cutover remain queued.
This checkpoint stops for user review before TASK-381.5, controller keyboard and accessible
modal routes.
