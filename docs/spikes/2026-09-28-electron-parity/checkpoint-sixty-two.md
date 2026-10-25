# Library filters, selection and lists checkpoint — 2026-09-30

TASK-381.7 closes twenty-four frozen source contracts and the fullscreen Filters visual
gap recorded at checkpoint 60. This checkpoint keeps desktop and fullscreen evidence
separate. It does not establish completion of the Electron migration.

## Implementation

Fullscreen Filters now uses the original Browse/Refine columns, readable source typography,
separate choice pages, staged result counts and fixed Apply/Clear/Cancel actions. A/B/Y
hints use the original vector assets. The Collection page includes all six explanations;
year buttons open the controller keyboard and return focus to the invoking button. Back
returns from a child page or discards the root draft. Desktop filters still apply immediately.
Selected rules remain restrictive and removable when their matching choices disappear.

Library Hide confirms the captured selection, waits for the refreshed library, then focuses
a surviving visible game without moving the viewport. Hidden games can be restored
individually. Library settings shows the explicit-content classification count and explains
missing classification evidence. Remove from Derelict is offered only in that collection.
Grouped copies retain the saved override after new services and later observations.

Membership writes now prevent a library refresh from publishing an intermediate result.
Refresh waits for queued intent and compensating writes; cancellation remains independent
for each reader. Failed list operations retain the saved model, draft, context and retry
feedback. Desktop STATS is reachable from the main rail; fullscreen keeps Library summary.
Static-list ordering/removal controls require an actual selection.
Fullscreen Library tools uses one bounded column with 24px controls and body text,
20px labels and 32px editor headings, all multiplied by the configured text scale.
This replaces the compact three-column form caught during native screenshot review.

Desktop rows display separate Steam/Epic/GOG chips in aligned store columns. Fullscreen
retains its grid initials and includes the owned-store chips in merge member sheets.

## Source equivalence

The inventory remains frozen at `cf45d9f1127243a987d3cf6e664a32fc767ecb67`.
`migration-library-filters-selection.json` records seventeen new dispositions;
`migration-avalon.json` and `migration-library-workflows.json` complete seven existing
partial entries. Twenty-three methods are ported and one retains its backend test.
Per-method mappings preserve the original fixtures, theory matrices and edge cases:

- Visibility checks retain single/bulk Hide, bucket counts, individual restore, explicit
  preference persistence and unrated games. The Derelict fixture uses every grouped copy,
  a real SQLite write failure, fresh services and later evidence. Its test-only clock is
  fixed at `2026-09-30T12:00:00Z` so the original September 1 observations stay within the
  production thirty-day freshness policy. Production classification is unchanged.
- The original 100-game viewport checks run grid/list and single/bulk selection, including
  bottom clamping and search reset. Both bulk Mark as read and Derelict removal preserve
  the unselected game. Desktop opens its pointer context menu; fullscreen uses simulated Y,
  because right-click is the fullscreen Back command.
- List failures retain Seed(4), Try next ordered `[4,1]`, saved YearFrom 2000 and the
  original SQL failure points. Membership checks retain Game 2, rapid latest intent,
  failed writes, refresh ordering and failed compensation. Six prompt routes cover
  Library/Details/Feed on both surfaces with Seed(2), Try next `[2,1]` and Second list.
  HTTP failures require checking saved lists before an uncertain retry; definite rejected
  saves separately prove immediate re-enablement. Pending prompts disable conflicting
  actions, and disposed pages cannot navigate after a late completion.
- The direct repository atomicity method remains in the shared backend. All six
  create/append/remove cases, with and without an outer transaction committed after the
  failure, pass against real temporary SQLite. Retaining this test preserves the ambient
  transaction guarantee that a renderer mock could not establish.
- All six collection descriptions retain their literal values. Xbox appears only after
  the source Steam-only snapshot changes. Missing-choice tests run account/hide/remove/
  facets on both surfaces, including a fresh renderer, saved Steam/RPG/year rules,
  zero persisted membership, enabled selected RPG with zero results, and removal of only
  that genre rule. A revision-checked unchanged API save replaces the source no-op Update
  button. The store layout check measures the original 131.1px minimum column clearance
  and all four merge rows, including the long title that must yield to the chips.

## Verification

Build/typecheck passes in `.tmp/task3817-build-final.log`. The separately built fixture
backend has zero warnings or errors. Six retained repository atomicity cases pass with
zero skips in `.tmp/task3817-list-results/list-atomic.trx`.

All **3,464 component/live API cases across 173 files** pass without skips in
**85.40 seconds** with `npm run test:integration -- --maxWorkers=4`:
`.tmp/task3817-components-final.log`. Changed Electron files pass formatting and
whitespace checks.

**Eighty-seven distinct native cases pass.** The deduplicated case list, executed attempts
and report paths are in `.tmp/task3817-native-summary.json`. Native suites run serially;
the complete component/API suite runs separately with four workers.

| Suite | Distinct cases | Evidence under `.tmp/` |
|---|---:|---|
| Fullscreen Filters, staged choices and controller year keyboard | 4 | `task3817-filters-results/` |
| List transactions, prompts, selection and STATS | 26 | `task3817-list-native-results/ledger.json` |
| Busy list-prompt controller regression | 2 | `task3817-list-native-results/busy-controller/` |
| Visibility, Derelict, bulk selection and viewport retention | 20 | `task3817-visibility-results/` |
| Collection explanations, store chips, Xbox and missing options | 14 | `task3817-labels-ledger.json` |
| Fullscreen Library tools type, overflow and focus at two sizes | 1 | `task3817-tools-results/` |
| Controller keyboard, browse, menu and filter-route regressions | 20 | `task3817-browse-keyboard-initial-results/`, `task3817-navigation-results/` |

The Library tools case checks 1920×1080 at normal text and 1280×720 at 140% text.
Controls/error text measure 24/33.6px, labels 20/28px and headings 32/44.8px before the
shared interface zoom. All fifteen enabled buttons are reachable by simulated D-pad
at each size, and focused destinations remain inside the clipped viewport. Desktop font
measurements remain unchanged. The independent fullscreen failed-filter regression also
passes after the styling change; it is counted once among the twenty-six list cases.

Inspected the normal and enlarged Filters screens, keyboard glyphs, settled Hide panel,
desktop STATS, list prompts, store chips, collection descriptions and both Library tools
screenshots. The extra settled Hide capture repeats one passing case and is not counted
again. Its evidence is `task3817-visibility-visual-results/`.

Earlier failed attempts remain beside the final evidence. Fixture repairs supply the
custom main process with the correct backend path, scope the retained list-name field,
use fullscreen Y instead of the global right-click Back command, wait for the authoritative
Xbox snapshot, and reach fullscreen rule chips through More. Library tools fixtures enter
the independent fullscreen Library page and establish the failed-save state at each size.
Native measurement exposed an actual narrow-window heading override, corrected by the
tools-specific selector. The old deep-browse border assertion now compares the original
3px border with a reference rendered under the same 85% zoom, preserving the requirement
while accounting for Chromium pixel snapping. No viewport, selected-column or focus
assertion was removed. The final deep-browse case passes sixty rows into nine hundred games.

The inventory now has **1,352 ported, 626 retained backend, 32 framework-specific,
351 pending and 74 partial** methods. **425 remain unresolved**, twenty-four fewer than
checkpoint 61. The report validates; the complete migration gate still fails as expected
because those remaining methods belong to subsequent checkpoints. Logs are
`.tmp/task3817-migration-report.log` and `.tmp/task3817-migration-gate.log`.

## Remaining validation

Controller checks use a simulated standard Gamepad API. Physical controllers, the complete
.NET suite and packaged installers are outside this checkpoint. Data directories are
disposable; no user library or launcher files are modified. Controlled main-process
responses supply the merge-review and rich filter presentation fixtures; renderer routes,
preload, application behavior and native layout remain real.

Store-chip screenshots also exposed existing compact text in the fullscreen merge member
sheet. TASK-381.12 records that typography gap alongside its merge-review work. This
checkpoint verifies the added store labels and their clearance, not complete merge-sheet
visual parity.

TASK-381.7 stops for review. TASK-381.8 remains unstarted until the user prompts continuation.
