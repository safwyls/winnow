# Loading dragon contour coverage

## Source and verification — 2026-09-15

TASK-301 removes the single-head-contour selection from LoadingDragon. The thirteen paths
in the bundled dragon SVG contain fifteen closed figures. Each figure now has its own
perimeter and moving trail, including the detached pieces and the head's inner details.
All trails complete a circuit in 1.8 seconds. Splitting a trail across the starting point
keeps its length continuous when a new circuit begins.

The geometry regression checks all fifteen closed figures at 101 positions around their
perimeters. It checks the leading endpoint against the source contour and the combined
trail length across the seam. Extracted-curve length allows one source unit of approximation,
less than a fifth of a pixel at the desktop and fullscreen mark sizes.

Eighteen focused loading tests passed, covering geometry, frame scheduling, theme brushes,
reduced motion, cancellation and fullscreen readiness. Nine frames per cycle were rendered
at both 88px desktop and 100px fullscreen sizes with dark and light brushes. Inspection
confirmed that highlights reach detached pieces and inner details without clipping.

The user's local five-second desktop preview delay was preserved. The desktop timing suite
was not rerun against that intentional override; the shared mark was rendered at both sizes.
