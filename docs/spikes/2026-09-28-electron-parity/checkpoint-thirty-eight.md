# Electron parity checkpoint 38 — tray and window lifecycle

Electron creates its native tray icon only for an enabled minimize/close preference,
a background launch, or a window hidden in the tray. Disabling both preferences while
hidden keeps a recovery route; restoring removes that temporary icon. The original
Open Winnow, separator and Exit menu is restored. Failed tray setup leaves the window
visible and ordinary close available.

Tray actions, secondary activation and journal notifications share one restoration path.
Only minimized windows call Electron's restore method, preserving normal, maximized and
fullscreen presentation. Background launches remain hidden and suppress fullscreen startup,
including an early restoration before the initial preference read. Ordinary fullscreen
startup applies once. Legacy capitalized boolean preferences remain supported.

Confirmed presentation preference writes refresh native settings before returning to the
renderer. This closes the interval where a saved close-to-tray change could be followed by
a close event still using the previous value. Native icons are disposed before asynchronous
updater shutdown drains, and later notifications cannot recreate them during quit.

## Verification

- Build/typecheck passes, and all 79 focused tray, updater and activation unit cases pass.
  The tray controller adds 18 cases covering absence, boolean compatibility, conditional
  lifetime, hidden recovery, one-time startup, unavailable tray and asynchronous shutdown.
  Logs: `.tmp/tray-focused.log`, `.tmp/tray-build.log`, `.tmp/tray-confirmed-build.log`.
- The first 12-case native run passes eight cases and fails four in 3.5 minutes. One window
  exits during a maximized close before its saved tray preference is reflected natively.
  The other three reach native will-quit with their icons destroyed and windows closed,
  but the fixture waits for process exit while Playwright still holds the inspector.
  The original report remains at `.tmp/tray-native/results.json`. The follow-up adds a
  controlled held preference read and releases the inspector after independently recording
  will-quit, before asserting exit code zero.
- The second native run passes all ten functional workflows, including the held preference
  read and all three presentation states. Its three exit cases still time out because
  invoking Playwright close after will-quit does not detach the already-closing connection;
  three worker teardown errors accompany that 7.8-minute run. Report:
  `.tmp/tray-confirmed-native/results.json`. The fixture now calls Node's inspector close
  only from the application's quit event. All three exit cases then pass in 27.4s,
  independently verifying the will-quit snapshot and process exit zero:
  `.tmp/tray-quit-native/results.json`.
- All 3,111 component/live API cases pass across 153 files without skips in 51.99s:
  `.tmp/tray-integration.log`. Final build/typecheck and the inventory audit pass:
  `.tmp/tray-final-build.log` and `.tmp/tray-migration.log`.
- All 44 native regression cases pass together in 6.5 minutes, including the full 13-case
  tray matrix, both corrected Library workflows, startup, activation, journal, shared
  Settings and updater behavior: `.tmp/tray-regression-native/results.json`.
- The preceding complete 249-case run on `fb1640d8` passes 247 cases, with two obsolete
  fullscreen selectors in the Library lifecycle fixture. The fixture now navigates to the
  Appearance switch and Library directional rows, retaining its original persistence,
  image identity, grouping and raw-library assertions. See checkpoint 37 for the report.

Seven source methods now have complete evidence. Inventory: 1,124 ported, 559 retained
backend, 17 framework-specific, 633 pending and 102 partial out of 2,435 methods. The
complete 262-case native suite on `d5f2994d` passes 259 cases, with three failures and no
skips or flaky results, in 27.6 minutes. Report: `.tmp/tray-complete-native/results.json`.
Two fixtures stop waiting for initial readiness at 15 seconds while the frontend remains
in preparation; the application permits 45 seconds for first attachment. The fullscreen
Details case at 2560×1440 reports a title height consistent with the preceding case's 1.2
interface scale: 168.94px instead of the expected two 70.4px lines. Its fixture waits for text scale but not
interface scale before measuring. Follow-up checks must verify both applied values and
retain the original title-height assertion. None of these failures came from the 13 tray
workflows, which all passed. The broader migration
acceptance criteria remain unchecked; the subsequent metadata hierarchy and sync work
is separate from this verified package.
