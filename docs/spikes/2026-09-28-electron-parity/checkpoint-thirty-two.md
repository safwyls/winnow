# Update flags and partial saves — 2026-09-29

Desktop and fullscreen Details now derive read controls from correlated unread pushes and
each release's acknowledgement. A batch captures the displayed observations before its
first request. Confirmed writes immediately update the shared details cache; a partial
refusal leaves those releases read and retries only the remaining releases. A definite
refusal neither reports success nor refreshes the library. A lost response reconciles with
the backend without claiming that nothing changed.

Mark as read and Show it again use the original labels and explanation. Patch counts use
the largest release group, so neither build/news pairs nor linked editions inflate them.
Updates and Activity preserve the original singular/plural gap captions. The current
timeline specification retains read history as neutral marks; only unread marks use Flare.
Acknowledgement keeps the selected history range. Results scroll into the reading area,
and completion restores the initiating action's focus unless the user moved elsewhere.

## Verification

- All 2,943 component and live API cases pass in 146 files without skips, in 50.88s:
  `.tmp/update-flags-full-integration-final.log`. This includes 35 new flag checks and
  corrected existing Details fixtures using valid build/news pairs and returned watermarks.
- Build and final typecheck pass: `.tmp/update-flags-build-final.log` and
  `.tmp/update-flags-typecheck.log`.
- All three native workflows pass together in 32.7s:
  `.tmp/update-flags-native-complete.log` and its copied `results.json`. They exercise actual
  desktop/fullscreen Details at 100% text and fullscreen at 140%, partial refusal, retry,
  exact captured IDs, undo, visibility and focus. Isolated fixture responses travel through
  the production main-process transport; these probes do not write real update flags.
  The ten original service contracts remain covered by the shared backend implementation.
- Final desktop and enlarged fullscreen captures were inspected in
  `.tmp/update-flags-native-complete`. An initial passing run missed a partly clipped result.
  Stronger geometry/focus assertions exposed subpixel edge placement and disabled-button
  focus loss. Result scroll margin and scoped focus recovery correct both without reducing
  those assertions. A component check separately preserves focus moved during a request.
- The preceding frozen build from `2942b280` passes all 222 native cases in 19.1 minutes,
  as recorded in checkpoint thirty-one. A combined 225-case run with this package is not
  yet claimed. The preceding full .NET Release run passes 6,863 cases with two Linux-only
  skips; this package changes no .NET code.

All twelve remaining UpdateFlag frontend methods now have named Electron evidence.
Inventory: 1,068 ported, 559 retained backend, 17 framework-specific, 683 pending and
108 partial, out of 2,435 frozen source methods. The overall migration remains incomplete.
