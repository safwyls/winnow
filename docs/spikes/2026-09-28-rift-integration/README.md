# Rift production integration — 2026-09-28

TASK-372 integrates the approved Rift study as an independent built-in Electron design.
Afterglow remains the default. Theme Studio switches compositions while preserving each
design's appearance and layout; interface scale and reduced motion remain shared.

## Verification

- `npm run build`: passed TypeScript and Electron main, preload and renderer builds.
  Rollup reports two existing Zod comment-annotation warnings; it removes those comments.
- `npm test` with both live-test environment variables pointing to
  `C:/Temp/winnow-electron-rift-20260928`: 26 suites passed, 236 tests passed, two skipped.
  The backend was started with `--data-dir` pointing to that directory, `--seed-sample`
  and `--no-sync`. The skips require a recorded session or credential-dependent metadata
  conditions. No real library or provider files were modified.
- Theme tests cover authored defaults, persisted design snapshots, startup reload,
  compatibility, invalid profiles and edits during external-theme loading. Portal tests
  cover fixed reading bounds, expansion coverage, shape normalization, activity zero,
  reduced motion, color updates, visibility and renderer cleanup. Interaction tests cover
  keyboard entry, dismissal, details focus and both presentation modes.
- Production React screens were exercised in the in-app Chromium browser through the
  explicit fixture bridge in `tests/preview`, at 390×700, 1280×720 and 1920×1080. This
  bridge supplies local artwork and representative library/API records; it has no real
  writes or launcher actions. It is excluded from production builds.

## Observed behavior

Desktop and fullscreen render distinct Rift Discover and Library layouts. The gallery uses
internal scrolling with a pinned header and footer. At 1280×720 its balanced setting shows
nine columns; the 2,000-game fixture at 1920×1080 mounted 50–80 covers as scrolling moved
through the collection. Returning from details restored the distant row and selected cover.
This is a DOM-size observation, not a GPU timing or memory benchmark.

Pointer movement changed both tilt axes after an accent edit. A rightmost cover opened its
portal to the left, and the pointer could cross into its actions. Keyboard users could enter
the preview with Tab, dismiss it with Escape, open details, and return to the same cover.
The narrow layout docked a 354×540 preview within the viewport; its reading plane did not
scroll. Portal expansion ended with focus on Back, removed its temporary GPU canvas, and
left details independently scrollable. Desktop and fullscreen retained separate journeys.

Theme Studio restored Afterglow's original accent and quiet covers, then restored Rift's
custom accent. Its portal sample updated with the controls; activity zero stopped ambient
edge animation. Missing covers retained their game names, empty libraries offered setup,
and unmatched searches offered filter recovery. Shared management, journal and settings
screens remained available. Final hover → preview → details → Back navigation produced
no new browser warnings or errors after correcting shader precision, renderer cleanup and
shared details action keys.

## Captures and limits

These captures show the production renderer with fixture records, not a native packaged
Electron session. The small fixture reuses hero artwork for several titles. Production
uses the normal cached `Artwork` component and authenticated preload bridge.

![Rift Discover](discover.png)
![Rift Library with adjacent preview](library-portal.png)
![Expanded game details](details.png)

Physical controller use, TV-distance readability and low-end GPU performance were not
measured. The existing portable executable was not repackaged by this task; use the current
source build or run the documented packaging command to update it.
