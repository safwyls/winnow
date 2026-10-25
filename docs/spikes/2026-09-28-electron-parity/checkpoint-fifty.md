# Saved link destinations and reachable fallback notices

Both native presentations now exercise the saved reading destination from Library-created
Details. The original IGDB, SteamDB and SteamGridDB targets open in the private reader;
browser and Steam-client preferences use their intended destinations. Desktop and controller
settings share persisted values through a renderer reload.

Native interaction exposed three notice defects. A notice behind a modal scrim could not
be dismissed by pointer. Reopening Details could try to show a popover in the previous,
detached dialog. Escape reached the dialog's capture handler before the notice, dismissing
More instead of the notice. Notices now render in the top layer inside the active dialog's
focus boundary, wait for a connected host, and consume Escape before dialog capture when
focus is inside the notice. Dismissal restores the originating action. Closing an owning
dialog keeps its notice reachable.

## Verification

- Build and TypeScript check pass: `.tmp/link-destinations-build-final.log`.
- All 3,249 component/live API cases pass across 158 files without skips, in 53.65s:
  `.tmp/link-destinations-integration.log`.
- The focused settings/notice group passed 40 cases; the final capture-handler change
  passes all 16 notice cases and the complete integration run above.
- All eight final native destination cases pass in 33.9s:
  `.tmp/link-destinations-native-final-pass.log` and
  `.tmp/link-destinations-native-final/results.json`.
- The preceding native group passed all six action-panel and both isolated-reader cases,
  plus six destination cases. Its two remaining Escape failures are fixed by the final
  run. Evidence: `.tmp/link-destinations-native-verified.log` and
  `.tmp/link-destinations-native-third/results.json`.
- The action-panel fixture now waits for the confirmed motion preference to reach the
  document before measuring animation. Its original duration, geometry and typography
  assertions are unchanged.
- Native captures were inspected on desktop and actual fullscreen. Network responses
  and OS URI dispatch are fixture-owned; backend persistence and production routing are
  real. The fixture asserts zero game launches, unexpected network requests and renderer
  errors.

Five original methods gain complete evidence. The inventory is 1,222 ported, 625 retained
backend, 23 framework-specific, 464 pending and 101 partial methods out of 2,435. This is
test accounting, not a percentage of product completion. No .NET source changes here;
the retained Release evidence is still checkpoint 39. Complete native regression and
Electron release/CI cutover remain open.
