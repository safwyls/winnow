# Electron parity verification — 2026-09-28

Avalon now reproduces the original Winnow presentation in the Electron frontend, with
separate desktop and fullscreen paths. This evidence records the migration in progress;
it does not establish complete behavior or test parity. The Avalonia frontend and its
tests remain available while equivalent Electron coverage is added.

## Original contract inventory

`source-contracts.json` freezes 2,310 test methods from 290 files at
`cf45d9f1127243a987d3cf6e664a32fc767ecb67`. Collection includes every test in
`tests/Winnow.Ui.Tests`, recursively, and main test-project files mentioning `Winnow.App`
or importing Avalonia. Roslyn identifies attributed methods and their containing class,
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
| `ported` | An identified Electron test exercises the original behavioral contract. |
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

All runtime checks use disposable directories beneath the repository's ignored `.tmp`
folder. No production library or launcher files are modified. Backend credentials and
Chromium profiles stay in those directories and are excluded from committed evidence.

| Check | Observed result |
|---|---|
| Full .NET Release solution | 6,772 passed; two Linux-only skips on Windows, across 13 assemblies. |
| Original Avalonia UI assembly | All 896 tests passed within the full Release run. |
| Electron unit/component and live API suite | All 1,183 tests passed across 81 files, without skips, against the isolated Debug backend. |
| Live API coverage within that run | All eight integration tests passed, including journal conflict and metadata operation cases. |
| Rendered core Electron checkpoint | Twelve tests passed for startup, layout, search/details return, settings/activity, secondary-instance activation and simulated controller overlays. |
| Complete rendered Electron checkpoint | All 40 tests passed in one run, including the individual groups below, against the freshly built frontend and isolated Debug backend. |
| Rendered typography | Five tests passed for both surfaces, per-palette font retention, installed-font enumeration and combined theme/fullscreen text scaling. |
| Rendered account statistics | Two tests passed for currency boundaries, chart focus, narrow panels and enlarged text across both surfaces. |
| Native reading browser | Two tests passed for desktop/fullscreen navigation, toolbar and controller input against isolated HTTP/HTTPS fixtures. See [reader evidence](native-reader.md). |
| Native library and details | Six tests passed for grouping, immediate exact-act Undo, bulk relationship acceptance/Undo, and metadata changes updating an open live list while unsaved title text remains. |
| Native feed | Two tests passed for dated receipts, focused Undo, history after navigation and modal geometry across both surfaces. See [feed evidence](feed.md). |
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
implementation, but most source contracts remain pending or partial. Original suites
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
