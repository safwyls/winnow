# Theme contracts and native teardown — 2026-09-29

Electron now publishes the original art-specific alpha bytes for store marks, reading
veils, faint raised surfaces and lightbox controls. These fills stay independent of the
desktop material. Fresh Avalon profiles resolve to the complete Winnow palette; older
Studio profiles with custom colors retain them. Both the root and shell use that rule.

Independent contrast tests cover all four calibrated palettes, every whole transparency
percentage, both material reach settings and layouts. They reproduce the original
dormant cover, desktop sample, hue separation, pane/input admission limits, art contrast
and alpha rounding. Native assertions check actual store-mark, veil and lightbox fills.
Desktop and fullscreen store-mark captures were inspected for containment and legibility.

## Verification

| Check | Result |
|---|---|
| Build and typecheck | Passed; `.tmp/theme-final-build.log`. |
| Focused theme contracts | 150 passed in five files; `.tmp/theme-contracts-verified.log`. |
| Full component/live-backend suite | 2,571 passed in 128 files, no skips (46.93s); `.tmp/checkpoint-fourteen-integration.log`. |
| Repeated affected native suites | 66 passed across three repetitions (1.2m); `.tmp/quit-queued-diagnostic.log`. |
| Full native suite | 139 passed with clean worker completion (5.5m); `.tmp/checkpoint-fourteen-native-verified.log`. |
| Frozen source inventory | 880 ported, 540 retained backend, 13 framework-specific, 843 pending and 159 partial, from 2,435 methods. |

## Teardown investigation

The previous full run passed every test body but stalled in worker teardown. Debug logs
identified an old setup watchdog that killed its Windows command wrapper while leaving
Electron running. Fixture cleanup now addresses only the launched process tree and its
own scratch backend, records lifecycle events, requires exit zero within five seconds,
and reports failure even if forced cleanup succeeds.

Consolidating cleanup exposed another failure: 136 native cases passed, while three
fixtures stopped between `before-quit` and window closure. Keeping the debugger connected
while calling `app.quit()` directly still failed twice in 44 repeated cases. Queuing
`app.quit()` with `setImmediate`, waiting for normal window closure, then releasing
Playwright's debugger passed 66 repeated cases and the full 139-case run. This isolates
the successful harness change; it does not establish an Electron internals diagnosis.
No production quit behavior or timeout was changed. Earlier failing logs remain in
`.tmp/checkpoint-fourteen-native.log` and `.tmp/quit-order-diagnostic.log`.

Actual operating-system compositor substitution remains outside the renderer's knowledge;
the corresponding source contract stays pending. List work begun after the full component
run belongs to the next checkpoint. The migration and its acceptance criteria remain open.
