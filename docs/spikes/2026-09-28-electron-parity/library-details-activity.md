# Library, details, activity and identity review checkpoint

This records the Electron migration package measured on 2026-09-28. It supplements
the shared [parity inventory](README.md), which remains incomplete.

## Implemented behavior

Desktop and fullscreen share the same revision-checked library actions. Manual lists
retain member ordering and restore the previous browse order on exit. Live lists load
their saved rules, expose named Update and Revert actions, preserve missing filter
values, and clear their contributed rules when the user leaves them. Add-to-list flows
serve library selections, individual feed cards and the open detail game. Pending
writes, conflicts and uncertain responses preserve drafts and require a saved-state
check before a potentially duplicate write.

Details retain independent editor drafts and section scroll positions. Back and Escape
return from an editor to the previous section. The unread shortcut uses the backend's
resolved-game count once, rather than summing its repeated ownership projections.
Relationship separation uses the child identity and expected link. Manual executable
browsing fills source-derived proposals while preserving user corrections; selecting
an IGDB candidate changes the draft until Save. As in the original application, a
manual executable path supplies installation and tracking evidence. It does not add
a direct executable-launch capability.

Activity provides date-grouped sessions, journal notes and ratings, filters, period
selection, a gameplay dashboard and separate Steam-reported history. Fullscreen starts
on the current week, with 50-row pages. Monthly timeline calculations retain unknown
gaps and counter resets, split tracked sessions across month boundaries, and distinguish
tracked time from approximate imported history.

Identity review now projects the original five proposal sections from the API workspace.
Users can choose each main game, exclude members, select groups across filters, merge a
batch, or accept only exact matches across stores. Confidence and explanations read the
stored matcher evidence. Expansion and variant parents remain fixed. Batch writes use
each returned revision; partial failure preserves the successfully saved acts for Undo.
Consecutive refusals accumulate in a seven-second undo dock. Group state survives
navigation and pending writes remain disabled across a remount. Same-title rows receive
distinct accessible names based on their displayed store, year, publisher and position.

The September 29 merge follow-up gives desktop rows compact playtime and idle columns,
ownership and unread details, store chips and covers from the shared artwork selection
path. The live dormancy preference updates covers without reloading the queue. Row-body
promotion is separate from Details, radio and inclusion actions. Fullscreen opens a
proposal sheet, then a member sheet; Open game and eligible Make header actions remain
separate. A confirms the focused action, B returns one layer, X opens grouping confirmation,
and Y changes group selection. Returning from Details retains the tools panel and restores
the specific row or proposal. Return intent is consumed on remount, after the departing
page's animation, so the exit cannot consume it early.

Suggestion refresh reports progress, failure, retry and truncated completion without
answering proposals. Leaving the page cancels the named request. Main binds cancellation
to its owning renderer and active lifetime; malformed or duplicate identities are refused,
completion removes the registration, and renderer destruction aborts pending requests.
Late responses cannot reload identity data or move focus on the next page.

## Measured checks

The earlier focused library/details/activity checkpoint passed 208 tests across 13
files. The identity follow-up passed 22 library tests, 19 grouped-review component tests
and 13 grouped-review model tests. Component tests exercise desktop and fullscreen;
injected DOM scroll dimensions and mocked API responses are identified as partial
evidence in the source-method mappings.

A fresh Electron build and all six `tests/electron/library-details.spec.ts` cases passed
in 24.1 seconds. The cases exercise both presentation modes against a real temporary
backend database, without mocked workspace responses:

- Search actual workspace work names, group editions, undo the returned act immediately,
  and search again after separation.
- Save a metadata year, retain an unfinished title draft, and observe the open live list
  lose the game after its year changes.
- Traverse review rows with the keyboard, merge two selected groups, verify both saved
  backend acts, undo both, and verify no proposal-card horizontal overflow.

Each native run creates a `winnow-electron-library-*` directory beneath `.tmp`, passes
`--data-dir`, and stops its own backend after the test process closes. Backend credentials
and browser profiles remain in ignored temporary data. The verified backend executable
was `.tmp/parity-full-tests/Debug/net10.0/Winnow.Backend.exe`. Fullscreen checks use the
production mode event in a fixed client window; they do not establish physical display
fullscreen or controller hardware behavior.

The September 29 follow-up passed 130 focused tests across nine merge/library/transport
files. The expanded native suite has eight cases. Six original cases passed in the combined
run; the two new long-title cases passed on the corrected build in 18.0 seconds. They check
desktop windows at 1920, 1200, 1280 and 1200 pixels, fullscreen sheets at 1920 and 1280,
native pointer and keyboard actions, simulated Gamepad API A/B/X/Y, live backend dormancy
preference changes, and Details-return focus. Native testing found and corrected a grid
track minimum that pushed trailing row controls outside the desktop card, and a return
intent consumed during the departing page's animation. Screenshots were inspected in
`.tmp/electron-rendered-results/library-details-*/merge-*.png`. The physical hardware and
display-fullscreen limits above still apply.

`tests/migration-library-details.json`, `tests/migration-library-workflows.json`,
`tests/migration-manual-flows.json`, `tests/migration-activity.json` and
`tests/migration-merges.json`, `tests/migration-merge-completeness.json` and
`tests/migration-merges-remaining.json` record the exact original methods, replacement tests and
remaining differences. `npm run migration:report` validates those references against
the frozen inventory. A green focused suite does not establish full migration parity.

## Shared backend audit

`tests/migration-backend-audited.json` records 141 additional original methods whose
assertions exercise retained production backend behavior: 97 methods across 13 complete
classes and 44 selected methods from five mixed backend/frontend classes. Each mapping
names its inspected implementation files. Application services were checked against
`BackendServiceRegistration` and `BackendStartupService`; historical `Winnow.App`
namespaces alone were not evidence of either frontend or backend ownership.

The existing full-suite result
`.tmp/parity-final-results/safwyl_ZEDSIXNINETY_2026-09-28_22_17_11_net10.0.trx`
contains all 182 expanded cases for these methods, all passed, within 4,997 passing
`Winnow.Tests` cases. This evidence-only audit did not rerun or replace those tests.
`npm run migration:report` checks the fragment against the frozen source inventory and
verifies that each retained implementation exists. The additional permitted paths in
the audit script are restricted to six inspected files in diagnostics, storefront
enrichment, GOG registry reading and launch monitoring.

Mixed tests that also assert Avalonia library/details state, account-panel state or
launch-status strips remain pending. The ten `SteamSignInServiceTests` methods exercise
the legacy service through `LegacyConnectionAliases`, not the production API handshake.
The five `SteamSignInResultRedactionTests` methods exercise the C# result used by the
Avalonia API adapter; Electron has a separate TypeScript result. Neither class was
reclassified by this audit. Retaining backend evidence does not establish browser or
renderer parity for these excluded behaviors.

## Merge contract completion and remaining inventory

The September 29 merge continuation passed nine real backend decision cases in
`MergeDecisionParityTests`, alongside the earlier three-case artwork precedence matrix.
The coordinator's integrated Electron run passed 1,925 cases across 105 files without skips;
the final added multi-pack projection assertion then passed with all 21 model tests.
`tests/electron/merge-contracts.spec.ts` passed all six native cases in 28.3 seconds against
`.tmp/merge-decision-parity/Debug/net10.0/Winnow.Backend.exe`. Both modes preserve answered
card slots through separation, keep work/release/ownership records, and change a saved
group's header storefront without changing its identity act. The preference matrix uses
a real manual ownership and resolver pass, then verifies saved preference, explicit
override and clearing across tools reentry and full renderer reloads. The native fixture
waits for loaded cards before capturing order; the initial empty sections are intentional.

Both native header-store cards were scrolled into view, measured inside the viewport with
no horizontal overflow, and captured in `.tmp/electron-merge-final-results/merge-contracts-*`.
Those screenshots were inspected. Fullscreen sort sheets return keyboard focus to their
trigger. The separate native long-member matrix also passed desktop retained-modal and
fullscreen Details-return focus after desktop began focusing the intended row before
notifying navigation.

The identity coordinator uses the existing TanStack cache key across surfaces. Named GET
requests consume cancellation signals, and a completed explicit refresh cancels older
reads before publishing its snapshot. Hidden surfaces defer invalidation work until entry.
Queue mutations register their lifetime with the existing mutation cache so application
event invalidation can wait for a batch. Presentation-only act-to-slot keys and slot order
retain card positions through answers and Undo; identity membership and revision always
come from the backend. Explicit sorting moves resolved strips behind pending cards.
Saved-group header-store controls, fullscreen sort/kind/preference sheets, empty-section
copy and total-to-shown counts are implemented on their respective surfaces.

The 89 original `MergeQueueViewModelTests` methods now map to 85 ported methods, one
retained backend method and three framework-specific counter assertions. There are no
pending or partial methods in that class. New behavioral matrices cover the five empty
pre-load sections, nonfatal stale structural refusal, triangle-edge rejection, initial
history separation, fixed expansion/saved-act preference, four Undo cycles, two exact plus
two ineligible groups, two selected groups with one untouched group, and six/eight-second
dock boundaries. Source title/note distinctions are asserted for individual, selected,
exact, excluded and dismissed answers. Refresh can be cancelled in place and retried;
exiting still cancels its named request. Real HTTP and SQLite cases verify rejection after
the actual resolver, sweep-retired candidate behavior, directional pack refusal, demo
relation labels and unchanged game/ownership records. The artwork matrix proves user art
precedes Steam, a live IGDB pin precedes Steam, and the same unpinned IGDB URL keeps Steam;
renderer tests consume those returned keys unchanged.

Three literal repository-call assertions belong to the retired in-process coordinator.
The API offers no count-only identity endpoint, so hidden pages defer loading and unchanged
entry reuses the shared cache instead of issuing the source COUNT query. Answering retains
backend compare-and-swap checks and performs one authoritative GET after each action;
it does not meet the old literal zero-read assertion. Its replacement uses the same
60-card/20-answer matrix on both renderer surfaces: all 60 DOM card instances and positions
remain, 40 proposals stay pending, 20 acts are written, and no candidate dismissals occur.
The real HTTP fixture preserves all candidate statuses and all work/release/ownership rows.

On this Windows host's Debug build, the 20 sequential actions measured POST mean 10.60 ms
and maximum 14.81 ms; GET mean 58.09 ms and maximum 69.67 ms; the largest combined action
and refresh was 80.54 ms. Evidence is in
`.tmp/merge-decision-results/merge-decision-parity.trx`. These measurements describe that
run; they are not a CI latency guarantee. The framework-specific mappings explicitly
retain the architectural difference and name the replacement behavior tests.

The complete 100-case merge renderer suite passed again after synchronizing the synthetic
answer helper with the enabled action control. A saved card can publish before the prior
mutation releases its busy state; clicking that still-disabled control had dropped the
next test action. The test still asserts every card identity, all 20 answers and the same
one-read-per-answer boundary. No production behavior or assertion was relaxed.

Library/list and Details contracts outside this package remain tracked by their exact
source IDs in the overall migration inventory. Completing the merge class does not close
those groups or establish full application parity.

### Library default order and selection, 29 September 2026

Both Library surfaces now observe changes to the saved default order even after a
temporary browsing choice. A manual list retains its order and adopts the new default
when it closes; this also works after visiting Settings while that list is open. Live-list
rules and revisions are unaffected by a sort change. Twelve cases exercise all six defaults
through the actual Settings component and named preference API, including a fresh cache
after remount. The Library workflow matrix independently checks the resulting game order.

Desktop grid and list clear a primary selection hidden by a filter and do not restore it
when the filter is cleared. A bulk selection paints only its members, survives refreshed
objects and sorting, and yields to one game on directional navigation on both surfaces.
Fullscreen continues to retain a navigation cursor on a remaining visible game. The
focused Library workflow, retained-focus and Avalon tests passed 88 cases; the preference
round-trip matrix passed 12. TypeScript compilation passed. Both native lifecycle cases
then verified that changing the default in Settings replaces a temporary browsing order
when returning to the Library.

### Expansion tile projection, 29 September 2026

The shared Library projection applies the saved expansion-grouping preference after backend
visibility and identity grouping, before search, panel filters and list membership. A pack
folds only when its base is present. The base keeps its own entries, stores, playtime, date
and bucket; a neutral +count mark names the folded packs and whether one remains unplayed.
Desktop list rows expose the same count and wording. Switching grouping off restores the
separate tiles. Library, rail counts, fullscreen Search and Settings use the same projection.
Raw API games and workspace facts remain available to Details and recommendation consumers.

The focused projection, lifecycle, filter and navigation regression set passed 156 cases
across seven files, followed by a passing 54-case workflow rerun including two additional
store-count assertions. Both native lifecycle cases passed in 14.8 seconds against the
frozen production frontend and `.tmp/parity-full-tests/Debug/net10.0/Winnow.Backend.exe`.
They create real expansion links, change preferences through Settings, verify pack-only
list exclusion, compare every raw game and ownership-bucket row, and restore the separate
tiles when grouping is turned off. The tests use accessible combobox names and wait for
the authoritative saved preference state. Cleanup waits for the launched Electron child
to exit before clearing its bounded fallback.

Desktop and fullscreen count marks were measured inside their covers and captured in
`.tmp/electron-library-lifecycle-passing/library-lifecycle-*`. The captures were inspected.
The fullscreen fixture uses the retained grid at 1440×900; its smaller cover size
is separate from the expansion mark behavior and remains part of layout review.
Refresh publication/cancellation and the remaining exact Library/filter/list assertions
stay in the migration inventory until their own replacement evidence is complete.
