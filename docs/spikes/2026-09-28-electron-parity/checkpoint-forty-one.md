# Unread update accessibility

Desktop and fullscreen cover names now include the unread patch count, with singular and
plural wording. The count comes from the backend's work aggregate and uses the maximum
across store copies. Desktop list rows expose the same information. A badge whose count
is unavailable still says what it means; an unplayed game announces no unread claim.
The Patched collection's accessible name states its game count and the meaning of its dot.

Recommendations expose the title in their button name and the reason in its ARIA
description. This gives the browser focus stop the information formerly combined into
Avalonia's automation name. Saved feedback uses a live status; no verdict is announced
before a save or after Undo. The source's correlated-push query remains in the shared
backend, with the original decoy-event regression retained.

## Verification

- Build/typecheck pass: `.tmp/unread-build.log`.
- The first complete component/API run passed 3,167 cases and found one existing assertion
  expecting the old `Patched0` name. The test now expects the restored wording. All 3,168
  cases then pass across 156 files without skips in 52.22s:
  `.tmp/unread-final-integration.log`. The earlier result is `.tmp/unread-integration.log`.
- All 33 native cases pass in 1.6 minutes: `.tmp/unread-native/results.json`. Native
  accessible-name checks cover missing counts, 1/3/1,234 updates, duplicate editions,
  acknowledged updates and games without play. Accessible descriptions are checked on
  both surfaces, as are silent/saved/undone feedback states in the production shell.
  Fullscreen controller navigation reaches the newly named collection and retains its
  saved selection, viewport and options return. All original cover geometry and pointer
  regression cases also pass.
- All eight original `UnreadAccessibleCopyTests` cases passed in the unchanged full
  Release suite at checkpoint 39; their TRX outcomes were checked again in
  `.tmp/metadata-final-regression-results`. The retained query excludes unannounced builds
  and announcements with no correlated push. No backend production code changed here.
- Inventory audit passes: `.tmp/unread-migration.log`. Seven methods gain Electron
  evidence and one is classified as retained backend. Totals are 1,154 ported, 560 retained
  backend, 17 framework-specific, 603 pending and 101 partial out of 2,435 methods.

These checks verify exposed browser accessibility properties, not a physical screen
reader. The broader migration remains incomplete. A complete native run on this frozen
source follows this checkpoint.
