# Fullscreen Library selection backdrop — 2026-09-29

Fullscreen Library now uses the shared ranked-artwork renderer behind the full canvas.
The selected cover changes its work without replacing the backdrop node. The enclosing
layer has the original 45% opacity. Ready pixels remain visible while replacement art
loads; leaving Browse releases decoded images and cancels pending image work. Desktop
Library retains its existing presentation. Home and Details retain separate ownership.

## Verification

- Build and typecheck pass: `.tmp/library-backdrop-build.log`.
- All 2,627 component/live-backend cases pass in 130 files, no skips, 45.95 seconds;
  `.tmp/library-backdrop-integration.log`.
- Nine native Home, Details, Library and Browse cases pass with clean completion in
  16 seconds; `.tmp/library-backdrop-native.log`. The new case asserts the same attached
  node, selected-work metadata request, exact parent opacity, retained loading pixels,
  decoded replacement, released object URLs and cancelled pending request.
- The 1920×1080 Library backdrop capture was inspected. It retains readable controls
  and the correct dimmed art across the canvas. The compact Browse controls remain
  unfinished and are not covered by this visual result.

The previous complete 152-case native run passed at checkpoint eighteen. This small
backdrop change reran its nine affected native cases; a complete native rerun is still
required after the next composition changes. No backend code changed. Existing real
HTTP candidate tests preserve repository IGDB screenshots, and ranked-renderer tests
verify their selection before portrait fallback.

Two additional source methods have replacement evidence. The frozen inventory contains
925 ported, 540 retained backend, 13 framework-specific, 819 pending and 138 partial
methods. Task acceptance criteria remain unchecked; plan 33 continues with the compact
Library layout and options/list paths.
