# Grouped-library contracts — 2026-09-29

Electron now sizes desktop cover-wall cells in whole pixels using the original column,
width and 3:2 height formulas. The virtual wall excludes the trailing gutter, shrinks by
whole rows and returns to one cell's height for one game. Fullscreen retains its separate
paged grid. Native measurements cover 1200px and 1920px windows at all three density
positions, 1,012 then 1,011 games, the last row, a single game and the empty state.

Both presentations count a manual list containing two copies of one game as one game.
Reordering uses the entry the list actually holds, preserves the revision and keeps the
list open. Home resolves either release in a linked group to its primary game; Activity
does the same for either ownership. These complete replacement evidence for all 24
original `LibraryGrainTests` methods.

## Verification

| Check | Result |
|---|---|
| Build and typecheck | Passed; `.tmp/checkpoint-thirteen-build.log`. |
| Full component/live-backend suite | 2,509 passed in 127 files, no skips (49.46s); `.tmp/checkpoint-thirteen-integration.log`. |
| Geometry and list component tests | 111 passed; `.tmp/library-grain-final-components.log`. |
| Home and Activity member navigation | 79 passed; `.tmp/grouped-member-navigation.log`. |
| Focused native wall matrix | Two passed; `.tmp/wall-geometry-native.log`. |
| Full native suite | All 139 test cases passed, followed by a worker teardown timeout. Overall command failed; `.tmp/checkpoint-thirteen-native.log`. |
| Frozen source inventory | 861 ported, 540 retained backend, 13 framework-specific, 859 pending and 162 partial, from 2,435 methods. |

The native run reached the end of every fixture, including the strict spine and Library
close assertions. Playwright then exceeded its 60-second worker teardown limit. Earlier
fixtures still contain permissive kill watchdogs, so passing test bodies do not establish
clean disposal. This remains an open verification failure; the full native run is not a
passing gate.

Desktop and fullscreen store-mark captures from `.tmp/checkpoint-thirteen-native/`
were inspected. The desktop wall, spare width, fallback titles and store marks remain
contained. The following theme package separately audits the store-mark fill opacity.

The migration remains in progress. Existing Avalonia tests and release entry points
remain until the remaining source contracts and final runtime gate are satisfied.
