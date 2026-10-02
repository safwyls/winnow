# Electron parity checkpoint 37 — fullscreen Library and Application settings

Fullscreen Library and Application now use the original directional rows, switches,
section rules and two-column layout. Headings and section navigation remain visible while
the reading pane scrolls. Library tools opens from Library settings; Back returns to its
originating action. The right pane carries the original shared-settings explanation.

Default sort, journal prompts, non-game and explicit-content visibility, expansion grouping,
rating cap, startup, tray and link preferences share confirmed values with desktop. The
fullscreen rating row covers all six levels and keeps the explicit-content explanation and
hidden-title count. Failed saves preserve confirmed state and allow retry. Native startup
registration is omitted from fullscreen when unsupported; desktop retains its explanation.

Application restores diagnostics help and the active backend's log directory. The directory
lookup is independent of native version and startup information, including for a legacy
data directory. Update preferences use directional switches; download, cancel, release notes
and restart actions retain their existing lifecycle. This package does not change the native
tray lifecycle, whose remaining source contracts are recorded separately.

## Verification

- All 3,093 component/live API cases pass across 152 files without skips, in 51.60s:
  `.tmp/fullscreen-shared-settings-diagnostics-integration.log`. Twenty-two new cases cover
  shared sort and Library refresh, journal/expansion state, confirmed startup round trips,
  native registration failure, destination cycling, rejected writes, updater preferences,
  parent return focus and Windows/Unix log-directory display.
- Build/typecheck and the source inventory audit pass:
  `.tmp/fullscreen-shared-settings-diagnostics-build.log` and
  `.tmp/fullscreen-shared-settings-migration.log`.
- The first native group passes four layout cases and fails one persistence fixture whose
  immediate checkbox assertion precedes the asynchronous save. The fixture now clicks and
  waits for the confirmed state. Report: `.tmp/fullscreen-shared-settings-native/results.json`.
- The 40-case regression passes 38 cases in 5.5 minutes. Both remaining failures expose an
  older rating-help CSS rule overriding the new fullscreen typography; the selector is
  corrected. The run includes all eight Appearance/Controller cases, five new shared-settings
  cases, Epic, platform/manual, core UI, rating, setup and updater workflows. Report:
  `.tmp/fullscreen-shared-settings-regression-native/results.json`.
- All 13 native follow-up cases pass in 2.5 minutes:
  `.tmp/fullscreen-shared-settings-final-native/results.json`. These cover the corrected
  rating typography, all six rating levels and endpoints, shared settings, update lifecycle,
  and both original long-hero failures at 720p and 2160p. The Epic and platform fixtures also
  pass after explicitly selecting Platforms; all five failures from checkpoint 36 have
  passing follow-up evidence without removing their workflow assertions.
- Library/Application layout checks cover 1280×720 and the original 2560×1440 cases, each
  at 100% and 140% text. They verify one selected section, settled focus styling, one-pixel
  rules, horizontal reading bounds and full control visibility after focus. Controller A/B
  opens and returns from Library tools. Native input and desktop edits retain shared values
  through section changes, presentation changes and reload. Enlarged 720p and rating captures
  were inspected.
- All five final native settings checks pass in 1.1 minutes after the independent
  diagnostics lookup: `.tmp/fullscreen-shared-settings-diagnostics-native/results.json`.
  The isolated backend's exact log-directory path is verified through the rendered UI.
- The complete 249-case native run on `fb1640d8` finishes with 247 passes and two failures,
  no skips or flaky cases, in 25.4 minutes. Both failures are in the older Library lifecycle
  fixture: fullscreen dormancy still selects Library and a checkbox, while sort and expansion
  grouping still select desktop controls. Their traces confirm the restored fullscreen
  Appearance switch and Library adjustment rows. The full report is preserved at
  `.tmp/fullscreen-shared-settings-complete-native/results.json`; corrected navigation keeps
  the original persistence, retained-image and raw-data assertions for follow-up verification.

Four source methods gain complete evidence. The settings hierarchy method is now partial:
Library's original cases pass, while the separate IGDB and artwork-order hierarchy remains
unfinished. Inventory: 1,117 ported, 559 retained backend, 17 framework-specific, 638 pending
and 104 partial out of 2,435 methods. The broader migration criteria remain unchecked.
