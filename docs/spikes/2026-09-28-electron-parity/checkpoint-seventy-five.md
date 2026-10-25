# Recommendation previews checkpoint — 2026-10-01

TASK-381.20 covers ten frozen source contracts. It is the tenth and final task in
the authorized sequential batch through TASK-381.20.

## Source boundaries

Desktop feed and library previews share their layout and lifecycle. A preview opens
on hover, closes on exit, rebinding or disposal, and cancels unfinished metadata.
A cancelled request that later succeeds cannot restore the old preview. Artwork
belongs to the open preview independently of the underlying portrait cover.

The bubble's fill, hero artwork and border share one rounded outline. The pointer
is 10px wide and 18px high; the corner radius is 6px. Content has 16px padding plus
the pointer's 10px gutter on its side. Flipping the pointer moves that gutter while
preserving the content width. Hero artwork has 18% opacity and cannot spill outside
the shape or leave a border seam across the pointer.

The source title is 22px bold. Store/play metadata uses subdued 13px text; ratings
use subdued 12px body text in one compact line. The three-population fixture has
Steam 93/1,500 with the published Very Positive label, IGDB critics 88/12 and IGDB
users 81/100. Visible text identifies each source but omits counts and Steam's
percentage. The accessible description retains the complete figures. Despite its
method name, the original rating test explicitly rejects visible counts and percent
signs. Missing ratings occupy no space, and delayed ratings must move a growing
preview back inside the window's 8px inset.

Fullscreen shows the selected game's hero and complete shelf. It does not add a
pointer-hover preview to that presentation path. Desktop preserves five cover slots,
the original shelf titles, pitches and reasons, and overflow at narrow widths.

The original shelf capture method returns without executing its assertions unless
`WINNOW_UI_CAPTURE_DIR` is set. Its 1600px and 900px cases require capture-enabled
execution; an ordinary passing run alone does not establish visual coverage.

Enabling that branch exposed a dormant assertion defect in the frozen test. Both
widths expected twelve rendered cards, but the fixture creates exactly two shelves
of five. `FeedViewModel` starts with an empty shelf collection and does not load in
its constructor, so there are no additional initial cards. These files match the
frozen revision. The unchanged original run records eleven passes and two failures
in `.tmp/task38120-source-tests.log`. The working source test now expects ten;
every fixture input, width and preview assertion remains unchanged.

The corrected capture-enabled run passes all thirteen cases
(`.tmp/task38120-source-corrected-tests.log`, TRX
`.tmp/task38120-dotnet-results/recommendation-preview-source-corrected.trx`). The
four captures in `.tmp/task38120-source-captures/` execute the source's optional
missing-artwork branch. Both quick-details captures were inspected: at 1600px the
Hades preview sits to the right, and at 900px it flips left while the shelf overflows
horizontally. No extra cards were added to obtain the expected count.

## Implementation

The production `AvalonPreviewBubble` uses one SVG path for the surface, clipped
artwork and border. This replaces the separate rotated-square pointer that could
not carry the artwork. Pointer-side padding, corner geometry, measured placement
and typography match the source. The surface uses the theme's raised color, and
compact ratings reuse the same reception formatting as Details. Their accessible
name preserves source populations and counts without adding visible detail or
another tooltip. Store/play metadata retains the source's `never opened` and idle
copy, and recommendation reasons remain below their covers.

Fullscreen covers retain their selected-game hero and action panel and no longer
open the desktop preview. Desktop previews keep their existing close, cancellation
and same-identity rebinding boundaries.

## Verification

The complete Electron component/live API gate passes **3,859 cases across 190
files** in 105.37 seconds (`.tmp/task38120-components.log`). The production build
passes (`.tmp/task38120-build.log`), producing `index-jNzwlGYY.js`.

The focused renderer gate passes 55 cases across four files
(`.tmp/task38120-renderer-focused.log`). The two new artwork lifetime cases retain
the source's 440×190 target, Steam hero 123, eight-pixel PNG and separate stale/current
metadata completions. Closing cancels and releases the consumer; ignored late bytes
cannot enter the image cache. Reattachment uses a fresh request, and stale metadata
cannot acquire `staleart` after the current request selects `currentart`. Electron's
shared scrollback cache may retain decoded pixels after the last visual consumer
leaves; closing the cache releases the object URL. The image decoder is a jsdom
platform double, while the production Artwork component and ownership cache run
unchanged. The native checks supply browser painting evidence separately.

The twelve focused preview cases also pass after a final test-only refinement to the
literal source Hades title (`.tmp/task38120-preview-final.log`). This does not change
the complete suite's test count or production bundle.

Nine authenticated HTTP cases pass. They preserve each original rating population,
the ten shelf tuples, source titles/pitches/reasons, unloaded candidate count and
default confidence. A test-only middleware delays immutable bytes after the real
endpoint releases its repository scope. It records hashes, cancellation and late
release without rewriting the response or retaining a SQLite transaction during the
delay. Source-style late completions therefore coexist with normal library writes.

Two additional source capture executions pass with supplied artwork
(`.tmp/task38120-source-artwork-tests.log`). The four images in
`.tmp/task38120-source-artwork-captures/` use the same deterministic portrait and
hero assets supplied to the native fixture's real cover pipeline. The wide source
quick-details image was inspected. These two executions repeat the original visual
cases; they are not additional distinct source contracts.

All **fifteen new native cases** and **thirteen existing feed-card preview
regressions** pass. The initial run passed thirteen of the new cases. Two harness
assumptions were corrected and rechecked: a full shell's side rail changes the room
beside Hades at 1600px, and the 1280px fullscreen shelf pages its five games through
four visible slots. Tests now verify measured placement and controller access to
the fifth game. Neither correction changes production behavior. The final two
cases pass in 13.0 seconds, and the existing consumers pass in 7.3 seconds.
The run logs are `.tmp/task38120-native-initial.log`,
`.tmp/task38120-native-final.log` and `.tmp/task38120-consumers.log`.
`.tmp/task38120-native-evidence.json` identifies each case's successful run and
links its full HTTP, image-hash, cancellation and operating-system dispatch ledger.

The native pixel probe renders the exported production bubble at the source's
200×100 size. It verifies alpha outside the shape, equal red pixels across the
pointer and body, and the flipped 26px/16px content origins with identical content
width. The real-app tests use authenticated metadata and artwork reads. For the
source's 900×400 ratings cases, the harness hosts the production card at its original
220px width and (20,20) or (20,340) position with interface scale 100%; these are
isolated component-hosting inputs, not normal shell-layout screenshots.

Whole-shell captures separately cover 900px/1600px desktop and 1280px/1920px
fullscreen. Both final desktop previews, compact ratings and both fullscreen hero
captures were inspected. Fullscreen retains the main-menu bumper hints and its
local navigation hints. Controller input is simulated; physical hardware and Steam
Deck remain unverified.

The source screenshot fixture supplies `CoverArt(bitmap, bitmap)`, making its
dormancy floor identical to the vivid image. The native fixture uses the real cover
pipeline and therefore desaturates unselected never-played covers. Their timestamps
and production dormancy policy were not changed to imitate this test double. Both
preview heroes use vivid artwork. These captures establish layout and interaction
evidence, not an identical whole-window pixel image across frameworks.

All ten assigned methods are ported. The inventory records **1,515 ported**,
650 retained-backend and 32 framework-specific methods, with **208 pending and
30 partial methods** remaining. The whole-port migration gate is still incomplete.
This checkpoint ends the authorized ten-task batch; work stops here for review.
