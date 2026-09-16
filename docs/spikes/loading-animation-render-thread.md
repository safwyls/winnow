# Loading animation under UI-thread work

## Source trace — 2026-09-15

TASK-302 replaces the loading mark's UI-thread RequestAnimationFrame loop with an Avalonia
CompositionCustomVisualHandler. The old loop advanced and rebuilt its drawing on the same
thread that applies library results and performs layout. A busy UI thread therefore paused
the glow even though the application had not intentionally stopped or restarted it.
Fullscreen also explicitly stopped the trace before starting the reveal fade.

The compositor now owns the SVG paths, contour measurement, reusable drawing objects and
animation clock. Only immutable color/state snapshots enter the handler. Atomic progress
values return to the UI; theme and size updates preserve the current circuit. Detachment
releases the renderer's native drawing objects. Both presentations keep the trace running
through the fade and wait for a circuit measured from the first rendered animated frame,
in addition to library/feed/layout readiness. Reduced motion and hidden desktop preparation
do not wait for the circuit. This replaces the temporary five-second desktop preview edit.

## Native stall experiment

An isolated native Avalonia window hosted only LoadingDragon over a solid background; it
did not start Winnow's production host or access its database. The probe was launched with
a temporary data directory and closed itself after the measurement.

After five rendered frames, its UI-thread callback recorded RenderedFrameCount, Phase and
RenderThreadId, deliberately blocked for 1200ms with Thread.Sleep, then read the same values
before yielding the UI thread. It then waited for HasCompletedCircuit. The native window
used Avalonia11.3.20/Skia on Windows, matching the app.

| Scheduling path | UI thread | Render thread | Frames during UI stall | Phase before → after |
|---|---:|---:|---:|---|
| UI-frame callback baseline | 2 | 2 | 0 | 0.0975 → 0.0975 |
| Compositor custom visual | 2 | 4 | 195 | 0.0873 → 0.7624 |

The baseline routed the control's injected frame scheduler through the native window's
RequestAnimationFrame, reproducing the prior scheduling dependency. Both paths completed
a circuit after the UI resumed, but only the compositor advanced while it was blocked.
The measured frame count depends on the display/renderer; it is evidence of independent
progress, not a promised frame rate or a profile of the user's library.

## Regression coverage

Controlled frame tests cover fast loads waiting for a full circuit, slow loads preserving
phase when readiness arrives, motion continuing through the fade, reduced motion, retry,
rapid re-entry and cancellation. A real compositor test renders a circuit and changes the
mutable theme brushes and size without restarting progress. Existing contour coverage and
dark/light captures remain in place for all fifteen figures at desktop and fullscreen sizes.

The full UI suite passed all 759 tests, and all 15 architecture/documentation checks passed.
After refining the mutable-brush check and adding its capture, all seven shared-mark tests
passed again. The compositor-rendered mark and desktop/fullscreen loading captures were
visually inspected. Production library data and the running app were not changed.
