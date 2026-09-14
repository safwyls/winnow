# Fullscreen stutter and flicker — 13 September 2026

## Recording observations

The supplied `Winnow_wEa8xUcVxN.mp4` is 78.4 seconds, 3440×1440, recorded at
30 frames per second. The app footage predates the fixes in TASK-261.

- At 33.533s (frame 1006), switching from the full Library to Installed games
  shows placeholder covers across the grid. Artwork is visible again in frame 1007
  at 33.567s. Collection changes around 35.7s show similar short reloads.
- During the Home shelf change around 55.2s, several captured frames have almost
  unchanged card positions before movement resumes around 55.3s. A 30fps recording
  cannot distinguish missed app frames from capture timing or identify a blocked stack.

ShareX's bundled FFmpeg extracted grayscale frames at 860×360. Adjacent-frame
differences located candidate discontinuities; timestamped strips were inspected
visually. Differences alone are not proof of flicker because scrolling also changes
many pixels. The recording and extracted frames remain outside the repository.

## Reproduced causes

1. **Row coordinates changed before layout caught up.** `Show` changed the target row
   and its render translation while child bounds still used the previous target.
   A deterministic test measured a row moving from Y=600 to Y=1200 immediately on
   `Show`, then returning to Y=600 after layout. Reversal had the same inconsistency.
   The viewport now computes translation from the last arranged row origin.
2. **The animation clock included preparation work.** A background dispatcher timer
   started before row creation, focus handling and layout completed. The first update
   could therefore skip a substantial part of the 220ms ease-out. Render-frame
   callbacks now establish the start timestamp after preparation, and stale callbacks
   cannot revive an interrupted or detached animation.
3. **Navigation rebuilt unchanged UI.** A Home shelf change rebuilt its hero twice
   and emitted three page-change notifications, each replacing both footer hint trees.
   Tests now require one hero update and preserve footer controls when hint text is
   unchanged. Page-change notifications fall from three to two in this fixture.
4. **An interrupted backdrop fade jumped to its intermediate image.** If C completed
   while A→B was still blending, the old code discarded A and made B fully opaque
   immediately. A regression test failed against that behavior. The completed latest
   replacement now waits for the visible blend to finish, with bounded lease ownership
   and cleanup for superseding selections, reduced motion and detachment.
5. **Backdrop metadata reads could block focus.** Microsoft.Data.Sqlite's asynchronous
   calls [execute synchronously](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async).
   The focus path invoked those reads directly on the dispatcher.
   It now captures identity on the UI thread and reads metadata on a worker, cancelling
   superseded selections and rejecting stale results. A gated synchronous repository
   test verifies that the dispatcher remains available while the read is blocked.
6. **New cover controls requested an unnecessary decode.** Before layout, a 400px
   cover requested the 240px bucket and then the 480px bucket. A regression test
   reproduced both acquisitions. It now requests only the display bucket after bounds
   exist and applies a warm hit synchronously. Fullscreen paint also reacts once to
   `Art`, rather than to all five derived presenter notifications for the same result.

These are reproduced implementation faults or unnecessary work. Their relative
contribution to every pause in the recording is not established. Cold or evicted art
still loads asynchronously, so the one-frame collection reload is not proven eliminated
for every cache state.

## Timing method and limits

The isolated Release headless fixture used a 1920×1080 window, ten shelves of ten
preview cards, no decoded cover art, and eight Down inputs at each text scale.
It measured `Handle` separately from `RunJobs` plus `UpdateLayout`.

At text scale 1, the initial input-handler range was 9.15–11.19ms and settled layout
was 30.95–41.70ms. The first run after changes measured 6.06–8.38ms and 11.34–34.99ms.
Other scales and repeats under concurrent builds were noisy: some settled measurements
grew rather than fell. These numbers are diagnostic observations, not an FPS result or
a reliable speedup estimate. Duplicate-tree counts and coordinate continuity are the
regression gates; wall-clock thresholds are not.

No unwanted viewport resize or recreation occurred during ordinary shelf navigation
in this fixture. Physical-controller/GPU presentation with the user's real library
has not been remeasured after the fixes.

## Verification

`FullscreenStutterDiagnosticsTests`, `FullscreenCoverRequestTests`, and the expanded
`FullscreenBackdropTests` exercise the reproduced cases. Existing fullscreen row,
layout, artwork-order and cover-lifetime tests cover integration and reduced motion.
Desktop presentation and shared cover-pipeline behavior are unchanged; existing
desktop/fullscreen cover-selection and lifetime cases verify that boundary.

The final Release solution build completed with zero warnings or errors. The combined
fullscreen, cover, artwork-preference, supplemental-feed and recommendation-composition
UI run passed 273 tests. This includes an animation that completes through scheduled
render callbacks without manually advancing the test animation clock.
