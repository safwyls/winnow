# Electron parity verification — 2026-09-28

Avalon now reproduces the original Winnow presentation in the Electron frontend, with
separate desktop and fullscreen paths. This evidence records the migration in progress;
it does not establish complete behavior or test parity. The Avalonia frontend and its
tests remain available while equivalent Electron coverage is added.

## Original contract inventory

`source-contracts.json` freezes 2,435 test methods from 299 files at
`cf45d9f1127243a987d3cf6e664a32fc767ecb67`. Collection includes every test in
`tests/Winnow.Ui.Tests`, recursively, and main test-project files mentioning `Winnow.App`,
`Winnow.Auth.WebView`, `Winnow.Core.Auth`, `Winnow.Presentation`, `Winnow.Covers.Avalonia`,
or importing Avalonia. The account sign-in filter adds 125 methods from nine files that
the initial collection missed; all 2,310 initially collected methods remain at the same
revision and source paths. Roslyn identifies attributed methods and their containing class,
including files with multiple test classes. Theory rows count as one source method.
This is a source-based inventory, not a count of executed test cases or a proof that
every product requirement has a test.

Reproduce the original inventory with PowerShell 7 from the repository root:

```powershell
./src/Winnow.Electron/scripts/collect-migration-contracts.ps1 `
  -Revision cf45d9f1127243a987d3cf6e664a32fc767ecb67 `
  -OutputFile .tmp/electron-original-contracts.json
```

`tests/migration-*.json` supplies evidence for individual methods. The generated
`test-inventory.json` preserves each original source path and records one of:

| Status | Meaning |
|---|---|
| `ported` | An identified replacement test exercises the original behavior through Electron or its shared application/backend boundary. |
| `partial` | Tests cover part of the contract; the reason names the remaining gap. |
| `pending` | No complete migration evidence has been recorded. |
| `retained-backend` | The original test exercises an implementation retained in the shared backend, with implementation paths recorded. |
| `framework-specific` | The assertion concerns an Avalonia mechanism that has no Electron equivalent, with a reason recorded. |

From `src/Winnow.Electron`, `npm run migration:report` validates and regenerates the
report. `npm run test:migration` additionally fails if any method is pending or partial.
The gate currently fails. Deleting original tests cannot make it pass: the frozen
inventory is independent of the current source tree. Its evidence checks locate test
names; executing those tests and reviewing the assertion scope remain separate checks.

## Measured checks

The [fullscreen sizing checkpoint](checkpoint-fifty-nine.md) records all 3,369 component/live
API cases passing. It restores the 85% baseline, display scaling, artwork edge padding,
source shelf targets, Activity reading hierarchy and full-canvas decorative backdrops.
All 19 assigned source methods gain complete replacement evidence; 391 pending and 86 partial
methods remain. Native pixel, scale, reading, row-motion and regression evidence is recorded
in the checkpoint. TASK-381.4 stops for review before TASK-381.5.

The [fullscreen lifecycle checkpoint](checkpoint-fifty-eight.md) records all 3,358 component/live
API cases and 87 distinct native lifecycle, navigation and affected-page cases passing. Startup entry,
cursor ownership, stable section hints and return focus preserve the separate presentation paths.
All 17 assigned source methods gain complete replacement evidence; 403 pending and 93 partial
methods remain. Physical-device validation and the complete migration gate are still open.
TASK-381.3 ends at the next user review boundary before TASK-381.4 can begin.

The [fullscreen status checkpoint](checkpoint-fifty-seven.md) records all 3,351 component/live
API cases and 23 native controller/status/Details cases passing. It restores the local clock,
connection and known Windows battery labels, centered navigation and external desktop restore.
Three source methods gain complete evidence; 417 pending and 96 partial methods remain.
Physical-device validation is still open. TASK-381.2 is the first implementation checkpoint
in the user-reviewed Backlog queue; each subsequent task waits for a continuation prompt.

The [Details structure checkpoint](checkpoint-fifty-six.md) records all 3,324 component/live
API cases and 38 distinct native Details/gallery cases passing. Desktop relationships follow
their source sections; rendered text, scrollbar clearance and inline screenshot/menu behavior
are checked. Seven structure contracts gain evidence, and seven already retired chart
contracts receive a documented disposition. There are 420 pending and 96 partial methods.

The [populated desktop Details checkpoint](checkpoint-fifty-five.md) records all 3,322
component/live API cases and 43 native Details/controller cases passing. Desktop sections
retain expanded relationships and exact scroll positions, and long titles and publishers
wrap with the source typography. Two source methods gain complete evidence;
434 pending and 96 partial methods remain.

The [fullscreen Details checkpoint](checkpoint-fifty-four.md) records all 3,322 component/live
API cases and 45 native Details/artwork/gallery cases passing. It closes the original rich/empty
section and cinematic-artwork matrices, including controller membership focus, full-window
backgrounds, complete screenshot edges and adaptive reading height. Two source methods gain
complete evidence; 434 pending and 98 partial methods remain.

The [Details interaction checkpoint](checkpoint-fifty-three.md) records all 3,322 component/live
API cases and 19 native Details/controller cases passing. History range input, child-end
confirmation and slow-launch focus restoration are covered alongside achievements, refreshed
drafts, long titles and pending prompts. Six source methods gain complete evidence;
434 pending and 100 partial methods remain.

The [launch checkpoint](checkpoint-fifty-two.md) records all 3,321 component/live API
cases, 34 native launch/Details/controller cases and two new backend HTTP cases passing.
Shared watcher-driven launch feedback and fullscreen version selection preserve ownership
attribution and the original notice lifetimes. Eight source methods gain complete evidence;
439 pending and 101 partial methods remain. The completion gate and release cutover remain open.

The [controller checkpoint](checkpoint-fifty-one.md) records all 3,296 component/live API
cases and 13 native controller, Details and merge cases passing. Desktop controller input,
original input filtering and fullscreen Details focus rows are present. The complete prior
native run passed 347 cases and found one lost merge-cover click; the controlled reproduction
and fix pass in the final focused run. Seventeen source methods gain complete evidence;
447 pending and 101 partial methods remain. The complete migration and release cutover are open.

The [saved destination checkpoint](checkpoint-fifty.md) records all 3,249 component/live
API cases and all eight final native destination cases passing. Library-created Details,
desktop/controller preferences and browser fallback use the production composition.
Notices remain reachable above dialog scrims, survive dialog replacement and consume
Escape without closing their owner. Five source methods gain complete evidence; 464 pending
and 101 partial methods remain. These inventory counts do not estimate product completion.

The [link and capture checkpoint](checkpoint-forty-nine.md) records all 3,241 component/live
API cases and seven final native browser/account cases passing. The affected Details matrix
also passes. Shared link validation preserves HTTP destinations and omits invalid buttons;
Steam scripts contain page failures and pagination requests fresh documents in the same
private session. Eighteen source methods gain complete evidence; 469 pending and 101 partial
methods remain.

The [Details structure checkpoint](checkpoint-forty-eight.md) records all 3,201 component/live
API cases and all 15 final native Details cases passing. The affected 35-case native group
also passed before the final fullscreen typography adjustment. Refetch feedback stays outside
the reading area, desktop caps and prose match the original measurements, and technical facts
remain visible. Seven methods gain behavioral evidence and three retired CLR converter
assertions receive explicit classifications; 487 pending and 101 partial methods remain.

The [cover presentation checkpoint](checkpoint-forty-seven.md) records all 3,195
component/live API cases and 64 native artwork, gallery, cover and feed cases passing.
Ready pixels survive size upgrades and recycled surfaces remain independent. Twelve
source methods gain complete evidence; 497 pending and 101 partial methods remain.

The [shared sort checkpoint](checkpoint-forty-six.md) records all 3,190 component/live API
cases and all 17 final native merge/menu cases passing. Desktop Library and Merges share
the original compact menu; a queued-scroll race is fixed. Two more source methods are
complete, leaving 509 pending and 101 partial. The wider native suite remains due for a
combined final pass.

The [dedicated Merges checkpoint](checkpoint-forty-five.md) records all 35 native cases
passing, including the complete 22-case Avalon layout matrix. Desktop gains its direct
rail destination, Details return cursor and original keyboard scope; fullscreen keeps its
own path. Three more source methods have equivalent evidence. The inventory has 511
pending and 101 partial methods, so complete migration is still open.

The [merge surface checkpoint](checkpoint-forty-four.md) records all 20 native regression
cases passing. Answers identify their game group on both surfaces; desktop row feedback
retains the original timing and fixed geometry. Nine methods gain behavioral evidence and
three Avalonia-only guards receive explicit classifications. The inventory has 514 pending
and 101 partial methods; dedicated merge navigation and other source contracts remain open.

The [recommendation card checkpoint](checkpoint-forty-three.md) records all 3,170
component/live API cases and 47 final native cases passing. Desktop cards restore the
original feedback strip, cover actions, caption slots and receipts; previews stay bounded
and retain the accessible reason. All 12 original card methods gain Electron evidence.
The inventory has 526 pending and 101 partial methods, so migration remains incomplete.

The [complete native and backend audit checkpoint](checkpoint-forty-two.md) records all
295 native cases passing on frozen source at `147997eb`. Another 65 source methods retain
their unchanged production backend tests; all 101 corresponding Release cases passed.
The inventory now has 538 pending and 101 partial methods. This is still an incomplete
migration despite the green covered suites.

The [unread accessibility checkpoint](checkpoint-forty-one.md) records all 3,168
component/live API cases and 33 native cases passing. Cover/list names retain exact patch
counts, collection names explain their count, and feed reason/status checks cover both
surfaces. Seven source methods gain Electron evidence and one retains its backend test;
603 methods remain pending and 101 partial.

The [cover tile checkpoint](checkpoint-forty.md) records 3,154 passing component/live API
cases and all 20 final native cover checks. The first native regression exposed and then
verified fixes for double hover movement and stationary-pointer reattachment. All dashboard
and rating-cap follow-ups from checkpoint 39 pass. Seventeen more source methods have
complete migration evidence; the inventory still has 611 pending and 101 partial methods.

The [metadata settings checkpoint](checkpoint-thirty-nine.md) records 3,139 passing
component/live API cases, 138 backend HTTP cases, and 36 native regression cases. Seven
final native checks verify corrected reading-page typography, rules and alignment.
The full .NET Release suite passes 6,895 cases with two Linux-only skips on Windows.
The complete 273-case native run passed 271 and failed two: a fixture database lock
and a rating-cap input sent around a pending save. Targeted follow-ups pass in checkpoint 40;
checkpoint 39 records the failure evidence and the run's source-provenance limitation.

The [tray/window checkpoint](checkpoint-thirty-eight.md) records 3,111 passing component/API
cases across 153 files, with build and typecheck passing. All 44 focused native cases pass,
including the new tray matrix, Library, startup, activation, journal, Settings and updates.
The latest full native run on `d5f2994d` passes 259 cases, with two initial-readiness
timeouts and one Details fixture that measures before interface scale has reset. Its
report and follow-up requirements are in checkpoint 38. Both earlier Settings selector
failures from [checkpoint 37](checkpoint-thirty-seven.md) have passing follow-up evidence.
The last entirely passing combined run,
all 236 cases on `7c48c35b`, is in the [IGDB/startup checkpoint](checkpoint-thirty-five.md).
All 133 backend HTTP cases also pass there, including credential protection and rollback.
The migration inventory contains 1,124 ported, 559 retained backend, 17 framework-specific,
633 pending and 102 partial methods. The migration gate remains incomplete.
The [journal checkpoint](checkpoint-thirty-three.md)
records the latest full .NET pass: 6,869 passed and two Linux-only skips across 13 assemblies,
including all 896 Avalonia UI cases. The table below preserves earlier measured checkpoints.

All runtime checks use disposable directories beneath the repository's ignored `.tmp`
folder. No production library or launcher files are modified. Backend credentials and
Chromium profiles stay in those directories and are excluded from committed evidence.

| Check | Observed result |
|---|---|
| Library navigation checkpoint | All 2,438 Electron cases across 125 files and all eight native Library lifecycle cases passed. See [checkpoint eight](checkpoint-eight.md) for the source audit and validation limits. |
| Spending dashboard checkpoint | All 2,442 Electron cases across 126 files and 24 native Spending/core parity cases passed. See [checkpoint nine](checkpoint-nine.md) for the original fixture matrices, signed charts, controller reading access and validation limits. |
| Account and filter checkpoint | All 2,419 Electron cases across 125 files passed without skips, plus both native Spending checks. See [checkpoint seven](checkpoint-seven.md) for the exact audit scope and remaining gaps. |
| Full .NET Release solution | 6,772 passed; two Linux-only skips on Windows, across 13 assemblies. |
| Original Avalonia UI assembly | All 896 tests passed within the full Release run. |
| Electron unit/component and live API suite | All 1,705 tests passed across 99 files, without skips, against the isolated Debug backend. This run includes the account, feed, merge, navigation and shared details fact packages. |
| Live API coverage within that run | All eight integration tests passed, including journal conflict and metadata operation cases. |
| Rendered core Electron checkpoint | Twelve tests passed for startup, layout, search/details return, settings/activity, secondary-instance activation and simulated controller overlays. |
| Complete rendered Electron checkpoint | All 40 tests passed in one run at commit `a8c7ad0`. Later focused groups below pass; the expanded full rendered suite has not yet had a final combined pass. |
| Rendered Home and Library navigation | All 16 layout/navigation tests plus five typography tests passed together after saved paging, two-row retention and text/cover geometry corrections. |
| Rendered Steam imports | Both surfaces passed with immediate import through the real backend, deduplicated licence counts, per-source truncation notices and narrow-layout checks. |
| Rendered merge review additions | Four tests passed for retained answered slots, separation, sort-sheet focus and preferred storefront headers across both modes. |
| Rendered screenshot gallery | Three tests passed for desktop/fullscreen wheel containment, uncropped 1280×720 image caps, trapped focus, one-layer Escape and original-thumbnail return. See [details evidence](details-facts.md). |
| Rendered typography | Five tests passed for both surfaces, per-palette font retention, installed-font enumeration and combined theme/fullscreen text scaling. |
| Rendered account statistics | Two tests passed for currency boundaries, chart focus, narrow panels and enlarged text across both surfaces. |
| Native reading browser | Two tests passed for desktop/fullscreen navigation, toolbar and controller input against isolated HTTP/HTTPS fixtures. See [reader evidence](native-reader.md). |
| Native library and details | Six tests passed for grouping, immediate exact-act Undo, bulk relationship acceptance/Undo, and metadata changes updating an open live list while unsaved title text remains. |
| Native feed | Four tests passed for dated receipts, focused Undo, history after navigation, modal geometry and database-verified exposed-card impressions across both surfaces. See [feed evidence](feed.md). |
| Native authored themes | Two tests passed for palette selection, file watching, typography reset, export and saved transparency after reload across both surfaces. |
| Native account input | One intercepted-provider test passed for masked fullscreen input, cancellation, navigation invalidation and input locking. See [account evidence](account-input.md). |
| Native modal layout and focus | Six tests passed for short and long lists, trapped keyboard focus, Cancel-first destructive actions and retained fullscreen state. |
| Native Windows visual inspection | Acrylic blur and Mica tint were visibly distinct at maximum transparency; covers remained opaque. Physical F11 entered a solid 3440 × 1440 fullscreen surface and restored the prior desktop Library on return. |

The full .NET command was:

```powershell
dotnet test --configuration Release --artifacts-path .tmp/parity-final-artifacts `
  --logger trx --results-directory .tmp/parity-final-results `
  --blame-hang --blame-hang-timeout 5m
```

Electron checks run from `src/Winnow.Electron` with `WINNOW_BACKEND_PATH` pointing to
a Debug backend executable. `npm run test:integration` starts its own sample backend,
adds a synthetic recorded session, suppresses inherited IGDB credentials in that child,
runs the complete unit/component suite plus live tests, then stops the child backend.
`npm run test:rendered` builds and launches the actual Electron runtime. Screenshots and
the Playwright JSON report are written to `.tmp/electron-rendered-results`.

Automated rendered layout tests use a real Electron window at fixed client dimensions and deliver
the same mode-change event as native fullscreen. They measure the fullscreen presentation
but do not establish physical display fullscreen or controller hardware behavior.
Spending tests insert synthetic facts only into their newly created test database and
read them through the production API and preload boundary. Installed-font verification
uses Chromium's actual permission checks and the named font-catalogue bridge.

## Verification limits

The current passing suites cover substantially more behavior than the original Electron
implementation, but many source contracts remain pending or partial. Original suites
covering detailed focus, input, modal geometry, performance, source provenance and native
application lifecycle still require equivalent Electron evidence. The inventory records
that work explicitly instead of treating an aggregate green test run as migration completion.

Native materials can be requested on supported Windows versions; Electron does not expose
the compositor's active material as Avalonia does. A separate Windows compositor inspection
observed Acrylic blur, Mica tint and physical fullscreen in the disposable visual-test profile;
this does not establish behavior on other Windows versions or desktop backgrounds.
Unsupported/accessibility cases remain opaque. Actual NSIS/AppImage
installation, update recovery, physical controllers, TV-distance legibility, and Linux/macOS
packaging have not been verified by these tests. The existing release pipeline still ships
the Avalonia frontend. See [Electron updates](../../electron-updates.md) for updater boundaries.
