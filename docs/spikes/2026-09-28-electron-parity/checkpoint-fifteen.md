# Manual and live list workflows — 2026-09-29

Both Electron presentations now explain an empty collection using the original creation
instructions. Details also explains empty manual membership when the only saved lists
are live. Names sort alphabetically after refresh, rename and reload; the backend's
creation order no longer determines their displayed order.

Saving a live cut suggests a name from its visible rules and opens the committed list.
Navigation waits for the owning Library to receive the saved collection, avoiding a
race between the returned write and React Query publication. Returning from Home keeps
that cut. Manual-list writes preserve the source collection and selected cover elements;
Home and Details target their own game independently of the Library selection.

## Verification

| Check | Result |
|---|---|
| Build and typecheck | Passed; `.tmp/checkpoint-fifteen-build.log`. |
| Full component/live-backend suite | 2,608 passed in 128 files, no skips (55.80s); `.tmp/checkpoint-fifteen-integration-verified.log`. |
| Native teardown repetition | 30 passed across three runs of Lists, Setup and Gallery (1.2m); `.tmp/quit-single-request.log`. |
| Full native suite | 145 passed with clean worker completion (5.9m); `.tmp/checkpoint-fifteen-native.log`. |
| Frozen source inventory | 911 ported, 540 retained backend, 13 framework-specific, 831 pending and 140 partial, from 2,435 methods. |

Production HTTP cases verify original Hades/Celeste/Tunic order, duplicate addition,
movement, removal, empty creation, checked membership, rename, deletion and saved rules.
The original Hades Soundtrack fixture sets the music app type only in the explicitly
isolated database: both stored member IDs remain, while the visible library contains
only Hades. Backend reload also preserves the Aardvark/Zebra names after renaming Middle;
alphabetical ordering belongs to the frontend.

Component tests exercise both presentations with the original named games, source-list
selection, cancellation, membership ticks, bounded moves, genre-rule provenance and
reversion. Native tests use the production preload/API path and renderer reloads for
manual order, live membership growth and alphabetical names. Desktop/fullscreen captures
from `.tmp/quit-single-request/` were inspected: the active cut and count remain visible,
manual fallback titles stay inside their covers and the fullscreen selection is distinct.

## Teardown refinement

The first four-case list run completed every workflow assertion but stalled after
`quit:0`. The checkpoint-fourteen harness made a second debugger request after queued
quit to release Playwright. Cleanup now wraps only the fixture's `app.quit` entry point
to defer its native call, then lets Playwright issue and finish one close request. Normal
window closure, exit zero and the five-second deadline remain required. No production
shutdown code changed. `.tmp/lists-native.log` retains the failed run; the subsequent
30-case repetition and the full 145-case native suite completed cleanly.

## Remaining list presentation

Thirty-two of the original 34 `ListsViewModelTests` methods now have replacement evidence.
The live-save method still includes an unported automatic desktop filter-panel opening
assertion. The same-list toggle has only hook evidence while desktop uses a native select.
The next package restores desktop rail rows and automatic inline filters. The original
fullscreen `FullscreenBrowseListsPage` instead opens the list and returns to Browse;
it does not automatically push a filter page. Those paths require separate verification.

Other source contracts, final device checks and release cutover remain open. This
checkpoint does not complete the migration.
