# Library loading and live refresh checkpoint — 2026-09-30

TASK-381.6 closes its eighteen source-method gaps: sixteen have equivalent executable
coverage and two concern retired Avalonia mechanisms. Both presentation paths use the
same cancellable library preparation. This is a review checkpoint, not completion of
the Electron migration.

## Implementation

Library response validation now yields to input after each 128 games or lists. It checks
the query's cancellation signal between batches and keeps all results private until the
whole response is valid. Small responses retain the direct schema path. Passthrough
metadata and validation issue paths remain compatible with the previous schema.

No backend production changes were needed. A separate test executable composes the
existing backend, ownership scheduler, coordinator, refresh pipeline and event publisher.
Its authenticated fixture endpoints supply the original gated acquisition and metadata
steps. It refuses existing databases and accepts only a new test-owned directory.

The preceding keyboard correction is committed as `172482f4`: fullscreen text hints now
use the original D-pad, A, X, RT and B glyphs at the source sizes, with accessible key
names and theme colors. Its nine native cases and nineteen focused component cases pass;
[checkpoint 60](checkpoint-sixty.md) records the screenshots and checks.

## Source equivalence

`tests/migration-library-loading.json` records sixteen methods; `migration-avalon.json`
completes the two existing refresh-order entries. All source contracts remain frozen at
`cf45d9f1127243a987d3cf6e664a32fc767ecb67`.

- API fixtures retain the original grouped Steam/GOG work, 150-minute aggregate, metadata,
  list revision conflicts, remote hidden/manual entries and inline identifier error.
  Both surfaces exercise production components. A recursive import audit replaces CLR
  assembly traversal and verifies the renderer, main, preload and worker boundaries.
- The source empty-field metadata response is checked through the API client. Rendered
  editing then exposes a name field. Two refused writes preserve revision `seen` and
  draft `Draft`; Electron's conflict gate disables immediate resubmission, so the second
  attempt follows an editor remount without adopting a newer revision.
- Two independent Electron processes share one disposable backend while keeping distinct
  renderer profiles and Alpha/Beta searches. Searches are set before creation and survive
  hiding Alpha and renaming Beta to Gamma. Clearing each search exposes only Gamma.
  Both surfaces also survive a real backend restart and receive the next committed game.
- Each surface opens the original twenty-minute manual session, saves its journal note
  through the production editor and reads it back through the API. Spending completes its
  read and displays its empty state without an error.
- Six native cases preserve the two-game maturity/account/non-game visibility matrix.
  A captured old HTTP read remains gated while the stored restriction and metadata edit
  commit. Before and after its release, Details retains its DOM node and displays the
  saved summary. Closing returns to the sole valid selected game. The companion query
  test separately asserts exactly one successful publication and retained winning
  envelope, game and Details identities after the late response.
- The App selection fixture opens Details for Game 2, then independently selects Game 1
  through the production view-state store. It proves that state before publishing the
  winning snapshot, then verifies Details closes while selection remains Game 1. A click
  on Game 2 cannot prove this invariant because it changes selection too.
- Deferred-pane checks cover initial absence, one mounted pane, Merges geometry,
  Appearance state after unmount/reentry, and exact Stardew work-ID binding with no stale
  content on the next game. React retains logical state and query data rather than the
  original Avalonia view instance. Native desktop uses the source 1280×820 window;
  fullscreen uses its 1920×1080 reference.
- The 512-game fixture posts DOM input on first enumeration. Complete/cancel/dispose/
  replace cases see zero published games during input, then respectively 512/0/0/1 games.
  Successful query publications occur once or never; no partial snapshot is exposed.
- Four native ownership cases run desktop/fullscreen with and without partial failure.
  The visible feed receives New acquisition before gated metadata, then Enriched
  acquisition afterward. Both actual publication phases are recorded, the scheduler
  remains running, and later fullscreen entry sees the enriched snapshot.

Two precise framework dispositions remain distinct from migrated behavior:

- `LazyPaneTests.A_panes_visibility_follows_its_container_without_a_change_at_birth`
  tests Avalonia `IsVisible` property attachment and change notifications. React has no
  equivalent dependency-property event. Conditional mounting and return focus have
  separate executable coverage.
- `LibraryViewModelTests.The_toggle_works_without_a_settings_store` constructs a legacy
  view model without its optional repository. Electron requires API preferences and has
  no such constructor. Both native dormancy cases verify the real saved toggle, unchanged
  cover elements and persistence after reload; no silent no-store save path was added.

## Verification

Build/typecheck passes in `.tmp/task3816-final-build.log`. The fixture backend builds with
zero warnings or errors. Changed Electron files pass formatting and whitespace checks.

All **3,414 component/live API cases across 170 files** pass without skips in
**87.25 seconds** with `npm run test:integration -- --maxWorkers=4`:
`.tmp/task3816-components-bounded-final.log`. The preceding eight-worker run overlapped
native checks and passed 3,413 cases, but the existing sixty-card merge-review case
exceeded its 30-second limit. Both surface variants passed alone, then the complete suite
passed with lower concurrency and no simultaneous native run. No assertion or timeout
was changed. Earlier and focused logs are `.tmp/task3816-components-final.log` and
`.tmp/task3816-merge-check.log`.

**Nineteen distinct native cases pass**, with no final skips or retries. The combined
case list is `.tmp/task3816-native-summary.json`.

| Suite | Cases | Evidence |
|---|---:|---|
| Lazy panes and bound Details | 2 | `.tmp/task3816-live-lazy-native.log`, `.tmp/task3816-live-lazy-results/` |
| Independent clients, restart and journal/Spending | 5 | Same live/lazy batch |
| Visibility/metadata refresh order | 6 | `.tmp/task3816-order-dormancy-native.log`, `.tmp/task3816-order-dormancy-results/` |
| Persisted dormancy regression | 2 | Same order/dormancy batch |
| Scheduled acquisition and enrichment | 4 | `.tmp/task3816-ownership.log`, `.tmp/task3816-ownership-results/` |

Initial native failures exposed fixture problems: session timestamps used an ISO format
inconsistent with the database's comparison format, and the fullscreen editor assertion
expected the field page after a successful save had returned to the menu. Corrected
fixtures pass without weakening production behavior. A process-sandbox startup timeout
is preserved in `.tmp/task3816-live-stalled-results/`. Earlier run logs and failure
contexts remain alongside the final evidence.

Inspected desktop Merges, fullscreen Stardew Details, both retained-summary layouts and
the ownership agent's acquisition screenshots. Lazy-pane attachments are extracted in
`.tmp/task3816-live-lazy-results/`; native summary screenshots remain in their case
directories. No new presentation styling is introduced by this task.

## Remaining validation

The inventory has **1,329 ported, 625 retained backend, 32 framework-specific, 368 pending
and 81 partial** methods. **449 remain unresolved**, eighteen fewer than checkpoint 60.
The complete migration gate still fails as expected; its output is
`.tmp/task3816-migration-gate.log`.

The 512-game responsiveness check is a controlled component measurement through shared
production hooks, not a native large-library performance benchmark. Ownership acquisition
and enrichment data are controlled fixtures; the scheduling, persistence, HTTP/SSE and
renderer paths are real. Identity-review and journal payloads in the lazy-pane native
fixture are substituted at the main-process HTTP boundary. Physical controllers, the
complete .NET suite and packaged installers were not validated by this checkpoint.

TASK-381.6 stops for review. TASK-381.7 remains unstarted until the user prompts continuation.
