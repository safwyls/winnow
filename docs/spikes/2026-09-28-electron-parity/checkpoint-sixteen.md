# Desktop list sections and inline filters — 2026-09-29

Desktop now has separate manual and live-list sections with independent, session-long
collapse state and accessible vector-chevron headings. Rows use body type, projected
game counts, hover fill and a single two-pixel selection edge. A second activation of
the open row leaves its list. Home removes the active rail marker without losing the
Library cut.

Opening a live list automatically opens the inline desktop filter panel. Leaving the
list clears its contributed rules and leaves the panel open. The toolbar, cut bar and
results reserve its width. Fullscreen retains its own selector and returns to Browse
without automatically opening the filter page, matching its separate source behavior.

## Verification

- Build and typecheck pass: `.tmp/rail-build.log`.
- Focused component contracts: 122 passed, including five new section/filter cases;
  `.tmp/rail-components-second.log`.
- Full component/live-backend suite: 2,613 passed in 128 files, no skips, 44.41 seconds;
  `.tmp/rail-integration-serial.log`.
- The affected native matrix initially passed 48/50. The two failures used a removed
  desktop Close list selector and an ambiguous filter-region name. Corrected Lists and
  Details fixtures passed all 15 cases, with clean teardown; the unchanged 35 layout,
  browse-spine and lifecycle cases passed in the first run. Logs:
  `.tmp/rail-native.log` and `.tmp/rail-native-corrected.log`.
- Native tests measure toolbar/cut containment at 1200 and 1440 pixels, activate section
  headings with Enter, retain collapse state through Home, toggle an active desktop row
  off, and verify live rules across renderer reloads. Desktop/fullscreen captures in
  `.tmp/rail-native-corrected/` were inspected. The fullscreen fallback covers remain
  small in this two-row, 1440×900 fixture; this is not a general visual-parity signoff.

Running the full component suite alongside the native suite first produced two timeouts:
the Windows executable-resource fixture and the twenty-answer merge fixture. Repeating
the component suite without concurrent native tests passed unchanged. No timeout or
assertion was relaxed; `.tmp/rail-integration.log` retains the failed run. Contention is
a plausible explanation, not a measured cause.

All 34 original `ListsViewModelTests` methods now have replacement evidence. The frozen
inventory contains 913 ported, 540 retained backend, 13 framework-specific, 830 pending
and 139 partial methods. The rail section contract remains partial because its original
footer menu, placement and focus restoration are still being restored. Other features,
source tests and release cutover remain incomplete; no task acceptance criteria are checked.
