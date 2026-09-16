# Desktop startup and fullscreen re-entry transitions

## Scope and source trace — 2026-09-15

TASK-300 follows the initial fullscreen presentation in TASK-299. The cached fullscreen
view previously bypassed its loading layer on subsequent entries and called RefreshAsync
while visible. That exposed row rebuilding and viewport restoration again. Desktop startup
awaited library/settings loads but did not join the primary feed execution triggered by
TilesChanged, so its initial feed could still be arriving after startup finished.

Each fullscreen entry now covers a fresh refresh before attachment and preserves the current
page beneath it. Return to desktop likewise covers its library and primary feed refresh.
Rapid re-entry shares unfinished data work, while presentation cancellation
prevents an old continuation from revealing the new entry. Desktop joins the existing feed
execution instead of launching a competing score. Both wait for layout/render opportunities
before revealing. Optional network artwork and supplemental recommendations remain independent.

The shared LoadingDragon reads the bundled vector and draws a short moving segment of the
head's outer contour. Layered strokes provide a soft glow using the current Text and Volt
brushes. Animation is tied to render frames and stops on detach, completion, failure or
reduced motion. Desktop uses the platform motion preference; fullscreen waits for its saved
preference. The visual specification owns the minimum display time and fade duration.

## Verification method

Avalonia headless tests use real fonts, templates and Skia rendering with preview data.
Controlled frame timestamps exercise transition timing and trace positions. Separate real
frame tests exercise automatic desktop OnOpened and fullscreen preparation. Tests cover
pending work, failure/retry, cancellation on close/detach, repeated fullscreen entry and
reduced motion. Tracing captures use both dark and light resource brushes; loading screen
captures include larger fullscreen text. Tests do not start the production host or use the
real library. This verifies application rendering and scheduling, not a recording of the
user's installed window compositor.

A delayed primary-feed fixture holds scoring beyond the minimum presentation duration and
verifies that automatic desktop startup remains covered. A hidden-window restore case checks
that visibility changes cannot mix renderer and stopwatch clock origins. First-run setup
retains its input isolation after the loading layer disappears.

Capture mode is scoped to the loading tests. Enabling captures for the entire suite exposed
six unrelated screenshot-helper failures: the existing shelf capture expects twelve cards
from a ten-card fixture, and four older captures dereference an unavailable rendered frame.
The normal full-suite run and focused loading captures are separate checks.

## Results — 2026-09-15

The final normal UI suite passed all 754 tests. The architecture/documentation selection
passed all 15 tests. Focused loading captures passed and were inspected for desktop loading
and retry, fullscreen loading with larger text, and trace positions on dark and light
backgrounds. The desktop return/setup test now waits for the loading presentation before
checking the restored input state.
