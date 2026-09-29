# Browse position and grouped-library checkpoint — 2026-09-29

Desktop grid and list now include the original 27-stop alphabet spine, including
diacritic folding, unavailable-letter scrubbing, descending order, viewport halo and
fractional pointer wave. Other sorts use proportional notches. The separate scrollbar
and cover art retain their own space. The spine spans the controls below the toolbar
so short windows retain readable letters. Fullscreen keeps its independent paged grid.
Closing desktop Details restores the exact underlying Library scroll offset, including
when that offset changes while the dialog is open.

The intermittent merge-header failure from checkpoint ten was reproduced with a delayed
initial preferences response. Initial hydration now preserves an explicitly selected
header; an explicit preference change can still update the cards. Controlled regressions
cover both surfaces, and four repetitions of the six native merge cases passed.

Grouped games now have one case-insensitive chip and initial per store, retaining primary
entry order and every licence. Their accessible names include store words. Desktop fades
initials into the hover/focus chip overlay; fullscreen retains them because it does not
show that caption. Rendered inspection found and corrected an overlap with fallback
titles. Cover totals now use the same compact formatting as Details and the list view,
including the original 390-minute group's `6h` headline.

Seven new production HTTP tests exercise original unlinked/linked library fixtures:
member identities, minute sums, latest dates, sum-based buckets, repeated licences and
updates on a secondary release. Composed App tests cover selection and boundary clamping,
Details totals and store names, both per-copy controls, Epic-only update rows, and Play
dispatch to the installed Epic copy when Steam is the primary title.

## Verification

| Check | Result |
|---|---|
| Electron build and TypeScript | Passed; `.tmp/ownership-build-final.log`. |
| Final full component/live-backend suite | All 2,490 cases in 127 files passed, no skips; `.tmp/checkpoint-twelve-verified-integration.log` (43.12s). |
| Composed App suite | All 39 cases passed; `.tmp/grouped-app-full.log`. |
| Backend grouping replacements | Seven passed with no skips; `.tmp/library-grain-api.log` and matching TRX directory. |
| Native merge repetition | 24 passed; `.tmp/merge-hydration-fixed.log`. |
| New grouped-store native matrix | Four passed in both motion modes, then passed in the full native run. Screenshot geometry covers initial containment and fallback-title separation. |
| Full native suite before the compact-total adjustment | 136 passed, one row-reversal test failed; `.tmp/checkpoint-eleven-twelve-native.log`. The new fixture had left reduced motion enabled. |
| Final-build layout/Details/Library/spine rerun | 41 passed, one spine teardown timeout; `.tmp/checkpoint-twelve-final-native.log`. All 20 layout, nine Details and eight Library cases passed after explicit motion reset. |
| Spine repetition with shutdown diagnostics | All 25 cases passed across five repetitions; `.tmp/spine-shutdown-diagnostics.log`. |
| Frozen source inventory | 856 ported, 540 retained backend, 13 framework-specific, 863 pending, 163 partial, from 2,435 methods. |

The earlier 133-case native run reported two temporary SQLite lock failures, a theme
save/reload race in the fixture, and a spine teardown timeout. The SQLite fixture
connections now wait for brief locks; the theme fixture reads back the stored preference
before reloading. All 45 cases in a threefold repeat of those four files passed. These
changes retain the original data and presentation assertions.

The spine timeout remains intermittent. Quit-event and child-exit diagnostics now
distinguish an Electron exit failure from Playwright close bookkeeping; five successful
repetitions do not establish its cause. The fixture still fails when graceful close
exceeds its deadline and requires exit code zero. The full native run is not described
as a complete pass. Some older native fixtures still use permissive teardown watchdogs;
these runs do not establish clean process exit for every fixture.

The latest full component run initially passed 2,489 of 2,490 cases. Its one failure
asserted rendered text before React Query's batched observer notification. The corrected
test retains the immediate fresh-cache and second-read assertions, waits for the displayed
publication, then verifies a late old response cannot replace it.
The complete corrected repeat passed all 2,490 cases without skips.

The coordinator inspected desktop and fullscreen screenshots in
`.tmp/checkpoint-eleven-twelve-native/avalon-layout-*/` and the spine screenshots in
`.tmp/browse-layout-final/`. Missing artwork in the store-mark matrix is intentional
fixture data and makes fallback-title overlap observable.

The full migration remains incomplete. Grouped-member navigation, exact desktop wall
geometry and secondary-release manual-list contracts still need further evidence.
The original frontend/tests and release entry points remain in place, and the complete
migration gate continues to fail for pending and partial contracts.
