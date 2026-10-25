# Fullscreen Browse collections and drafts — 2026-09-29

Fullscreen Library now cycles through the source's four collection shortcuts:
All games, Installed, Never played and Patched. Choosing a collection clears the
fullscreen search and filter cut while retaining its sort. Each collection and saved
list keeps its own selected cover and two-row position. Returning to a collection,
resizing the window and returning from Home preserve selection and focus. Desktop
keeps its separate query, sorting and collection-toggle behavior.

Fullscreen Filter & sort keeps collection, installation and sorting edits in a draft.
Apply commits them together; Cancel and controller Back discard them. Clear filters
resets the draft collection and filters while retaining its sort. Controller Y applies
from the top of the panel, and the fixed actions stay visible at 1280×720 with 140%
text. The fullscreen panel no longer inherits the generic dialog's 700-pixel limit.

## Verification

- Build and typecheck pass: `.tmp/fullscreen-browse-build-final.log` and
  `.tmp/fullscreen-browse-typecheck.log`.
- Focused component checks: 133 passed in three files;
  `.tmp/fullscreen-browse-components.log`.
- Complete component/live-backend suite: 2,627 passed in 130 files, no skips,
  46.75 seconds; `.tmp/fullscreen-browse-integration-final.log`.
- Four native cases cover the original 60-game resize fixture, trigger cycling,
  queued invalidations, return focus, filter draft input, ultrawide fitting and text
  scale. Test-owned 600×900 PNGs pass through production image IPC and decode with
  uncropped portrait fitting. Five consecutive repetitions passed all 20 cases with
  clean completion in 41.4 seconds; `.tmp/fullscreen-browse-native-repeat.log`.
- Complete native repeat: 152 passed with clean worker completion in 6.1 minutes;
  `.tmp/fullscreen-browse-native-verified.log`.

The first full native run passed 151 of 152 cases. Its resize test observed columns
before asynchronous layout completed and changed the DOM fitting flag outside the
preference owner. The corrected fixture writes the actual presentation preference,
waits for its publication and polls the resulting column count. The expected geometry
and focus assertions are unchanged. The failed run remains in
`.tmp/fullscreen-browse-native-full.log`.

Earlier focused attempts exposed fixture startup without the sample setup state,
an exact label selector that included select-option text, and tests applying desktop
collection assumptions to fullscreen. Fixtures and selectors now follow the actual
separate surfaces; desktop expectations are retained.

The 1728-pixel cover, 1280×720 filter and ultrawide large-text captures were inspected.
Portrait fitting and filter actions pass; Browse still allocates too much vertical
space to desktop-style controls. Its compact source heading, options/list paths and
selection backdrop remain separate work. This checkpoint does not claim complete
fullscreen visual parity.

Nine more source methods have replacement evidence. The frozen inventory contains
923 ported, 540 retained backend, 13 framework-specific, 821 pending and 138 partial
methods. Task acceptance criteria remain unchecked.
