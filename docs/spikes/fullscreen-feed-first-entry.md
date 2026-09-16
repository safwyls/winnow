# Fullscreen feed movement on first entry

## Recording evidence — 2026-09-15

The supplied 29-second recording starts with desktop preferences, enters fullscreen with
an empty feed, then shows Recently played. Before navigation, the same covers jump upward
and return while the title, artwork and footer stay in place.

To inspect the transient, extract the fullscreen display from the 6320×2560 recording
at `(1440, 600)` with a size of 3440×1440. Inspect 15 frames per second from seconds 9–16,
scaled to 860×360. This avoids relying on a contact sheet that samples only once a second.

At 11.067 seconds the cover row starts around y=208 in the scaled crop. At 11.200 seconds
it starts around y=166, and at 11.267 seconds it returns. The approximately 42-pixel change
corresponds to 168 pixels on the recorded display. The shelf heading also moves upward and
temporarily loses its normal gap above the covers. The same pattern recurs around
12.733 seconds. These are approximate frame measurements, not runtime layout telemetry.

## Code path

`FullscreenBrowsePage` rebuilds Home when feed shelves or loading state change.
`FeedViewModel` can append supplemental shelves after the first result; a library refresh
can also replace the feed while retaining its current screen. Before the fix, `BuildHome`
recreated the shelf and viewport with unconstrained height and stretch alignment.
`SizeWall` queued a background dispatcher callback; only then did `ResizeWall` restore
explicit heights and bottom alignment. Changing the calculated capacity could queue
another complete rebuild.

The replacement layout could therefore become visible between the reset and correction.
That explains the simultaneous heading and cover movement in the recording. Home has no
scroll viewer, and its row animation starts only when the selected shelf changes; neither
is needed to trigger this failure. The recording alone does not distinguish a supplemental
shelf arrival from a complete background feed refresh.

Desktop uses separate feed shelves and ordinary scrolling. The faulty geometry correction
belongs to the fullscreen presentation, so the repair does not require changing feed data
or desktop card layout.

## Repair and verification

Home computes shelf geometry inside its measure pass, using the measured hero and heading.
The background sizing callback remains only for the separate Library grid. Capacity changes
reconfigure Home's rows without rebuilding its hero and shelf container. A true tail append
extends the viewport and updates the indicator while retaining the current covers and focus.

The regression in `FullscreenHomeLayoutTests` clears and replaces the feed three times,
collecting the viewport rectangle on every `LayoutUpdated` event. Every intermediate
rectangle must match the settled starting rectangle, including at larger text scale and
with reduced motion. A second regression appends delayed shelves and checks that the
viewport, current row, bounds and focused control stay the same before navigating to a new
shelf. These assertions catch the transient that an eventual-settling assertion misses.

Running the replacement regression against the original `FullscreenBrowsePage` failed all
three cases. In the 1920×1080 fixture, the viewport origin changed from approximately
y=633.045 to y=432.510 and its height from 398 to 634 before settling. The large-text case
also captured an intermediate y=456.302 and height 606. These test coordinates describe the
viewport, while the recording measurements above describe the visible artwork; both expose
the same stretch-then-correct sequence.

The baseline comparison used `FullscreenBrowsePage` from `e584e70` with the new regression
tests, then restored the fixed source and rebuilt. Focused Home, Browse, Stutter and Scale
coverage passed 47 tests. Headless captures at normal and 140% text size were inspected for
bottom alignment, heading spacing and visible focus. The test fixtures use isolated preview
data; the running app and production library were not used for interactions.
The full UI suite then passed all 725 tests, including desktop feed coverage, with no skips
or failures. Results: `C:/Temp/winnow-298/results/fullscreen-startup-ui.trx`.
